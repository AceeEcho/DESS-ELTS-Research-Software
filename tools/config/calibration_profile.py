"""Map dashboard geometry into the synthetic ELTS rig contract."""
import math


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


def apply_profile(rig, profile, profile_hash):
    if (type(profile["version"]) is not int or profile["version"] != 1
            or profile["coordinateSystem"] != "RH_X-right_Y-up_Z-toward-viewer_metres_YXZ-degrees"):
        raise ValueError("Unsupported dashboard profile version or coordinate system")
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
    rig["headEyeOffsetM"] = _tracker_local(profile["head"], profile["head"]["eye"])
    weapon = profile["weapon"]
    rig["weaponMuzzleOffsetM"] = _tracker_local(weapon, weapon["muzzle"])
    bore = _rotate([0, 0, -1], weapon["bore"])
    rig["weaponBoreLocalDirection"] = _tracker_local(weapon, bore, direction=True)
    # The bore already incorporates the dashboard's correction.
    rig["weaponZeroQuaternionXyzw"] = [0, 0, 0, 1]
    rig["calibration"]["calibrationId"] = "synthetic-profile-" + profile_hash
