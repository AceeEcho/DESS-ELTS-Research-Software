"""Preview or copy the private data of a stopped ELTS station into a new folder."""
import argparse
import ctypes
import hashlib
import json
import os
import shutil
import sqlite3
import stat
import tempfile
from contextlib import ExitStack, closing, contextmanager
from pathlib import Path

COPY_INFO = ".elts-station-copy.json"
ROOT = Path(__file__).resolve().parents[2]


def plain_path(path):
    # Refuse junctions as well as symbolic links, including above the data root.
    for part in (path, *path.parents):
        try:
            info = part.lstat()
        except FileNotFoundError:
            continue
        attrs = getattr(info, "st_file_attributes", 0)
        if stat.S_ISLNK(info.st_mode) or attrs & stat.FILE_ATTRIBUTE_REPARSE_POINT:
            raise ValueError(f"Cannot copy through a linked path: {part}")
    return path


@contextmanager
def exclusive(path):
    """The Windows station and the calibration server use these same lease files."""
    if os.name != "nt":
        raise RuntimeError("Run the copy on the Windows station with ELTS and calibration closed.")
    import msvcrt
    from ctypes import wintypes
    create = ctypes.WinDLL("kernel32", use_last_error=True).CreateFileW
    create.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID,
                       wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
    create.restype = wintypes.HANDLE
    handle = create(str(plain_path(path)), 0xC0000000, 0, None, 4, 0x80, None)
    if handle == ctypes.c_void_p(-1).value:
        raise OSError(ctypes.get_last_error(), f"Close all station and calibration windows before copying {path.name}")
    with os.fdopen(msvcrt.open_osfhandle(handle, os.O_RDWR | os.O_BINARY), "rb") as lease:
        yield lease


def copy_database(source, target, new_root, indexed_root):
    """Keep session and exclusion identities while rebasing their recording paths."""
    with closing(sqlite3.connect(source.as_uri() + "?mode=ro", uri=True)) as old:
        with closing(sqlite3.connect(target)) as new:
            old.backup(new)
            with new:
                for table in ("sessions", "removed_recordings"):
                    exists = new.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name=?", (table,)).fetchone()
                    if not exists:
                        continue
                    for (directory,) in new.execute(f"SELECT directory FROM {table}").fetchall():
                        run = Path(directory)
                        if run.parent not in (source.parent, indexed_root) or run.name in ("", ".", ".."):
                            raise ValueError(f"Recording path falls outside the selected data folder: {run}")
                        new.execute(f"UPDATE {table} SET directory=? WHERE directory=?", (str(new_root / run.name), directory))
            if new.execute("PRAGMA integrity_check").fetchall() != [("ok",)] or new.execute("PRAGMA foreign_key_check").fetchone():
                raise ValueError("The copied database failed its integrity check")


def digest(path):
    checksum = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            checksum.update(chunk)
    return checksum.digest()


def configured_data_root(source):
    local = plain_path(source / "config/local.json")
    effective = plain_path(source / "ELTS-Synthetic_Data/StreamingAssets/config-generated/effective-config.json")
    if local.is_file():
        return json.loads(local.read_text(encoding="utf-8-sig"))["dataRoot"]
    if effective.is_file():
        return json.loads(effective.read_text(encoding="utf-8-sig"))["machine"]["dataRoot"]
    # Standalone station homes can lack application configuration. Detect the
    # two shipped locations and ask for the relative directory when ambiguous.
    candidates = [name for name in ("collection data", "data/synthetic") if (source / name).is_dir()]
    if len(candidates) > 1:
        raise ValueError("Multiple recording directories found. Specify the station's --data-root.")
    return candidates[0] if candidates else "collection data"


def walk_failure(error):
    # os.walk otherwise omits unreadable directories and could report a partial backup as complete.
    raise error


