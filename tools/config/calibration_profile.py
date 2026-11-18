"""Map dashboard geometry into the synthetic ELTS rig contract.

Room-level screen geometry is staged into the build. Head/weapon offsets belong to
one participant and are applied by the Unity runtime when that participant
starts (ParticipantCalibration.cs); participant_offsets mirrors that conversion
so the two implementations can be checked against the same expected values.
"""
import math

# Keep aligned with "Calibration Environment/server.py".
PARTICIPANT_FORMAT = "elts-participant-calibration-v1"
COORDINATE_SYSTEM = "RH_X-right_Y-up_Z-toward-viewer_metres_YXZ-degrees"


def unwrap(saved):
    """Return the dashboard configuration from a participant file or a legacy profile."""
    if isinstance(saved, dict) and "format" in saved:
        if saved["format"] != PARTICIPANT_FORMAT:
            raise ValueError("Unsupported participant calibration format")
        return saved["calibration"]
    return saved


def _number(value):
    if type(value) not in (int, float) or not math.isfinite(value) or abs(value) > 10_000:
        raise ValueError("Expected a finite, bounded profile number")
    return value


def _vector(values):
    if not isinstance(values, list) or len(values) != 3:
        raise ValueError("Expected three profile coordinates")
    return [_number(value) for value in values]


def _rotate(vector, degrees, *, inverse=False):
    """Apply the dashboard's YXZ Euler rotation (Z first for a point)."""
    pitch, yaw, roll = map(math.radians, _vector(degrees))
    rotations = [(0, 1, roll), (1, 2, pitch), (2, 0, yaw)]
    if inverse:
        rotations = [(a, b, -angle) for a, b, angle in reversed(rotations)]
    result = list(vector)
    for a, b, angle in rotations:
        c, s = math.cos(angle), math.sin(angle)
        result[a], result[b] = c * result[a] - s * result[b], s * result[a] + c * result[b]
    return result


def _unity(vector):
    x, y, z = vector
    return [x, y, -z]


def _tracker_local(role, point, *, direction=False):
    # The dashboard's mount is tracker-on-object. The Unity rig expects
    # tracker-local offsets, so undo the mount before reflecting Z.
    mount = role["mount"]
    if not direction:
        point = [value - offset for value, offset in zip(_vector(point), _vector(mount["position"]))]
    return _unity(_rotate(point, mount["rotation"], inverse=True))


def _check_version(profile):
    if (type(profile["version"]) is not int or profile["version"] != 1
            or profile["coordinateSystem"] != COORDINATE_SYSTEM):
        raise ValueError("Unsupported dashboard profile version or coordinate system")


def room_identity(profile):
    """The values staged into the build; a change here requires a rebuild."""
    return {"screen": profile["screen"], "weaponSerial": profile["weapon"].get("serial", "")}


def apply_room(rig, profile, room_hash):
    """Stage the shared screen rectangle. Participant offsets stay at source values."""
    _check_version(profile)
    screen = profile["screen"]
    width, height = _number(screen["width"]), _number(screen["height"])
    if not 0.001 <= width <= 100 or not 0.001 <= height <= 100:
        raise ValueError("Screen width and height must be within 0.001–100 metres")
    half_width = width / 2
    bottom = _vector([screen["x"], screen["bottom"], screen["z"]])
    for key, local in (
        ("lowerLeftM", [-half_width, 0, 0]),
        ("lowerRightM", [half_width, 0, 0]),
        ("upperLeftM", [-half_width, height, 0]),
    ):
        offset = _rotate(local, screen["rotation"])
        rig["display"][key] = _unity([a + b for a, b in zip(bottom, offset)])
    rig["screenBottomHeightAboveFloorM"] = screen["bottom"]
    rig["calibration"]["calibrationId"] = "synthetic-room-" + room_hash


def participant_offsets(profile):
    """Tracker-local eye, muzzle and bore in Unity axes for one participant."""
    _check_version(profile)
    weapon = profile["weapon"]
    bore = _rotate([0, 0, -1], weapon["bore"])
    return {
        "headEyeOffsetM": _tracker_local(profile["head"], profile["head"]["eye"]),
        "weaponMuzzleOffsetM": _tracker_local(weapon, weapon["muzzle"]),
        # The bore already incorporates the dashboard's correction.
        "weaponBoreLocalDirection": _tracker_local(weapon, bore, direction=True),
    }
