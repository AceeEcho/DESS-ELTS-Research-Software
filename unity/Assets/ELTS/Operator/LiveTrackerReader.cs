#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Elts.Calibration;
using Elts.Geometry;
using Elts.Tracking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Valve.VR;

namespace Elts.Operator
{
    /// <summary>One tracker as SteamVR reported it on the latest poll. The pose is in the raw tracking frame.</summary>
    internal readonly struct LiveTrackerSample
    {
        public LiveTrackerSample(string serial, bool connected, RigidPose? pose, string status)
        { Serial = serial; Connected = connected; Pose = pose; Status = status; }
        public string Serial { get; }
        public bool Connected { get; }
        /// <summary>Present only when SteamVR reports a valid, fully tracked pose.</summary>
        public RigidPose? Pose { get; }
        public bool Valid => Pose.HasValue;
        public string Status { get; }
        public static LiveTrackerSample Missing(string serial, string status) => new LiveTrackerSample(serial, false, null, status);
    }

    /// <summary>
    /// Reads raw tracker poses from SteamVR. It asks for the raw, uncalibrated
    /// universe so that a SteamVR room setup or "reset standing position" cannot
    /// silently move the frame under a saved calibration. No prediction is added.
    /// It never changes roles, sends haptics, or talks to the apparatus.
    /// Call from the Unity main thread.
    /// </summary>
    internal sealed class LiveTrackerReader : IDisposable
    {
        private const double RetrySeconds = 2, SerialRefreshSeconds = 2;
        private readonly TrackedDevicePose_t[] poses = new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly string?[] serials = new string?[OpenVR.k_unMaxTrackedDeviceCount];
        private bool ownsRuntime;
        private double retryAt, serialsAt;
        public bool Connected { get; private set; }
        public string Status { get; private set; } = "Connecting to SteamVR";
        /// <summary>SteamVR's identity for the current tracking frame; it changes when SteamVR builds a new one.</summary>
        public string UniverseId { get; private set; } = "";

        public void Poll(double seconds)
        {
            Connected = false;
            try
            {
                var system = OpenVR.System;
                if (system == null)
                {
                    if (seconds < retryAt) return;
                    retryAt = seconds + RetrySeconds;
                    var error = EVRInitError.None;
                    system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                    if (system == null || error != EVRInitError.None) { Status = "Start SteamVR"; return; }
                    ownsRuntime = true; serialsAt = 0;
                }
                if (seconds >= serialsAt) { serialsAt = seconds + SerialRefreshSeconds; RefreshSerials(system); }
                system.GetDeviceToAbsoluteTrackingPose(ETrackingUniverseOrigin.TrackingUniverseRawAndUncalibrated, 0, poses);
                Connected = true; Status = "SteamVR connected";
            }
            catch (Exception error)
            {
                Status = "SteamVR unavailable: " + error.GetType().Name;
                Release(); retryAt = seconds + RetrySeconds;
            }
        }

        /// <summary>Serial numbers of every connected tracker or controller, in a stable order.</summary>
        public IReadOnlyList<string> ConnectedSerials()
        {
            var result = new List<string>();
            if (!Connected) return result;
            for (int index = 0; index < serials.Length; index++)
                if (serials[index] != null && poses[index].bDeviceIsConnected) result.Add(serials[index]!);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public LiveTrackerSample Sample(string serial)
        {
            if (String.IsNullOrEmpty(serial)) return LiveTrackerSample.Missing("", "not assigned");
            if (!Connected) return LiveTrackerSample.Missing(serial, Status);
            for (int index = 0; index < serials.Length; index++)
            {
                if (serials[index] != serial) continue;
                var pose = poses[index];
                if (!pose.bDeviceIsConnected) return LiveTrackerSample.Missing(serial, "disconnected");
                if (!pose.bPoseIsValid || pose.eTrackingResult != ETrackingResult.Running_OK)
                    return new LiveTrackerSample(serial, true, null, "not tracked (" + pose.eTrackingResult + ")");
                try { return new LiveTrackerSample(serial, true, Convert(pose.mDeviceToAbsoluteTracking), "tracked"); }
                catch (ArgumentException) { return new LiveTrackerSample(serial, true, null, "rejected pose"); }
            }
            return LiveTrackerSample.Missing(serial, "not found in SteamVR");
        }

        private void RefreshSerials(CVRSystem system)
        {
            var text = new StringBuilder(128);
            string universe = "";
            for (uint index = 0; index < OpenVR.k_unMaxTrackedDeviceCount; index++)
            {
                serials[index] = null;
                var kind = system.GetTrackedDeviceClass(index);
                if (kind != ETrackedDeviceClass.Controller && kind != ETrackedDeviceClass.GenericTracker) continue;
                var error = ETrackedPropertyError.TrackedProp_Success;
                text.Clear();
                system.GetStringTrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_SerialNumber_String, text, 128, ref error);
                if (error != ETrackedPropertyError.TrackedProp_Success || text.Length == 0) continue;
                serials[index] = text.ToString();
                ulong id = system.GetUint64TrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_CurrentUniverseId_Uint64, ref error);
                if (error == ETrackedPropertyError.TrackedProp_Success && id != 0) universe = id.ToString(CultureInfo.InvariantCulture);
            }
            UniverseId = universe;
        }

