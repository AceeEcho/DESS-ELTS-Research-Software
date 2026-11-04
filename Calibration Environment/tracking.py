"""Read-only OpenVR acquisition. No compositor, trigger, haptic or apparatus commands."""
import copy
import math
import threading
import time


def checked_matrix(matrix):
    """Keep native metres / right-handed axes. Reject malformed rigid transforms."""
    rows = [[float(matrix[r][c]) for c in range(4)] for r in range(3)]
    if not all(math.isfinite(v) for row in rows for v in row):
        raise ValueError("Nonfinite tracking matrix")
    for a in range(3):
        for b in range(3):
            dot = sum(rows[r][a] * rows[r][b] for r in range(3))
            if abs(dot - (1 if a == b else 0)) > 0.02:
                raise ValueError("Tracking matrix is not orthonormal")
    a, b, c = rows
    determinant = a[0]*(b[1]*c[2]-b[2]*c[1])-a[1]*(b[0]*c[2]-b[2]*c[0])+a[2]*(b[0]*c[1]-b[1]*c[0])
    if abs(determinant - 1) > 0.02:
        raise ValueError("Tracking matrix has invalid handedness")
    return rows


class TrackingBridge:
    def __init__(self):
        self.lock = threading.Lock()
        self.enabled = threading.Event()
        self.closed = threading.Event()
        self.state = {"status": "off", "message": "Live tracking is off.", "devices": [], "sequence": 0}
        self.sample_time = 0
        self.thread = threading.Thread(target=self._run, daemon=True, name="calibration-openvr")
        self.thread.start()

    def set_enabled(self, enabled):
        if enabled:
            self.enabled.set()
        else:
            self.enabled.clear()
        self._publish("connecting" if enabled else "off", "Connecting to SteamVR…" if enabled else "Live tracking is off.", [])

    def _publish(self, status, message, devices):
        with self.lock:
            self.sample_time = time.monotonic()
            self.state = {"status": status, "message": message, "devices": devices,
                          "sequence": self.state["sequence"] + 1}

    def snapshot(self):
        with self.lock:
            result = copy.deepcopy(self.state)
            result["ageMs"] = (time.monotonic() - self.sample_time) * 1000 if self.sample_time else None
        # A stalled native call must never leave an old pose advertised as usable.
        if result["ageMs"] is not None and result["ageMs"] > 500:
            for device in result["devices"]:
                device.update(valid=False, matrix=None, reason="stale")
        return result

    def _run(self):
        vr = system = None
        while not self.closed.is_set():
            if not self.enabled.is_set():
                if system is not None:
                    vr.shutdown()
                    system = None
                self.closed.wait(0.1)
                continue
            try:
                if system is None:
                    import openvr as vr
                    system = vr.init(vr.VRApplication_Background)
                # Zero prediction, one shared acquisition per pair. SteamVR's own
                # driver filtering is outside this app's control.
                poses = system.getDeviceToAbsoluteTrackingPose(vr.TrackingUniverseStanding, 0, vr.k_unMaxTrackedDeviceCount)
                devices = []
                classes = {vr.TrackedDeviceClass_GenericTracker: "tracker", vr.TrackedDeviceClass_Controller: "controller",
                           vr.TrackedDeviceClass_HMD: "headset", vr.TrackedDeviceClass_TrackingReference: "base station"}
                for index, pose in enumerate(poses):
                    kind = classes.get(system.getTrackedDeviceClass(index))
                    if kind is None:
                        continue
                    try:
                        serial = system.getStringTrackedDeviceProperty(index, vr.Prop_SerialNumber_String)
                    except Exception:
                        # Index is not a persistent binding; do not fabricate a serial.
                        continue
                    valid = bool(pose.bDeviceIsConnected and pose.bPoseIsValid and pose.eTrackingResult == vr.TrackingResult_Running_OK)
                    matrix = None
                    if valid:
                        try:
                            matrix = checked_matrix(pose.mDeviceToAbsoluteTracking)
                        except ValueError:
                            valid = False
                    devices.append({"serial": serial, "kind": kind, "connected": bool(pose.bDeviceIsConnected),
                                    "valid": valid, "matrix": matrix, "reason": "valid" if valid else f"unavailable ({pose.eTrackingResult})"})
                if self.enabled.is_set():
                    self._publish("connected", "SteamVR connected · standing origin · no added prediction", devices)
                self.closed.wait(1 / 60)
            except Exception as error:
                if system is not None:
                    try:
                        vr.shutdown()
                    except Exception:
                        pass
                system = None
                message = "Restart START-CALIBRATION.cmd and answer Y to set up live tracking." if isinstance(error, ImportError) else f"Start SteamVR and check paired devices. {type(error).__name__}: {error}"
                if self.enabled.is_set():
                    self._publish("unavailable", message, [])
                self.closed.wait(2)
        if system is not None:
            vr.shutdown()

    def close(self):
        self.closed.set()
        self.thread.join(timeout=3)
