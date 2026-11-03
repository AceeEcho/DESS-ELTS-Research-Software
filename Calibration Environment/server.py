"""Isolated loopback dashboard and profile storage; Python standard library only."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import secrets
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse
from urllib.request import Request, urlopen
import webbrowser

from tracking import TrackingBridge

ROOT = Path(__file__).resolve().parent
WEB = ROOT / "web"
DEFAULTS = json.loads((ROOT / "defaults.json").read_text(encoding="utf-8"))
IDENTITY = hashlib.sha256(str(ROOT).casefold().encode()).hexdigest()[:24]
APP = "elts-calibration-environment-v1"
MAX_BODY = 100_000


def validate_config(config):
    """Strict, bounded shape validation keeps imported profiles safe and editable."""
    def walk(value, sample, path):
        if path.endswith(".reference"):
            if value is None:
                return
            if not isinstance(value, dict) or set(value) != {"quaternion", "source", "serial"}:
                raise ValueError(f"{path}: invalid forward reference")
            q = value["quaternion"]
            if not isinstance(q, list) or len(q) != 4 or any(type(x) not in (int, float) or not math.isfinite(x) for x in q):
                raise ValueError(f"{path}: invalid quaternion")
            if abs(sum(x*x for x in q) - 1) > 0.001 or value["source"] not in ("manual", "live") or not isinstance(value["serial"], str) or len(value["serial"]) > 128:
                raise ValueError(f"{path}: invalid reference metadata")
        elif isinstance(sample, dict):
            if not isinstance(value, dict) or set(value) != set(sample):
                raise ValueError(f"{path}: fields do not match version 1")
            for key in sample:
                walk(value[key], sample[key], f"{path}.{key}")
        elif isinstance(sample, list):
            if not isinstance(value, list) or len(value) != len(sample):
                raise ValueError(f"{path}: expected {len(sample)} values")
            for index, example in enumerate(sample):
                walk(value[index], example, f"{path}[{index}]")
        elif isinstance(sample, bool):
            if type(value) is not bool:
                raise ValueError(f"{path}: expected a boolean")
        elif isinstance(sample, (int, float)):
            if type(value) not in (int, float) or not math.isfinite(value) or abs(value) > 10000:
                raise ValueError(f"{path}: expected a finite, bounded number")
        elif not isinstance(value, str) or len(value) > 128:
            raise ValueError(f"{path}: invalid text")
    walk(config, DEFAULTS, "config")
    if config["version"] != 1 or config["coordinateSystem"] != DEFAULTS["coordinateSystem"]:
        raise ValueError("Unsupported profile version or coordinate system")
    for section, fields in {"screen": ["width", "height"], "room": ["width", "depth", "height"], "person": ["height", "radius"], "weapon": ["length"], "view": ["near", "far", "virtualDepth", "staleMs"]}.items():
        for field in fields:
            if not 0.001 <= config[section][field] <= 100:
                if section == "view" and field == "staleMs" and 100 <= config[section][field] <= 500:
                    continue
                raise ValueError(f"{section}.{field}: outside supported range")
    if not 100 <= config["view"]["staleMs"] <= 500:
        raise ValueError("Stale threshold must be 100–500 ms")
    if config["view"]["near"] >= config["view"]["far"]:
        raise ValueError("Near clipping distance must be smaller than far distance")
    if config["screen"]["bottom"] < 0 or config["person"]["height"] < 2 * config["person"]["radius"]:
        raise ValueError("Check screen bottom and person dimensions")
    if config["head"]["serial"] and config["head"]["serial"] == config["weapon"]["serial"]:
        raise ValueError("Assign different trackers to head and weapon")
    return config


def profile_path(name):
    if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9 _-]{0,63}", name):
        raise ValueError("Use 1–64 letters, numbers, spaces, hyphens or underscores for the profile name")
    return ROOT / "profiles" / (name + ".json")


class Server(ThreadingHTTPServer):
    daemon_threads = True
    def __init__(self, address):
        super().__init__(address, Handler)
        self.token = secrets.token_urlsafe(32)
        self.bridge = TrackingBridge()
        self.profile_lock = threading.Lock()


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(WEB), **kwargs)

    def log_message(self, *args):
        pass

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Content-Security-Policy", "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'")
        super().end_headers()

    def json_response(self, data, code=200):
        payload = json.dumps(data, allow_nan=False).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        self.end_headers()
        self.wfile.write(payload)

    def local_request(self):
        expected = f"127.0.0.1:{self.server.server_port}"
        return self.headers.get("Host") == expected and self.headers.get("Origin", f"http://{expected}") == f"http://{expected}"

    def do_GET(self):
        if not self.local_request():
            self.json_response({"error": "Only this local dashboard may access the server"}, 403)
            return
        parsed = urlparse(self.path)
        try:
            if parsed.path == "/api/state":
                self.json_response({"app": APP, "workspace": IDENTITY, "token": self.server.token, "defaults": DEFAULTS,
                                    "profiles": sorted(p.stem for p in (ROOT / "profiles").glob("*.json"))})
            elif parsed.path == "/api/tracking":
                self.json_response(self.server.bridge.snapshot())
            elif parsed.path == "/api/profile":
                path = profile_path(parse_qs(parsed.query).get("name", [""])[0])
                self.json_response(validate_config(json.loads(path.read_text(encoding="utf-8"))))
            elif parsed.path.startswith("/api/"):
                self.json_response({"error": "Unknown endpoint"}, 404)
            else:
                super().do_GET()
        except (ValueError, FileNotFoundError) as error:
            self.json_response({"error": str(error)}, 400)

    def do_POST(self):
        if not self.local_request() or not secrets.compare_digest(self.headers.get("X-Calibration-Token", ""), self.server.token):
            self.json_response({"error": "Refresh this local dashboard before making changes"}, 403)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= MAX_BODY or self.headers.get_content_type() != "application/json":
                raise ValueError("Expected a JSON body under 100 KB")
            body = json.loads(self.rfile.read(size))
            if self.path == "/api/profile":
                config = validate_config(body["config"])
                path = profile_path(body["name"])
                with self.server.profile_lock:
                    path.parent.mkdir(exist_ok=True)
                    temporary = path.with_suffix(".tmp")
                    # Flush then atomically replace: interrupted saves do not truncate profiles.
                    with temporary.open("w", encoding="utf-8") as stream:
                        json.dump(config, stream, indent=2, allow_nan=False)
                        stream.flush()
                        os.fsync(stream.fileno())
                    temporary.replace(path)
                self.json_response({"saved": path.name})
            elif self.path == "/api/validate":
                self.json_response(validate_config(body))
            elif self.path == "/api/tracking":
                if type(body.get("enabled")) is not bool:
                    raise ValueError("enabled must be true or false")
                self.server.bridge.set_enabled(body["enabled"])
                self.json_response({"ok": True})
            elif self.path == "/api/shutdown":
                self.json_response({"ok": True})
                threading.Thread(target=self.server.shutdown, daemon=True).start()
            else:
                self.json_response({"error": "Unknown endpoint"}, 404)
        except (ValueError, KeyError, TypeError, OSError) as error:
            self.json_response({"error": str(error)}, 400)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=8766)
    parser.add_argument("--no-browser", action="store_true")
    parser.add_argument("--stop", action="store_true")
    args = parser.parse_args()
    url = f"http://127.0.0.1:{args.port}"
    try:
        with urlopen(url + "/api/state", timeout=2) as response:
            existing = json.load(response)
    except Exception:
        existing = None
    if existing:
        if existing.get("app") != APP or existing.get("workspace") != IDENTITY:
            raise SystemExit("This port belongs to another app or project copy. Use --port with another port.")
        if args.stop:
            request = Request(url + "/api/shutdown", data=b"{}", headers={"Content-Type": "application/json", "X-Calibration-Token": existing["token"]})
            urlopen(request, timeout=3).close()
            print("Calibration Environment stopped.")
        elif not args.no_browser:
            webbrowser.open(url)
        return
    if args.stop:
        print("No Calibration Environment is running on this port.")
        return
    try:
        server = Server(("127.0.0.1", args.port))
    except OSError as error:
        raise SystemExit(f"Cannot open port {args.port}: {error}. Choose another --port.") from error
    print(f"Calibration Environment: {url}\nKeep this window open. Ctrl+C or STOP-CALIBRATION.cmd stops this app.", flush=True)
    if not args.no_browser:
        webbrowser.open(url)
    try:
        server.serve_forever(poll_interval=0.2)
    except KeyboardInterrupt:
        pass
    finally:
        server.bridge.close()
        server.server_close()


if __name__ == "__main__":
    main()