        /// <summary>OpenVR's row-major 3x4 matrix to a pose in the project's tracking frame.</summary>
        private static RigidPose Convert(HmdMatrix34_t m)
        {
            // The matrix columns are the tracker's own axes in OpenVR's right-handed frame.
            var native = SightLineSolver.FromBasis(new Vector3d(m.m0, m.m4, m.m8), new Vector3d(m.m1, m.m5, m.m9), new Vector3d(m.m2, m.m6, m.m10));
            return TrackingCoordinateConverter.Convert(new NativeTrackingPose(
                new NativeTrackingVector(m.m3, m.m7, m.m11), new NativeTrackingQuaternion(native.X, native.Y, native.Z, native.W)));
        }

        private void Release()
        {
            if (ownsRuntime) { try { OpenVR.Shutdown(); } catch (Exception) { /* SteamVR already gone. */ } }
            ownsRuntime = false; Connected = false;
            for (int index = 0; index < serials.Length; index++) serials[index] = null;
        }
        public void Dispose() { Release(); }
    }

    /// <summary>
    /// Adjustable values for live tracking. Edit the optional file
    /// "Calibration Environment/live-tracking.json" in the station folder; anything
    /// left out keeps the default below. None of these is an acceptance limit.
    /// </summary>
    [Serializable]
    public sealed class LiveTrackingSettings
    {
        /// <summary>Head tracker serial. Empty picks the other connected tracker (the gun's is weaponTriggerSerial).</summary>
        public string headSerial = "";
        /// <summary>Draw a marker where the sights point. For setup and checking; turn off when shooters must rely on the sights alone.</summary>
        public bool showAimMarker = true;
        /// <summary>Dots for the first standing spot, as fractions of the screen (0 = left/bottom).</summary>
        public double[][] nineDots = { new[]{.5,.5}, new[]{.2,.8}, new[]{.8,.8}, new[]{.8,.2}, new[]{.2,.2}, new[]{.5,.8}, new[]{.8,.5}, new[]{.5,.2}, new[]{.2,.5} };
        /// <summary>Dots for the other standing spots and for a shooter's zero.</summary>
        public double[][] fiveDots = { new[]{.5,.5}, new[]{.15,.85}, new[]{.85,.85}, new[]{.85,.15}, new[]{.15,.15} };
        /// <summary>Dots for the quick check before a scenario.</summary>
        public double[][] threeDots = { new[]{.5,.5}, new[]{.2,.75}, new[]{.8,.25} };
        /// <summary>Dot diameter on the glass (metres).</summary>
        public double dotDiameterM = 0.025;
        /// <summary>A capture averages poses from this long before the trigger pull (seconds), so the pull itself is left out.</summary>
        public double captureWindowStartSeconds = 0.30, captureWindowEndSeconds = 0.06;
        /// <summary>A capture is refused when the gun turned or moved more than this within that window.</summary>
        public double captureMaximumSpreadDegrees = 0.8, captureMaximumSpreadM = 0.02;
        public int captureMinimumSamples = 4;
        /// <summary>The eye is taken this far toward the target from the point of the sight line nearest the head tracker (metres).</summary>
        public double eyeTowardTargetM = 0;
        /// <summary>A zero that moves the aim more than this suggests the rig moved; the operator is told.</summary>
        public double zeroWarningDegrees = 1.5;
        /// <summary>A fit whose typical miss exceeds this is called loose; the operator is told.</summary>
        public double looseFitDegrees = 0.5;
        /// <summary>Raw tracker poses are written to the recording this often (seconds).</summary>
        public double rawPoseLogSeconds = 0.05;
        /// <summary>
        /// Picture only: when the head tracker drops out, keep drawing from its last tracked
        /// pose for up to this long (seconds) instead of blanking. Recorded samples stay unavailable.
        /// </summary>
        public double renderHoldSeconds = 0.5;

        public void Validate()
        {
            void Range(double value, double minimum, double maximum, string name)
            { if (double.IsNaN(value) || value < minimum || value > maximum) throw new ArgumentException("Invalid live-tracking setting: " + name); }
            void Dots(double[][] dots, int minimum, string name)
            {
                if (dots == null || dots.Length < minimum) throw new ArgumentException("Invalid live-tracking setting: " + name);
                foreach (var dot in dots)
                    if (dot == null || dot.Length != 2 || !(dot[0] >= 0.05 && dot[0] <= 0.95 && dot[1] >= 0.05 && dot[1] <= 0.95))
                        throw new ArgumentException("Dots must be two fractions between 0.05 and 0.95: " + name);
            }
            Dots(nineDots, 6, nameof(nineDots)); Dots(fiveDots, 4, nameof(fiveDots)); Dots(threeDots, 2, nameof(threeDots));
            Range(dotDiameterM, 0.005, 0.1, nameof(dotDiameterM));
            Range(captureWindowEndSeconds, 0, 0.5, nameof(captureWindowEndSeconds));
            Range(captureWindowStartSeconds, captureWindowEndSeconds + 0.05, 1, nameof(captureWindowStartSeconds));
            Range(captureMaximumSpreadDegrees, 0.1, 10, nameof(captureMaximumSpreadDegrees));
            Range(captureMaximumSpreadM, 0.002, 0.2, nameof(captureMaximumSpreadM));
            Range(captureMinimumSamples, 2, 100, nameof(captureMinimumSamples));
            Range(eyeTowardTargetM, -0.3, 0.3, nameof(eyeTowardTargetM));
            Range(zeroWarningDegrees, 0.1, 20, nameof(zeroWarningDegrees));
            Range(looseFitDegrees, 0.05, 10, nameof(looseFitDegrees));
            Range(rawPoseLogSeconds, 0.02, 5, nameof(rawPoseLogSeconds));
            Range(renderHoldSeconds, 0, 2, nameof(renderHoldSeconds));
        }

        /// <summary>Defaults, overlaid by the station's optional settings file. A bad file is reported, not half-applied.</summary>
        public static LiveTrackingSettings Load(string path, out string message)
        {
            message = "";
            if (!File.Exists(path)) return new LiveTrackingSettings();
            try
            {
                var settings = JsonConvert.DeserializeObject<LiveTrackingSettings>(File.ReadAllText(path)) ?? new LiveTrackingSettings();
                settings.Validate();
                return settings;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is IOException)
            {
                message = "live-tracking.json was ignored: " + error.Message;
                return new LiveTrackingSettings();
            }
        }
    }

    /// <summary>
    /// The saved station calibration: where the screen is in SteamVR's frame and
    /// where the sight line is on the gun tracker. It belongs to this base-station
    /// placement, this screen and this gun mount. Fitted to aimed captures; it is
    /// not a physical accuracy measurement.
    /// </summary>
    public sealed class LiveStationCalibration
    {
        public const string Format = "elts-station-tracking-v1";
        public SightLineModel Model { get; }
        public Vector3d? EyeInHeadTracker { get; }
        public string GunSerial { get; }
        public string HeadSerial { get; }
        public string UniverseId { get; }
        public double ScreenWidthM { get; }
        public double ScreenHeightM { get; }
        public string SavedUtc { get; }
        public double RmsDegrees { get; }
        public double MaxDegrees { get; }
        public int CaptureCount { get; }
        public int DroppedCount { get; }

        public LiveStationCalibration(SightLineModel model, Vector3d? eye, string gunSerial, string headSerial, string universeId,
            double screenWidthM, double screenHeightM, string savedUtc, double rmsDegrees, double maxDegrees, int captureCount, int droppedCount)
        {
            Model = model; EyeInHeadTracker = eye; GunSerial = gunSerial; HeadSerial = headSerial; UniverseId = universeId;
            ScreenWidthM = screenWidthM; ScreenHeightM = screenHeightM; SavedUtc = savedUtc;
            RmsDegrees = rmsDegrees; MaxDegrees = maxDegrees; CaptureCount = captureCount; DroppedCount = droppedCount;
        }

        /// <summary>The saved fit only applies while the screen size it was fitted on is the one in use.</summary>
        public bool MatchesScreen(ScreenPlane screen) =>
            Math.Abs(screen.Width - ScreenWidthM) <= 0.001 && Math.Abs(screen.Height - ScreenHeightM) <= 0.001;

        public void Save(string path)
        {
            var record = new JObject
            {
                ["format"] = Format, ["savedUtc"] = SavedUtc,
                ["gunSerial"] = GunSerial, ["headSerial"] = HeadSerial, ["steamVrUniverseId"] = UniverseId,
                ["screenWidthM"] = ScreenWidthM, ["screenHeightM"] = ScreenHeightM,
                ["screenFromTracking"] = new JObject { ["positionM"] = Json(Model.ScreenFromTracking.Position), ["orientationXyzw"] = Json(Model.ScreenFromTracking.Orientation) },
                ["sightLine"] = new JObject { ["originM"] = Json(Model.LineOrigin), ["direction"] = Json(Model.LineDirection) },
                ["eyeInHeadTrackerM"] = EyeInHeadTracker.HasValue ? Json(EyeInHeadTracker.Value) : JValue.CreateNull(),
                ["fit"] = new JObject { ["rmsDegrees"] = RmsDegrees, ["maxDegrees"] = MaxDegrees, ["captures"] = CaptureCount, ["setAside"] = DroppedCount },
                ["note"] = "Fitted to aimed captures. Axes: screen frame x right, y up, z into the screen from its lower-left corner; tracking frame is SteamVR raw with Z reflected. Not a physical accuracy measurement.",
            };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            // Write beside the target, then replace, so an interrupted save cannot truncate the last good calibration.
            File.WriteAllText(temporary, record.ToString(Formatting.Indented), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }

        /// <summary>Null with a reason when there is no usable saved calibration.</summary>
        public static LiveStationCalibration? Load(string path, out string message)
        {
            message = "";
            if (!File.Exists(path)) { message = "No station tracking calibration saved yet."; return null; }
            try
            {
                if (new FileInfo(path).Length > 100_000) throw new IOException("file is too large");
                var record = JObject.Parse(File.ReadAllText(path));
                if ((string?)record["format"] != Format) throw new ArgumentException("unsupported format");
                var pose = (JObject)record["screenFromTracking"]!; var line = (JObject)record["sightLine"]!; var fit = (JObject)record["fit"]!;
                var orientation = Numbers(pose["orientationXyzw"], 4);
                var model = new SightLineModel(
                    new RigidPose(Vector(pose["positionM"]), new Quaterniond(orientation[0], orientation[1], orientation[2], orientation[3])),
                    Vector(line["originM"]), Vector(line["direction"]));
                Vector3d? eye = record["eyeInHeadTrackerM"] is JArray ? Vector(record["eyeInHeadTrackerM"]) : (Vector3d?)null;
                return new LiveStationCalibration(model, eye, (string?)record["gunSerial"] ?? "", (string?)record["headSerial"] ?? "",
                    (string?)record["steamVrUniverseId"] ?? "", Number(record["screenWidthM"]), Number(record["screenHeightM"]),
                    (string?)record["savedUtc"] ?? "", Number(fit["rmsDegrees"]), Number(fit["maxDegrees"]), (int)Number(fit["captures"]), (int)Number(fit["setAside"]));
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is IOException ||
                error is InvalidCastException || error is NullReferenceException)
            {
                message = "The saved station tracking calibration cannot be read (" + error.Message + "). Calibrate again.";
                return null;
            }
        }

        private static JArray Json(Vector3d value) => new JArray(value.X, value.Y, value.Z);
        private static JArray Json(Quaterniond value) => new JArray(value.X, value.Y, value.Z, value.W);
        private static double Number(JToken? token)
        {
            if (token == null || (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)) throw new ArgumentException("expected a number");
            double value = (double)token;
            if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 1e6) throw new ArgumentException("expected a finite number");
            return value;
        }
        private static double[] Numbers(JToken? token, int count)
        {
            if (!(token is JArray array) || array.Count != count) throw new ArgumentException("expected " + count + " numbers");
            return array.Select(Number).ToArray();
        }
        private static Vector3d Vector(JToken? token) { var v = Numbers(token, 3); return new Vector3d(v[0], v[1], v[2]); }
    }
}
