"""Isolated loopback dashboard and profile storage; Python standard library only."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
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
def profiles_directory():
    home = os.environ.get("ELTS_STATION_HOME", "")
    if not home:
        return ROOT / "profiles"
    if not Path(home).is_absolute():
        raise ValueError("ELTS_STATION_HOME must be an absolute directory.")
    return Path(home) / "Calibration Environment" / "profiles"


IDENTITY = hashlib.sha256(f"{ROOT}|{profiles_directory()}".casefold().encode()).hexdigest()[:24]
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


# Participant calibrations are keyed by a positive whole number so ELTS can find
# them from the participant ID typed on its dashboard. Keep these values aligned
# with ParticipantCalibration in unity/Assets/ELTS/Config and tools/config/stage.py.
PARTICIPANT_FORMAT = "elts-participant-calibration-v1"
MAX_PARTICIPANT_ID = 999_999
PARTICIPANT_FILE = re.compile(r"participant-(0|[1-9][0-9]{0,5})\.json")


def participant_id(value):
    """Accept a whole number or a digits-only string; reject everything else."""
    if type(value) is int:
        number = value
    elif isinstance(value, str) and re.fullmatch(r"[0-9]{1,6}", value.strip()):
        number = int(value.strip())
    else:
        raise ValueError(f"Participant ID must be a whole number from 0 to {MAX_PARTICIPANT_ID}")
    if not 0 <= number <= MAX_PARTICIPANT_ID:
        raise ValueError(f"Participant ID must be a whole number from 0 to {MAX_PARTICIPANT_ID}")
    return number


def participant_path(number):
    return profiles_directory() / f"participant-{participant_id(number)}.json"


def dataset_generation():
    """A station reset changes this value, invalidating drafts in other browser tabs."""
    try:
        return (profiles_directory() / ".dataset-generation").read_text(encoding="ascii")
    except FileNotFoundError:
        return ""


def draft_path(key):
    if not isinstance(key, str) or not re.fullmatch(r"[0-9a-f]{32}", key):
        raise ValueError("Invalid calibration draft ID")
    return profiles_directory() / f".draft-{key}.json"


def validate_draft(body):
    # Whitelist persisted fields. Draft measurements are in metres.
    draft = {"config": validate_config(body["config"])}
    code = body.get("participantId", "")
    draft["participantId"] = "" if code == "" else str(participant_id(code))
    for field, minimum in (("resetHeight", 0), ("resetDistance", 0.25)):
        if field in body:
            number = body[field]
            if type(number) not in (int, float) or not minimum <= number <= 10:
                raise ValueError("Invalid draft measurement")
            draft[field] = number
    reference = body.get("resetReference", "head")
    if reference not in ("head", "eye"):
        raise ValueError("Invalid draft reference")
    draft["resetReference"] = reference
    return draft


def read_participant(path):
    """Load and fully validate one saved participant calibration file."""
    record = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(record, dict) or set(record) - {"name"} != {"format", "participantId", "savedUtc", "calibration"}:
        raise ValueError(f"{path.name}: not a participant calibration")
    if record["format"] != PARTICIPANT_FORMAT:
        raise ValueError(f"{path.name}: unsupported participant calibration format")
    number = participant_id(record["participantId"])
    if path.name != f"participant-{number}.json":
        raise ValueError(f"{path.name}: participant ID does not match the file name")
    return {"participantId": number,
            "savedUtc": str(record["savedUtc"])[:40], "calibration": validate_config(record["calibration"])}


def saved_participants():
    """Keep unreadable IDs reserved so a new calibration cannot overwrite them."""
    result = []
    for path in profiles_directory().glob("participant-*.json"):
        match = PARTICIPANT_FILE.fullmatch(path.name)
        if not match:
            continue
        try:
            record = read_participant(path)
            result.append({"id": record["participantId"], "savedUtc": record["savedUtc"], "available": True})
        except (ValueError, OSError, KeyError, TypeError):
            result.append({"id": int(match[1]), "savedUtc": "", "available": False})
    return sorted(result, key=lambda item: item["id"])


def save_room(config):
    # A room is shared across visits. Never retain participant fields or offsets here.
    room = {key: config[key] for key in ("version", "coordinateSystem", "screen")}
    for role in ("head", "weapon"):
        room[role] = {"serial": config[role]["serial"]}
    write_atomic(profiles_directory().parent / "station-room.json", room)


def preserve_current_room(config):
    if (profiles_directory().parent / "station-room.json").exists():
        return
    # Preserve the room an existing checkout used before it had a separate room file.
    candidates = [p for p in profiles_directory().glob("*.json")
                  if re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9 _-]{0,63}\.json", p.name)]
    for path in sorted(candidates, key=lambda p: (p.stat().st_mtime_ns, p.name), reverse=True):
        try:
            if PARTICIPANT_FILE.fullmatch(path.name):
                config = read_participant(path)["calibration"]
            else:
                config = validate_config(json.loads(path.read_text(encoding="utf-8")))
            break
        except (ValueError, OSError, KeyError, TypeError):
            continue
    save_room(config)


def write_atomic(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(".tmp")
    # Flush then atomically replace: interrupted saves do not truncate profiles.
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


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
                                    "participants": saved_participants(), "generation": dataset_generation()})
            elif parsed.path == "/api/dataset":
                self.json_response({"generation": dataset_generation()})
            elif parsed.path == "/api/draft":
                path = draft_path(parse_qs(parsed.query).get("id", [""])[0])
                self.json_response(json.loads(path.read_text(encoding="utf-8")) if path.exists() else None)
            elif parsed.path == "/api/tracking":
                self.json_response(self.server.bridge.snapshot())
            elif parsed.path == "/api/participant":
                path = participant_path(parse_qs(parsed.query).get("id", [""])[0])
                self.json_response(read_participant(path))
            elif parsed.path.startswith("/api/"):
                self.json_response({"error": "Unknown endpoint"}, 404)
            else:
                super().do_GET()
        except (ValueError, OSError) as error:
            self.json_response({"error": str(error)}, 400)

    @contextmanager
    def storage_write(self):
        with self.server.profile_lock:
            profiles = profiles_directory()
            profiles.mkdir(parents=True, exist_ok=True)
            # Removal and the offline copy tool hold this file exclusively.
            with (profiles / ".dataset-generation").open("a+b") as marker:
                marker.seek(0)
                if self.headers.get("X-Dataset-Generation", "") != marker.read().decode("ascii"):
                    raise ValueError("Participant data was removed. Reload the calibration workspace.")
                yield

    def do_POST(self):
        if not self.local_request() or not secrets.compare_digest(self.headers.get("X-Calibration-Token", ""), self.server.token):
            self.json_response({"error": "Refresh this local dashboard before making changes"}, 403)
            return
        try:
            size = int(self.headers.get("Content-Length", "0"))
            if not 0 < size <= MAX_BODY or self.headers.get_content_type() != "application/json":
                raise ValueError("Expected a JSON body under 100 KB")
            body = json.loads(self.rfile.read(size))
            if self.path == "/api/participant":
                config = validate_config(body["config"])
                number = participant_id(body["participantId"])
                record = {"format": PARTICIPANT_FORMAT, "participantId": number,
                          "savedUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"),
                          "calibration": config}
                path = participant_path(number)
                with self.storage_write():
                    if path.exists():
                        try:
                            read_participant(path)
                        except (ValueError, OSError, KeyError, TypeError) as error:
                            raise ValueError(f"Participant {number} has an unreadable calibration. Choose another ID.") from error
                    preserve_current_room(config)
                    write_atomic(path, record)
                self.json_response({"saved": number})
            elif self.path == "/api/draft":
                path, record = draft_path(body.get("id")), validate_draft(body)
                with self.storage_write():
                    write_atomic(path, record)
                self.json_response({"ok": True})
            elif self.path == "/api/room":
                config = validate_config(body)
                with self.storage_write():
                    save_room(config)
                self.json_response({"saved": True})
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
