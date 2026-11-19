#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Elts.Calibration;
using Elts.Geometry;
using Elts.Logging;
using Elts.Session;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>What the participant screen shows while a tracking step is running.</summary>
    public sealed class LiveOverlayContent
    {
        public string Heading = "", Instruction = "", Status = "", Keys = "";
        public bool ShowDot;
        /// <summary>Dot centre as fractions of the screen (0,0 = lower left).</summary>
        public float DotU, DotV;
        /// <summary>Dot diameter as a fraction of the screen width.</summary>
        public float DotDiameterFraction;
    }

    /// <summary>
    /// Live VIVE tracking for the participant station.
    ///
    /// The shot is the sight line as the shooter sees it: from the aiming eye
    /// through the point on the glass under the sights. That is exactly what the
    /// mouse rehearsal already does (eye plus a point on the screen), so the live
    /// trackers feed the same pose pipeline and every scoring, logging and replay
    /// path stays as it was. The published head pose is the tracked head; the
    /// published weapon pose is the sight-line ray. Raw tracker poses and every
    /// calibration in force are written to the recording as events.
    ///
    /// Station calibration, a shooter's zero and the quick check all ask the
    /// shooter to aim at dots and squeeze the trigger. Nothing here measures
    /// physical accuracy; the fit only reports how well the captures agree.
    /// </summary>
    public sealed partial class DevelopmentSessionPanel
    {
        public const string LiveSourceIdentity = "vive-sight-line-through-screen-v1";
        /// <summary>How long recent tracker frames are kept for the pre-trigger average (seconds).</summary>
        private const double LiveFrameHistorySeconds = 1.0;
        /// <summary>A second pull straight after the last dot must not save a station fit by accident (seconds).</summary>
        private const double LiveAcceptDelaySeconds = 1.0;

        private readonly struct LiveFrame
        {
            public LiveFrame(double time, RigidPose? gun, RigidPose? head) { Time = time; Gun = gun; Head = head; }
            public readonly double Time;
            public readonly RigidPose? Gun, Head;
        }

        private sealed class LiveProcedure
        {
            public string Kind = "";
            public string[] Titles = Array.Empty<string>(), Instructions = Array.Empty<string>();
            public double[][][] Dots = Array.Empty<double[][]>();
            public int Spot, Dot;
            public readonly List<AimCapture> Captures = new List<AimCapture>();
            public string Status = "", Summary = "";
            public SightLineFit? Fit;
            public EyeEstimate? Eye;
            public bool Failed, FromTutorial;
            public double SolvedAt = -1, CloseAt = -1;
            public int TotalDots => Dots.Sum(spot => spot.Length);
        }

        private LiveTrackerReader? liveReader;
        private SteamVrHealthWatch? liveHealth;
        private string liveHealthNote = "";
        private LiveTrackingSettings liveSettings = new LiveTrackingSettings();
        private LiveStationCalibration? liveStation;
        private SightLineModel? liveModel;
        private Vector3d? liveEye;
        private bool liveEnabled = true, liveMarker = true, liveTriggerWasPressed, liveAimValid;
        private string liveHeadSerial = "", liveNotice = "";
        private LiveTrackerSample liveHead = LiveTrackerSample.Missing("", "not assigned"), liveGun = LiveTrackerSample.Missing("", "not assigned");
        private double liveAimU, liveAimV, liveNextRawLog, liveHeadResolveAt;
        private readonly List<LiveFrame> liveFrames = new List<LiveFrame>();
        private RigidPose? livePublishedGun, livePublishedHead;
        private bool livePublishedAimValid;
        private double livePublishedAimU, livePublishedAimV;
        private RigidPose? liveLastRenderedHead;
        private double liveLastRenderedHeadAt = -1;
        private LiveProcedure? liveProcedure;

        private string LiveStationFolder => Path.GetDirectoryName(CalibrationProfileDirectory) ?? CalibrationProfileDirectory;
        private string LiveCalibrationPath => Path.Combine(LiveStationFolder, "station-tracking.json");
        private string LiveSettingsPath => Path.Combine(LiveStationFolder, "live-tracking.json");
        private string ConfiguredGunSerial => view.Configuration.Machine.WeaponTriggerSerial ?? "";
        /// <summary>Found from SteamVR, or taken from the saved station fit, when no serial is configured.</summary>
        private string liveDetectedGunSerial = "";
        /// <summary>The gun tracker: the configured serial when there is one, otherwise the hand-held tracker SteamVR reports.</summary>
        private string LiveGunSerial => ConfiguredGunSerial.Length > 0 ? ConfiguredGunSerial : liveDetectedGunSerial;

        /// <summary>Tracked poses are driving the station in place of the mouse.</summary>
        public bool LiveTrackingActive => SeparateAdministrator && liveReader != null && liveEnabled && liveModel != null;
        /// <summary>The gun step's aim dots are done and the operator is reviewing them.</summary>
        public bool TutorialLiveZeroDone => LiveTrackingActive && tutorial.Stage == TutorialStage.GunCalibration &&
            calibration.State == Elts.Calibration.CalibrationWizardState.Review;
        public bool LiveProcedureRunning => liveProcedure != null;
        public bool LiveMarkerVisible => liveMarker;
        /// <summary>Where the sights point on the glass, as fractions of the screen.</summary>
        public (bool valid, float u, float v) LiveAim => (liveAimValid, (float)liveAimU, (float)liveAimV);

        /// <summary>A short line for the participant window when live tracking needs the operator's attention.</summary>
        public string LiveBanner
        {
            get
            {
                if (liveReader == null || liveProcedure != null || !SeparateAdministrator) return "";
                if (!LiveTrackingActive)
                {
                    // Only while no participant is in progress: how to turn the trackers on.
                    if (!StationCanStartParticipant) return "";
                    // A mouse-only station with no SteamVR and no saved fit has nothing to be told.
                    if (!liveReader.Connected && liveStation == null) return "";
                    if (!liveEnabled) return "MOUSE INPUT · F10 switches to the trackers";
                    return "LIVE TRACKING · " + (liveNotice.Length > 0 ? liveNotice : "station not calibrated") + " · F6 calibrates the station";
                }
                if (!liveReader.Connected) return "TRACKING · " + liveReader.Status;
                if (!liveGun.Valid) return "GUN TRACKER · " + liveGun.Status;
                if (!liveHead.Valid) return "HEAD TRACKER · " + liveHead.Status;
                // Shown only between tests: SteamVR moved its frame, so the aim wants a ten-second check.
                bool betweenTests = engine == null || (engine.State != SessionState.BlockRunning && engine.State != SessionState.BlockPaused);
                return betweenTests ? liveHealthNote : "";
            }
        }

        public object LiveTrackingState => new
        {
            available = liveReader != null, active = LiveTrackingActive, enabled = liveEnabled, marker = liveMarker,
            steamVr = liveReader?.Status ?? "Live tracking is off (no gun tracker serial, or an automated run).",
            gunSerial = liveReader == null ? "" : LiveGunSerial, headSerial = liveHeadSerial,
            gun = liveGun.Status, head = liveHead.Status, calibrated = liveModel != null,
            savedUtc = liveStation?.SavedUtc ?? "", rmsDegrees = liveStation?.RmsDegrees, maxDegrees = liveStation?.MaxDegrees,
            captures = liveStation?.CaptureCount, procedure = liveProcedure?.Kind ?? "", notice = liveNotice,
            health = liveHealthNote, steamVrLogFollowed = liveHealth?.Following == true,
            frameChangesSinceAimCheck = liveHealth?.FrameChanges ?? 0, trackerResetsSinceAimCheck = liveHealth?.TrackerResets ?? 0,
            aim = liveAimValid ? new[] { liveAimU, liveAimV } : null,
        };

        private void InitializeLiveTracking()
        {
            liveSettings = LiveTrackingSettings.Load(LiveSettingsPath, out string settingsMessage);
            liveMarker = liveSettings.showAimMarker;
            liveReader = new LiveTrackerReader();
            liveHealth = new SteamVrHealthWatch();
            liveStation = LiveStationCalibration.Load(LiveCalibrationPath, out string calibrationMessage);
            liveNotice = settingsMessage.Length > 0 ? settingsMessage : calibrationMessage;
            // Until SteamVR answers, assume the gun tracker the station fit was saved with.
            if (ConfiguredGunSerial.Length == 0 && liveStation != null) liveDetectedGunSerial = liveStation.GunSerial;
            AdoptStationCalibration();
        }

        /// <summary>Starts from the saved station fit: every participant begins from the same room and gun facts.</summary>
        private void AdoptStationCalibration()
        {
            liveModel = null; liveEye = null;
            if (liveStation != null)
            {
                if (!liveStation.MatchesScreen(view.Configuration.Rig.Display))
                    liveNotice = "The screen size changed since the tracking calibration was saved. Calibrate the station again (F6).";
                else if (liveStation.GunSerial != LiveGunSerial)
                    liveNotice = "The tracking calibration was saved for another gun tracker. Calibrate the station again (F6).";
                else { liveModel = liveStation.Model; liveEye = liveStation.EyeInHeadTracker; }
            }
            view.SetLiveEyeOffset(LiveTrackingActive ? liveEye : null);
        }

        /// <summary>
        /// With no serial configured, the gun is the tracker SteamVR presents as
        /// hand-held: the role its trigger needs anyway. A tracker already chosen is
        /// kept while it stays connected.
        /// </summary>
        private void ResolveGunSerial()
        {
            if (ConfiguredGunSerial.Length > 0 || liveReader == null || !liveReader.Connected) return;
            var handHeld = liveReader.HandHeldSerials();
            if (handHeld.Count == 0 || handHeld.Contains(liveDetectedGunSerial)) return;
            liveDetectedGunSerial = handHeld[0];
            // A different gun tracker than the saved fit: say so before anyone aims with it.
            if (liveProcedure == null && StationCanStartParticipant) AdoptStationCalibration();
        }

        /// <summary>Read SteamVR first each frame, so this reader holds the connection the trigger reader then shares.</summary>
        private void PollLiveTrackers()
        {
            if (liveReader == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            liveReader.Poll(now);
            if (now >= liveHeadResolveAt || liveHeadSerial.Length == 0)
            {
                liveHeadResolveAt = now + 1;
                ResolveGunSerial();
                // The head tracker is the other one; prefer a tracker that is not hand-held.
                var handHeld = liveReader.HandHeldSerials();
                liveHeadSerial = liveSettings.headSerial.Length > 0 ? liveSettings.headSerial
                    : liveReader.ConnectedSerials().Where(serial => serial != LiveGunSerial)
                        .OrderBy(serial => handHeld.Contains(serial) ? 1 : 0).FirstOrDefault() ?? "";
            }
            liveGun = LiveGunSerial.Length == 0 && liveReader.Connected
                ? LiveTrackerSample.Missing("", "not found: set the gun tracker to Held in hand in SteamVR > Manage Trackers")
                : liveReader.Sample(LiveGunSerial);
            liveHead = liveReader.Sample(liveHeadSerial);
            liveFrames.Add(new LiveFrame(now, liveGun.Pose, liveHead.Pose));
            int stale = 0;
            while (stale < liveFrames.Count && liveFrames[stale].Time < now - LiveFrameHistorySeconds) stale++;
            if (stale > 0) liveFrames.RemoveRange(0, stale);
            liveAimValid = false;
            if (liveModel != null && liveGun.Pose.HasValue && liveModel.TryGlassPoint(liveGun.Pose.Value, out double u, out double v, out _))
            {
                var screen = view.Configuration.Rig.Display;
                liveAimU = u / screen.Width; liveAimV = v / screen.Height; liveAimValid = true;
            }
        }

        /// <summary>After the trigger has been read: advance any tracking step and write raw poses.</summary>
        private void UpdateLiveTracking()
        {
            if (liveReader == null) return;
            double now = Time.realtimeSinceStartupAsDouble;
            bool pressed = viveTrigger != null && viveTrigger.Available && viveTrigger.Pressed;
            bool pulled = pressed && !liveTriggerWasPressed;
            liveTriggerWasPressed = pressed;
            var procedure = liveProcedure;
            if (procedure != null)
            {
                if (procedure.CloseAt >= 0) { if (now >= procedure.CloseAt) liveProcedure = null; }
                else if (pulled) LiveProcedureInput("trigger", now);
            }
            LogLiveTrackerPoses(now, "periodic");
            WatchSteamVrHealth(now);
        }

        /// <summary>
        /// SteamVR's log is the only place that says when it re-set its frame or a
        /// tracker was reset. Each such event is written to the recording, and the
        /// operator is asked for a quick aim check. This advises; it never blocks.
        /// </summary>
        private void WatchSteamVrHealth(double now)
        {
            if (liveHealth == null) return;
            foreach (var item in liveHealth.Poll(now))
            {
                var fields = new List<LogField> { LogField.String("kind", item.Kind), LogField.String("detail", item.Detail),
                    LogField.String("origin", "steamvr-vrserver-log") };
                if (!double.IsNaN(item.Degrees)) fields.Add(LogField.NumberValue("degrees", item.Degrees));
                RecordLiveEvent("LiveTrackingHealth", fields);
            }
            if (!LiveTrackingActive) { liveHealthNote = ""; return; }
            double shift = liveHealth.FrameShiftDegrees;
            if (liveHealth.BaseStationDisturbed)
                liveHealthNote = "BASE STATION WAS BUMPED OR POWER CYCLED · quick aim check (F8); recalibrate the station (F6) if it is loose";
            else if (liveHealth.FrameChanges > 0)
                liveHealthNote = "STEAMVR SHIFTED ITS FRAME" + (double.IsNaN(shift) ? "" : " " + shift.ToString("F1", CultureInfo.InvariantCulture) + "°") + " · quick aim check (F8)";
            else liveHealthNote = "";
        }

        /// <summary>
        /// Publishes this frame's tracked poses in place of the mouse-driven virtual
        /// tracker. An untracked head or gun is published as unavailable: the last
        /// good pose is never held, so no shot can be scored on a stale pose. The
        /// head is published on its own when the gun is not tracked, so the picture
        /// and the walkthrough's head step do not depend on a gun that is set down.
        /// </summary>
        public void PublishLivePoses()
        {
            RigidPose? head = null, weapon = null;
            if (liveModel != null && liveHead.Pose.HasValue)
            {
                var rig = view.Configuration.Rig; var screen = rig.Display;
                var roomFromTracking = SightLineSolver.Compose(SightLineSolver.RoomFromScreen(screen), liveModel.ScreenFromTracking);
                var headInRoom = SightLineSolver.Compose(roomFromTracking, liveHead.Pose.Value);
                head = headInRoom;
                if (liveGun.Pose.HasValue)
                {
                    var eye = headInRoom.TransformPoint(rig.HeadEyeOffsetM);
                    // Through the point under the sights; when the gun points away from the glass, along the gun itself.
                    Vector3d direction = liveModel.TryGlassPoint(liveGun.Pose.Value, out double u, out double v, out _)
                        ? screen.Origin + screen.U * u + screen.V * v - eye
                        : roomFromTracking.TransformDirection(liveGun.Pose.Value.TransformDirection(liveModel.LineDirection));
                    if (direction.Length > 1e-6)
                    {
                        // Same construction as the mouse rehearsal: muzzle at the eye, bore along the aim.
                        var rotation = WeaponZeroSolver.Solve(rig.WeaponZero.Rotate(rig.WeaponBoreLocalDirection), direction);
                        weapon = new RigidPose(eye - rotation.Rotate(rig.WeaponMuzzleOffsetM), rotation);
                    }
                }
            }
            // A trigger observed next frame is scored on this publication: the pose from just before the pull was seen.
            livePublishedGun = liveGun.Pose; livePublishedHead = liveHead.Pose;
            livePublishedAimValid = liveAimValid; livePublishedAimU = liveAimU; livePublishedAimV = liveAimV;
            desktopSource?.Publish(head, weapon);
            if (desktopSource == null) view.SetSessionFrame(LiveRenderHead(head), weapon, EmptyDesktopTargets);
        }

        /// <summary>
        /// Picture only. A brief head-tracking dropout would blank the participant's
        /// screen, so for a short time the picture keeps the last tracked head. The
        /// recorded sample for that moment is still "unavailable", and no shot is scored on it.
        /// </summary>
        private RigidPose? LiveRenderHead(RigidPose? tracked)
        {
            if (!LiveTrackingActive) return tracked;
            double now = Time.realtimeSinceStartupAsDouble;
            if (tracked.HasValue) { liveLastRenderedHead = tracked; liveLastRenderedHeadAt = now; return tracked; }
            return now - liveLastRenderedHeadAt <= liveSettings.renderHoldSeconds ? liveLastRenderedHead : null;
        }

        /// <summary>Operator actions from the keyboard or the dashboard.</summary>
        public void StationLiveAction(string action)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            switch (action)
            {
                case "liveRig": BeginLiveProcedure("rig", false); break;
                case "liveZero": BeginLiveProcedure("zero", false); break;
                case "liveCheck": BeginLiveProcedure("check", false); break;
                case "liveCapture": LiveProcedureInput("capture", now); break;
                case "liveAccept": LiveProcedureInput("accept", now); break;
                case "liveUndo": LiveProcedureInput("undo", now); break;
                case "liveCancel": LiveProcedureInput("cancel", now); break;
                case "liveMarker": liveMarker = !liveMarker; break;
                case "liveInput": SetLiveEnabled(!liveEnabled); break;
                default: throw new ArgumentException("Unknown live-tracking action.");
            }
        }

        public void LiveReport(string message) { liveNotice = message; lastMessage = message; }

        private void SetLiveEnabled(bool enabled)
        {
            if (liveReader == null) throw new InvalidOperationException("Live tracking is unavailable in this run.");
            liveEnabled = enabled;
            view.SetLiveEyeOffset(LiveTrackingActive ? liveEye : null);
            lastMessage = enabled ? (liveModel != null ? "Live trackers are driving the aim." : "Live tracking is on, but the station is not calibrated yet (F6).")
                : "Mouse and keyboard are driving the aim.";
            RecordLiveEvent("LiveInputChanged", new List<LogField> {
                LogField.Boolean("liveTracking", LiveTrackingActive), LogField.String("source", LiveTrackingActive ? LiveSourceIdentity : Elts.Tracking.DesktopTrackingSource.SourceIdentity) });
        }

        /// <summary>A new participant starts from the station fit; the previous shooter's zero never carries over.</summary>
        private void ResetLiveForParticipant()
        {
            if (liveReader == null) return;
            if (liveProcedure != null) liveProcedure = null;
            AdoptStationCalibration();
        }

        private void BeginLiveProcedure(string kind, bool fromTutorial)
        {
            if (liveReader == null)
                throw new InvalidOperationException("Live tracking is unavailable in this run.");
            if (LiveGunSerial.Length == 0 && liveReader.Connected)
                throw new InvalidOperationException("No gun tracker found. In SteamVR > Manage Trackers set the gun tracker to Held in hand, then try again.");
            if (liveProcedure != null) throw new InvalidOperationException("A tracking step is already running. Press Esc on the participant window to cancel it.");
            if (!liveReader.Connected) throw new InvalidOperationException("SteamVR is not connected: " + liveReader.Status + ".");
            if (engine != null && !closing && (engine.State == SessionState.BlockRunning || engine.State == SessionState.BlockPaused))
                throw new InvalidOperationException("Wait for the current test to end before starting an aim step.");
            var procedure = new LiveProcedure { Kind = kind, FromTutorial = fromTutorial };
            if (kind == "rig")
            {
                if (!StationCanStartParticipant) throw new InvalidOperationException("Finish the current participant before calibrating the station.");
                procedure.Titles = new[] { "SPOT 1 OF 4 · CENTRE MARK", "SPOT 2 OF 4 · FAR MARK", "SPOT 3 OF 4 · CLOSE, LEFT", "SPOT 4 OF 4 · CLOSE, RIGHT" };
                procedure.Instructions = new[] {
                    "Stand on the centre floor mark, square to the screen. Aim through the sights at the dot and squeeze the trigger.",
                    "Step back to the far floor mark. Aim through the sights at each dot and squeeze.",
                    "Come to about 4 ft from the screen and take one big step LEFT of centre. Aim at each dot and squeeze.",
                    "Stay about 4 ft from the screen and take one big step RIGHT of centre. Aim at each dot and squeeze." };
                procedure.Dots = new[] { liveSettings.nineDots, liveSettings.fiveDots, liveSettings.fiveDots, liveSettings.fiveDots };
            }
            else
            {
                if (liveStation == null || liveModel == null) throw new InvalidOperationException("Calibrate the station first (F6 on the participant window).");
                procedure.Titles = new[] { kind == "zero" ? "AIM AT THE DOT" : "QUICK AIM CHECK" };
                procedure.Instructions = new[] { "Hold the replica as shown. Look down the sights, aim at the dot and squeeze the trigger." };
                procedure.Dots = new[] { kind == "zero" ? liveSettings.fiveDots : liveSettings.threeDots };
            }
            liveProcedure = procedure;
            lastMessage = kind == "rig" ? "Station calibration started on the participant screen." : "Aim dots are showing on the participant screen.";
        }

        /// <summary>
        /// "trigger" or "capture" takes the current dot, or saves a finished station
        /// fit; "accept" saves; "undo" returns to the previous dot; "cancel" ends the step.
        /// </summary>
        private void LiveProcedureInput(string input, double now)
        {
            var procedure = liveProcedure;
            if (procedure == null || procedure.CloseAt >= 0) return;
            if (input == "cancel") { liveProcedure = null; lastMessage = "Tracking step cancelled. Nothing was changed."; return; }
            if (procedure.Failed)
            {
                // Start the same step again from its first dot.
                if (input == "undo") return;
                var restarted = new LiveProcedure { Kind = procedure.Kind, FromTutorial = procedure.FromTutorial,
                    Titles = procedure.Titles, Instructions = procedure.Instructions, Dots = procedure.Dots };
                liveProcedure = restarted;
                return;
            }
            if (procedure.Fit != null)
            {
                if (input == "undo") { procedure.Fit = null; procedure.Eye = null; UndoLiveCapture(procedure); }
                else AcceptLiveProcedure(procedure, now);
                return;
            }
            if (input == "undo") { UndoLiveCapture(procedure); return; }
            if (input != "trigger" && input != "capture") return;
            if (!TryAverageLiveFrames(now, out var gun, out var head, out string problem)) { procedure.Status = problem; return; }
            var screen = view.Configuration.Rig.Display;
            double[] dot = procedure.Dots[procedure.Spot][procedure.Dot];
            procedure.Captures.Add(new AimCapture(gun, dot[0] * screen.Width, dot[1] * screen.Height, head, procedure.Spot));
            procedure.Status = "Captured " + procedure.Captures.Count + " of " + procedure.TotalDots + (head.HasValue ? "." : " · head tracker not seen.");
            if (++procedure.Dot >= procedure.Dots[procedure.Spot].Length) { procedure.Dot = 0; procedure.Spot++; }
            if (procedure.Spot >= procedure.Dots.Length) SolveLiveProcedure(procedure, now);
        }

        private static void UndoLiveCapture(LiveProcedure procedure)
        {
            if (procedure.Captures.Count == 0) return;
            procedure.Captures.RemoveAt(procedure.Captures.Count - 1);
            if (procedure.Spot >= procedure.Dots.Length) { procedure.Spot = procedure.Dots.Length - 1; procedure.Dot = procedure.Dots[procedure.Spot].Length - 1; }
            else if (procedure.Dot > 0) procedure.Dot--;
            else { procedure.Spot--; procedure.Dot = procedure.Dots[procedure.Spot].Length - 1; }
            procedure.Status = "Aim at this dot again.";
        }

        /// <summary>
        /// Mean gun and head pose over a short window that ends just before the
        /// trigger pull, so the pull itself does not move the capture. Refused when
        /// the gun was not tracked or not held still.
        /// </summary>
        private bool TryAverageLiveFrames(double now, out RigidPose gun, out RigidPose? head, out string problem)
        {
            gun = default; head = null; problem = "";
            double from = now - liveSettings.captureWindowStartSeconds, to = now - liveSettings.captureWindowEndSeconds;
            var guns = new List<RigidPose>(); var heads = new List<RigidPose>(); int frames = 0;
            foreach (var frame in liveFrames)
            {
                if (frame.Time < from || frame.Time > to) continue;
                frames++;
                if (frame.Gun.HasValue) guns.Add(frame.Gun.Value);
                if (frame.Head.HasValue) heads.Add(frame.Head.Value);
            }
            if (guns.Count < liveSettings.captureMinimumSamples || guns.Count < frames)
            { problem = "The gun tracker was not tracked (" + liveGun.Status + "). Keep it in view of the base station and squeeze again."; return false; }
            gun = MeanPose(guns);
            foreach (var sample in guns)
            {
                double turn = 2 * Math.Acos(Math.Min(1, Math.Abs(Dot(sample.Orientation, gun.Orientation)))) * 180 / Math.PI;
                if (turn > liveSettings.captureMaximumSpreadDegrees || (sample.Position - gun.Position).Length > liveSettings.captureMaximumSpreadM)
                { problem = "The gun moved during the squeeze. Settle on the dot, then squeeze gently."; return false; }
            }
            // The head only places the eye; use it when it was tracked for most of the window.
            if (heads.Count >= liveSettings.captureMinimumSamples && heads.Count * 2 >= frames) head = MeanPose(heads);
            return true;
        }

        private static double Dot(Quaterniond a, Quaterniond b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        /// <summary>Mean of nearly equal poses. q and -q are the same rotation, so signs are aligned first.</summary>
        private static RigidPose MeanPose(List<RigidPose> poses)
        {
            var position = Vector3d.Zero; double x = 0, y = 0, z = 0, w = 0;
            var reference = poses[0].Orientation;
            foreach (var pose in poses)
            {
                position += pose.Position;
                double sign = Dot(reference, pose.Orientation) < 0 ? -1 : 1;
                x += sign * pose.Orientation.X; y += sign * pose.Orientation.Y; z += sign * pose.Orientation.Z; w += sign * pose.Orientation.W;
            }
            return new RigidPose(position / poses.Count, new Quaterniond(x, y, z, w));
        }

        private void SolveLiveProcedure(LiveProcedure procedure, double now)
        {
            try
            {
                procedure.Fit = procedure.Kind == "rig"
                    ? SightLineSolver.SolveRig(procedure.Captures)
                    : SightLineSolver.SolveZero(liveStation!.Model, procedure.Captures);
                try { procedure.Eye = SightLineSolver.EyeInHeadTracker(procedure.Fit.Model, procedure.Captures, liveSettings.eyeTowardTargetM); }
                catch (ArgumentException) { procedure.Eye = null; } // head never tracked: the previous eye stays
                procedure.SolvedAt = now;
                procedure.Summary = DescribeLiveFit(procedure);
                // A shooter's zero applies at once; a station fit waits for the operator to read it.
                if (procedure.Kind != "rig") AcceptLiveProcedure(procedure, now);
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            {
                procedure.Failed = true; procedure.Fit = null;
                procedure.Status = error.Message;
            }
        }

        private string DescribeLiveFit(LiveProcedure procedure)
        {
            var fit = procedure.Fit!;
            string text = procedure.Kind == "rig" ? "Station fit" : procedure.Kind == "zero" ? "Aim zero" : "Aim check";
            text += ": typical miss " + fit.RmsDegrees.ToString("F2", CultureInfo.InvariantCulture) + "°, worst " +
                fit.MaxDegrees.ToString("F2", CultureInfo.InvariantCulture) + "° over " + fit.UsedCount + " dots";
            if (fit.DroppedIndices.Count > 0) text += ", " + fit.DroppedIndices.Count + " set aside";
            if (procedure.Kind != "rig")
                text += "; aim moved " + fit.DirectionChangeDegrees.ToString("F2", CultureInfo.InvariantCulture) + "°";
            text += procedure.Eye.HasValue ? "; eye placed (spread " + (procedure.Eye.Value.SpreadM * 100).ToString("F1", CultureInfo.InvariantCulture) + " cm)." : "; eye not placed (head tracker not seen).";
            if (fit.RmsDegrees > liveSettings.looseFitDegrees) text += " This is loose: check the sights were on every dot and repeat.";
            if (procedure.Kind != "rig" && fit.DirectionChangeDegrees > liveSettings.zeroWarningDegrees)
                text += " Large correction: the base station, screen or gun tracker may have moved. Recalibrate the station if it persists.";
            return text;
        }

        private void AcceptLiveProcedure(LiveProcedure procedure, double now)
        {
            var fit = procedure.Fit!;
            if (procedure.Kind == "rig")
            {
                if (now - procedure.SolvedAt < LiveAcceptDelaySeconds) return;
                var screen = view.Configuration.Rig.Display;
                var saved = new LiveStationCalibration(fit.Model, procedure.Eye?.EyeInHeadTracker, LiveGunSerial, liveHeadSerial, liveReader!.UniverseId,
                    screen.Width, screen.Height, DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    fit.RmsDegrees, fit.MaxDegrees, fit.UsedCount, fit.DroppedIndices.Count);
                try { saved.Save(LiveCalibrationPath); }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                { procedure.Status = "The calibration could not be saved: " + error.Message; return; }
                liveStation = saved; liveModel = fit.Model; liveEye = procedure.Eye?.EyeInHeadTracker; liveNotice = "";
            }
            else
            {
                liveModel = fit.Model;
                if (procedure.Eye.HasValue) liveEye = procedure.Eye.Value.EyeInHeadTracker;
            }
            liveEnabled = true;
            view.SetLiveEyeOffset(liveEye);
            // The aim now agrees with the dots, so later frame changes are counted from this moment.
            liveHealth?.MarkAimConfirmed(); liveHealthNote = "";
            RecordLiveCalibration(procedure.Kind, fit.Model, liveEye, fit.RmsDegrees, fit.MaxDegrees, fit.UsedCount, fit.DroppedIndices.Count, fit.DirectionChangeDegrees);
            lastMessage = procedure.Summary;
            procedure.CloseAt = now + (procedure.Kind == "rig" ? 5 : 2.5);
            if (procedure.FromTutorial) CompleteTutorialLiveZero(procedure.Summary);
        }

        /// <summary>What the participant window shows for the running step, or null.</summary>
        public LiveOverlayContent? LiveOverlay
        {
            get
            {
                var procedure = liveProcedure;
                if (procedure == null) return null;
                var content = new LiveOverlayContent();
                string trackers = "Gun tracker: " + liveGun.Status + "   ·   Head tracker: " + liveHead.Status;
                if (procedure.CloseAt >= 0)
                { content.Heading = procedure.Kind == "rig" ? "STATION CALIBRATION SAVED" : "THANK YOU"; content.Instruction = procedure.Kind == "rig" ? procedure.Summary : "You can lower the replica."; return content; }
                if (procedure.Failed)
                {
                    content.Heading = "THAT DID NOT FIT"; content.Instruction = procedure.Status; content.Status = trackers;
                    content.Keys = "Trigger or Space: start again   ·   Esc: cancel"; return content;
                }
                if (procedure.Fit != null)
                {
                    content.Heading = "STATION CALIBRATION"; content.Instruction = procedure.Summary; content.Status = procedure.Status;
                    content.Keys = "Trigger or Enter: save   ·   Backspace: redo the last dot   ·   Esc: discard"; return content;
                }
                var screen = view.Configuration.Rig.Display;
                double[] dot = procedure.Dots[procedure.Spot][procedure.Dot];
                content.Heading = procedure.Titles[procedure.Spot];
                content.Instruction = procedure.Instructions[procedure.Spot];
                content.Status = (procedure.Status.Length > 0 ? procedure.Status + "   ·   " : "") + trackers;
                // Dots are fractions of the window. Unless it fills the screen they are not where the fit assumes.
                if (!Application.isEditor && !Screen.fullScreen)
                    content.Status = "THE WINDOW IS NOT FULLSCREEN · press F11 before aiming   ·   " + content.Status;
                content.Keys = procedure.Kind == "rig" ? "Trigger or Space: capture   ·   Backspace: redo the last dot   ·   Esc: cancel" : "";
                content.ShowDot = true; content.DotU = (float)dot[0]; content.DotV = (float)dot[1];
                content.DotDiameterFraction = (float)(liveSettings.dotDiameterM / screen.Width);
                return content;
            }
        }

        // ---------------------------------------------------------------- recording

        private void RecordLiveEvent(string type, List<LogField> fields)
        {
            if (recording?.IsOpen != true || sequence == null || clock == null || closing) return;
            if (!recording.TryRecord(new SessionEvent(sequence.Next(), clock.Now, type, fields)))
                engine?.Abort("A live-tracking event could not be recorded.");
        }

        private static void AddPose(List<LogField> fields, string prefix, RigidPose pose)
        {
            fields.Add(LogField.NumberValue(prefix + "X", pose.Position.X)); fields.Add(LogField.NumberValue(prefix + "Y", pose.Position.Y));
            fields.Add(LogField.NumberValue(prefix + "Z", pose.Position.Z));
            fields.Add(LogField.NumberValue(prefix + "Qx", pose.Orientation.X)); fields.Add(LogField.NumberValue(prefix + "Qy", pose.Orientation.Y));
            fields.Add(LogField.NumberValue(prefix + "Qz", pose.Orientation.Z)); fields.Add(LogField.NumberValue(prefix + "Qw", pose.Orientation.W));
        }
        private static void AddVector(List<LogField> fields, string prefix, Vector3d value)
        {
            fields.Add(LogField.NumberValue(prefix + "X", value.X)); fields.Add(LogField.NumberValue(prefix + "Y", value.Y));
            fields.Add(LogField.NumberValue(prefix + "Z", value.Z));
        }

        /// <summary>
        /// The calibration in force, in full, so recorded poses can be traced back to
        /// raw tracker poses and recomputed. Written when a session opens and whenever
        /// the calibration changes.
        /// </summary>
        private void RecordLiveCalibration(string kind, SightLineModel model, Vector3d? eye, double rmsDegrees, double maxDegrees, int captures, int setAside, double aimMovedDegrees)
        {
            var fields = new List<LogField> {
                LogField.String("kind", kind), LogField.String("source", LiveSourceIdentity),
                LogField.String("frames", "tracking=SteamVR-raw-Z-reflected;screen=x-right_y-up_z-into-screen_from-lower-left;metres"),
                LogField.String("gunSerial", String.IsNullOrEmpty(LiveGunSerial) ? "none" : LiveGunSerial),
                LogField.String("headSerial", liveHeadSerial.Length == 0 ? "none" : liveHeadSerial),
                LogField.String("steamVrUniverseId", String.IsNullOrEmpty(liveReader?.UniverseId) ? "unknown" : liveReader!.UniverseId),
                LogField.String("stationSavedUtc", String.IsNullOrEmpty(liveStation?.SavedUtc) ? "none" : liveStation!.SavedUtc),
                LogField.NumberValue("captures", captures), LogField.NumberValue("setAside", setAside),
                LogField.NumberValue("aimMovedDegrees", aimMovedDegrees), LogField.Boolean("eyePlaced", eye.HasValue),
                LogField.Boolean("physicalAccuracyMeasured", false) };
            if (!double.IsNaN(rmsDegrees)) fields.Add(LogField.NumberValue("rmsDegrees", rmsDegrees));
            if (!double.IsNaN(maxDegrees) && maxDegrees >= 0) fields.Add(LogField.NumberValue("maxDegrees", maxDegrees));
            AddPose(fields, "screenFromTracking", model.ScreenFromTracking);
            AddVector(fields, "lineOrigin", model.LineOrigin); AddVector(fields, "lineDirection", model.LineDirection);
            if (eye.HasValue) AddVector(fields, "eyeInHeadTracker", eye.Value);
            RecordLiveEvent("LiveCalibrationApplied", fields);
        }

        /// <summary>Called when a recording opens, so its first live event states the calibration it starts with.</summary>
        private void RecordLiveCalibrationAtStart()
        {
            if (!LiveTrackingActive || liveModel == null) return;
            RecordLiveCalibration("session-start", liveModel, liveEye, liveStation?.RmsDegrees ?? double.NaN, liveStation?.MaxDegrees ?? double.NaN,
                liveStation?.CaptureCount ?? 0, liveStation?.DroppedCount ?? 0, 0);
        }

        /// <summary>Raw tracker poses in the tracking frame, as SteamVR gave them. Untracked devices carry no pose.</summary>
        private void LogLiveTrackerPoses(double now, string reason)
        {
            if (!LiveTrackingActive || recording?.IsOpen != true || IsBusy) return;
            if (reason == "periodic")
            {
                // Only while aim matters (a running test, practice, or the walkthrough's target scene),
                // so the recording does not fill with poses from breaks and slides.
                bool aiming = engine != null && (engine.State == SessionState.BlockRunning || engine.State == SessionState.Practice || tutorial.LiveScene);
                if (!aiming || now < liveNextRawLog) return;
                liveNextRawLog = now + liveSettings.rawPoseLogSeconds;
            }
            // A trigger record carries the poses that were published for it; the periodic record carries this frame's.
            bool published = reason == "trigger";
            RigidPose? gun = published ? livePublishedGun : liveGun.Pose, head = published ? livePublishedHead : liveHead.Pose;
            bool aimValid = published ? livePublishedAimValid : liveAimValid;
            double aimU = published ? livePublishedAimU : liveAimU, aimV = published ? livePublishedAimV : liveAimV;
            var fields = new List<LogField> { LogField.String("reason", reason),
                LogField.Boolean("gunTracked", gun.HasValue), LogField.Boolean("headTracked", head.HasValue), LogField.Boolean("aimOnGlass", aimValid) };
            if (gun.HasValue) AddPose(fields, "gun", gun.Value);
            if (head.HasValue) AddPose(fields, "head", head.Value);
            if (aimValid) { fields.Add(LogField.NumberValue("aimU", aimU)); fields.Add(LogField.NumberValue("aimV", aimV)); }
            RecordLiveEvent("LiveTrackerPose", fields);
        }
    }
}
