"""Watch one VIVE tracker's buttons without starting ELTS or sending outputs.

Run with Calibration Environment/.venv/Scripts/python.exe and --serial SERIAL.
SteamVR must assign the tracker Held in hand and a left/right hand for legacy
button input. This monitor never changes tracker roles or calibration itself.
"""
import argparse
import json
from pathlib import Path
import time
import tkinter as tk

import openvr


class TriggerMonitor:
    def __init__(self, serial):
        self.serial = serial
        self.system = None
        self.previous = None
        self.presses = self.releases = 0
        self.started = time.monotonic()
        self.status = None
        folder = Path(__file__).resolve().parents[2] / "diagnostics" / "trigger-test"
        folder.mkdir(parents=True, exist_ok=True)
        self.log_path = folder / (time.strftime("trigger-%Y%m%d-%H%M%S") + ".jsonl")
        self.log = self.log_path.open("x", encoding="utf-8", buffering=1)
        self.record("start", serial=serial, note="Diagnostic only; host poll times, not hardware timestamps.")

        self.window = tk.Tk()
        self.window.title("VIVE gun trigger test")
        # Put the interactive test on the main screen, above other windows briefly.
        width, height = 760, 490
        left = max(0, (self.window.winfo_screenwidth() - width) // 2)
        top = max(0, (self.window.winfo_screenheight() - height) // 2)
        self.window.geometry(f"{width}x{height}+{left}+{top}")
        self.window.lift()
        self.window.attributes("-topmost", True)
        self.window.after(2000, lambda: self.window.attributes("-topmost", False))
        self.window.configure(bg="#16202b")
        self.window.protocol("WM_DELETE_WINDOW", self.close)
        self.label("VIVE GUN TRIGGER TEST", 19).pack(pady=(22, 5))
        self.label(serial, 12).pack()
        self.readout = self.label("CONNECTING", 35)
        self.readout.pack(pady=24)
        self.counts = self.label("Presses: 0     Releases: 0", 20)
        self.counts.pack()
        self.detail = self.label("Waiting for SteamVR…", 11)
        self.detail.pack(pady=14)
        self.label("Pull and release 5 times slowly. Then hold for 3 seconds and release.\n"
                   "Each pull should add one press; each release should add one release.\n"
                   "An unavailable input is never treated as a released trigger.", 12).pack(pady=12)
        self.label("Input diagnostic only — no ELTS session or apparatus commands.\n"
                   "Observed button changes are saved under diagnostics/trigger-test.", 10).pack(pady=12)
        self.window.after(100, self.poll)

    def label(self, text, size):
        return tk.Label(self.window, text=text, font=("Segoe UI", size),
                        bg="#16202b", fg="#e8eef4", wraplength=730)

    def record(self, event, **values):
        self.log.write(json.dumps({"elapsed_s": time.monotonic() - self.started,
                                   "event": event, **values}) + "\n")

    def unavailable(self, message):
        # Reconnects establish a new baseline; they must not invent button edges.
        self.previous = None
        self.readout.configure(text="INPUT UNAVAILABLE", fg="#efbd69")
        self.detail.configure(text=message)
        if message != self.status:
            self.record("unavailable", message=message)
        self.status = message

    def poll(self):
        delay_ms = 10
        try:
            if self.system is None:
                self.system = openvr.init(openvr.VRApplication_Background)
            index = None
            for candidate in range(openvr.k_unMaxTrackedDeviceCount):
                if self.system.getTrackedDeviceClass(candidate) not in (
                        openvr.TrackedDeviceClass_GenericTracker, openvr.TrackedDeviceClass_Controller):
                    continue
                if self.system.getStringTrackedDeviceProperty(candidate, openvr.Prop_SerialNumber_String) == self.serial:
                    index = candidate
                    break
            if index is None or not self.system.isTrackedDeviceConnected(index):
                self.unavailable("Tracker not connected. Switch it on and wait for its green light.")
                delay_ms = 250
            else:
                available, state = self.system.getControllerState(index)
                if not available:
                    self.unavailable("SteamVR sees the tracker, but button input is unavailable.\n"
                                     "Manage Trackers: Held in hand, then Right Hand or Left Hand.")
                    delay_ms = 100
                else:
                    # Pin 4 is the digital trigger; the legacy SteamVR trigger bit is 33.
                    pressed = bool(state.ulButtonPressed & (1 << openvr.k_EButton_SteamVR_Trigger))
                    raw = (state.ulButtonPressed, state.ulButtonTouched,
                           tuple((axis.x, axis.y) for axis in state.rAxis))
                    if self.previous is None or raw != self.previous:
                        self.record("baseline" if self.previous is None else "input", pressed=pressed,
                                    packet=state.unPacketNum, buttons=state.ulButtonPressed,
                                    touched=state.ulButtonTouched, axes=raw[2])
                    if self.previous is not None:
                        was_pressed = bool(self.previous[0] & (1 << openvr.k_EButton_SteamVR_Trigger))
                        if pressed != was_pressed:
                            self.presses += int(pressed)
                            self.releases += int(not pressed)
                    self.previous = raw
                    self.status = "available"
                    self.readout.configure(text="PRESSED" if pressed else "RELEASED",
                                           fg="#72dfa8" if pressed else "#e8eef4")
                    self.counts.configure(text=f"Presses: {self.presses}     Releases: {self.releases}")
                    self.detail.configure(text=f"Button input available · raw buttons {state.ulButtonPressed:#x}")
        except Exception as error:
            self.unavailable(f"{type(error).__name__}: {error}\nStart SteamVR; this window retries automatically.")
            openvr.shutdown()
            self.system = None
            delay_ms = 1000
        self.window.after(delay_ms, self.poll)

    def close(self):
        self.record("stop", presses=self.presses, releases=self.releases)
        self.log.close()
        openvr.shutdown()
        self.window.destroy()


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--serial", required=True, help="Gun tracker serial shown in SteamVR")
    args = parser.parse_args()
    monitor = TriggerMonitor(args.serial)
    monitor.window.mainloop()