def station_copy(source, destination, relative=None, apply=False):
    if apply and os.name != "nt":
        raise RuntimeError("Run the copy on the Windows station with ELTS and calibration closed.")
    source = plain_path(Path(source).absolute()).resolve(strict=True)
    destination = plain_path(Path(destination).absolute()).resolve()
    metadata = plain_path(source / COPY_INFO)
    info = {}
    if metadata.exists():
        info = json.loads(metadata.read_text(encoding="utf-8"))
        if not isinstance(info, dict) or info.get("version") != 1 or not all(
            isinstance(info.get(key), str) for key in ("dataRoot", "indexedRoot")
        ) or not Path(info["indexedRoot"]).is_absolute():
            raise ValueError("Invalid station copy information")
    if relative is None:
        relative = info["dataRoot"] if info else configured_data_root(source)
    relative = Path(relative)
    if relative.anchor or ".." in relative.parts or relative == Path("."):
        raise ValueError("Use the station's relative dataRoot")
    if destination.exists() or source == destination or source in destination.parents or destination in source.parents:
        raise ValueError("Choose a new destination outside the source station")
    protected = [ROOT]
    if os.environ.get("ELTS_STATION_HOME"):
        protected.append(Path(os.environ["ELTS_STATION_HOME"]).resolve())
    if any(destination == location or location in destination.parents for location in protected):
        raise ValueError("Choose a destination outside the application directory and configured station home")
    if not destination.parent.is_dir():
        raise ValueError("The destination's parent directory must already exist")
    data = plain_path(source / relative)
    indexed_root = Path(info["indexedRoot"]) if info and Path(info["dataRoot"]) == relative else data
    profiles = plain_path(source / "Calibration Environment" / "profiles")
    room = plain_path(profiles.parent / "station-room.json")
    if not data.is_dir() and not profiles.is_dir() and not room.is_file():
        raise ValueError("No recordings directory or calibration profiles directory found at the source")
    if data in profiles.parents or profiles in data.parents or data == profiles:
        raise ValueError("The data and calibration directories must be separate")
    with ExitStack() as locks:
        generation = None
        if apply:
            # Acquire both leases on stations that have only profiles or only recordings.
            # Their first concurrent save uses these same directories and handles.
            data.mkdir(parents=True, exist_ok=True)
            profiles.mkdir(parents=True, exist_ok=True)
            locks.enter_context(exclusive(data / ".participant-store.lock"))
            generation = locks.enter_context(exclusive(profiles / ".dataset-generation")).read()
        files = []
        folders = []
        for directory in (data, profiles):
            if directory.is_dir():
                for current, children, names in os.walk(directory, onerror=walk_failure):
                    folder = Path(current)
                    folders.append(folder)
                    for child in children:
                        plain_path(folder / child)
                    for name in names:
                        path = plain_path(folder / name)
                        if path in (data / ".participant-store.lock", profiles / ".dataset-generation"):
                            continue
                        if folder == data and name in ("collection.sqlite-wal", "collection.sqlite-shm", "collection.sqlite-journal"):
                            continue
                        files.append(path)
        if room.is_file():
            files.append(room)
        if not apply:
            return len(files)
        with tempfile.TemporaryDirectory(prefix="elts-station-copy-", dir=destination.parent) as scratch:
            staging = Path(scratch) / "station"
            staging.mkdir()
            for folder in folders:
                (staging / folder.relative_to(source)).mkdir(parents=True, exist_ok=True)
            for path in files:
                target = staging / path.relative_to(source)
                target.parent.mkdir(parents=True, exist_ok=True)
                if path == data / "collection.sqlite":
                    copy_database(path, target, destination / relative, indexed_root)
                else:
                    shutil.copy2(path, target)
                    if digest(path) != digest(target):
                        raise IOError(f"Copy verification failed: {path.name}")
            if generation is not None:
                (staging / profiles.relative_to(source) / ".dataset-generation").write_bytes(generation)
            # After transport (for example on removable media), the copied SQLite
            # rows still point at this original destination until the next restore.
            # This small locator contains no inventory or participant data.
            (staging / COPY_INFO).write_text(json.dumps({
                "version": 1, "dataRoot": str(relative), "indexedRoot": str(destination / relative)
            }) + "\n", encoding="utf-8")
            # Publish only after all checks succeed. On Windows rename refuses an existing destination.
            staging.rename(destination)
    return len(files)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path, help="Station home or a previous verified copy")
    parser.add_argument("destination", type=Path, help="A new folder whose parent already exists")
    parser.add_argument("--data-root", help="Relative dataRoot from station configuration (default: collection data, or the copied station's setting)")
    parser.add_argument("--copy", action="store_true", help="Perform the verified copy (default is preview)")
    args = parser.parse_args()
    count = station_copy(args.source, args.destination, args.data_root, args.copy)
    print(f"{'Copied' if args.copy else 'Would copy'} {count} station files into {args.destination}.")
