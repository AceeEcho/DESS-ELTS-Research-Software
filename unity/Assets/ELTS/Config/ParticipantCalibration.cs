#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elts.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.Config
{
    /// <summary>
    /// One participant's calibration saved by the Calibration Environment as
    /// <c>profiles/participant-N.json</c>. The tracker-local eye, muzzle and bore
    /// offsets are applied when that participant starts. They are tracker-local, so
    /// they do not depend on the screen. Screen geometry is shared room setup staged
    /// into the build; it is compared here but never replaced at runtime.
    /// The conversion mirrors tools/config/calibration_profile.py; both are checked
    /// against the same expected values. This is synthetic development calibration,
    /// not a measured or study-ready rig calibration.
    /// </summary>
    public sealed class ParticipantCalibration
    {
        // Keep aligned with "Calibration Environment/server.py".
        public const string Format = "elts-participant-calibration-v1";
        public const int MaximumId = 999999;
        public const int MaximumIdDigits = 6;
        private const string CoordinateSystem = "RH_X-right_Y-up_Z-toward-viewer_metres_YXZ-degrees";
        private const int MaximumFileBytes = 100_000;
        /// <summary>Largest coordinate accepted from a dashboard profile, in metres or degrees.</summary>
        private const double MaximumMagnitude = 10_000;
        /// <summary>Allowed difference (metres) between saved and running screen corners.</summary>
        public const double ScreenToleranceM = 0.001;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public int ParticipantId { get; }
        public string FileName => FileNameFor(ParticipantId);
        /// <summary>SHA-256 of the saved file bytes, recorded with the session.</summary>
        public string Sha256 { get; }
        public Vector3d HeadEyeOffsetM { get; }
        public Vector3d WeaponMuzzleOffsetM { get; }
        public Vector3d WeaponBoreLocalDirection { get; }
        /// <summary>Saved screen corners in Unity room axes (metres).</summary>
        public Vector3d LowerLeftM { get; }
        public Vector3d LowerRightM { get; }
        public Vector3d UpperLeftM { get; }
        public double ScreenBottomHeightAboveFloorM { get; }
        public string CalibrationId => "synthetic-participant-" + ParticipantId + "-" + Sha256;

        /// <summary>Digits only, 0–999999. Leading zeros are dropped ("007" → 7).</summary>
        public static bool TryParseId(string? text, out int id)
        {
            id = 0;
            string value = (text ?? "").Trim();
            if (!Regex.IsMatch(value, "^[0-9]{1," + MaximumIdDigits + "}$")) return false;
            id = Int32.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            return id >= 0 && id <= MaximumId;
        }

        public static string FileNameFor(int id)
        {
            if (id < 0 || id > MaximumId) throw new ArgumentOutOfRangeException(nameof(id));
            return "participant-" + id + ".json";
        }

        /// <summary>Reads and validates one participant's file; throws a readable message when absent.</summary>
        public static ParticipantCalibration Load(string directory, int id)
        {
            string path = Path.Combine(directory, FileNameFor(id));
            if (!File.Exists(path))
                throw new FileNotFoundException("No saved calibration for participant " + id +
                    ". Save one in the Calibration Environment under ID " + id + " first.", path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Participant calibration cannot be a linked file.");
            if (new FileInfo(path).Length > MaximumFileBytes)
                throw new IOException("Participant " + id + "'s calibration file is too large.");
            return Parse(File.ReadAllBytes(path), id);
        }

        /// <summary>
        /// A saved profile is optional at intake. Returns null when none is saved for
        /// this ID (<paramref name="problem"/> is empty) or when the saved file cannot
        /// be used (<paramref name="problem"/> says why, for the recording).
        /// </summary>
        public static ParticipantCalibration? LoadIfSaved(string directory, int id, out string problem)
        {
            problem = "";
            if (!File.Exists(Path.Combine(directory, FileNameFor(id)))) return null;
            try { return Load(directory, id); }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            { problem = error.Message; return null; }
        }

        /// <summary>Cache file validation for the ID suggestions. Intake always reloads the chosen file.</summary>
        public sealed class Inventory
        {
            private Dictionary<string,(long Length, long Changed, ParticipantCalibration? Profile)> cached = new Dictionary<string,(long,long,ParticipantCalibration?)>();

            public IReadOnlyList<(int Id, bool Available, bool ScreenMatches, string Revision)> Scan(string directory, RigConfiguration rig)
            {
                var result = new List<(int Id, bool Available, bool ScreenMatches, string Revision)>();
                var next = new Dictionary<string,(long Length, long Changed, ParticipantCalibration? Profile)>();
                FileInfo[] files;
                try { files = new DirectoryInfo(directory).GetFiles("participant-*.json"); }
                catch (DirectoryNotFoundException) { cached.Clear(); return result; }

                foreach (var file in files)
                {
                    string digits = Path.GetFileNameWithoutExtension(file.Name).Substring("participant-".Length);
                    if (!TryParseId(digits, out int id) || file.Name != FileNameFor(id)) continue;
                    ParticipantCalibration? profile = null;
                    try
                    {
                        if (!cached.TryGetValue(file.FullName, out var entry) || entry.Length != file.Length ||
                            entry.Changed != file.LastWriteTimeUtc.Ticks)
                        {
                            try { profile = Load(directory, id); }
                            catch (Exception error) when (error is ArgumentException || error is JsonException ||
                                error is IOException || error is UnauthorizedAccessException) { profile = null; }
                            entry = (file.Length, file.LastWriteTimeUtc.Ticks, profile);
                        }
                        next[file.FullName] = entry;
                        profile = entry.Profile;
                    }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    {
                        // A concurrent replacement or unreadable file remains visible for recovery.
                    }
                    result.Add((id, profile != null, profile?.ScreenMatches(rig) == true, profile?.Sha256 ?? ""));
                }
                cached = next;
                return result.OrderBy(item => item.Id).ToArray();
            }
        }

        public static ParticipantCalibration Parse(byte[] raw, int expectedId)
        {
            JObject record;
            try
            {
                using (var text = new StringReader(StrictUtf8.GetString(raw)))
                using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 32 })
                {
                    record = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw Invalid(expectedId, "trailing content");
                }
            }
            catch (Exception exception) when (exception is JsonException || exception is DecoderFallbackException)
            { throw new ArgumentException("Participant " + expectedId + "'s calibration is not valid JSON.", exception); }
            return new ParticipantCalibration(record, expectedId, Hash(raw));
        }

        private ParticipantCalibration(JObject record, int expectedId, string sha256)
        {
            if ((string?)record["format"] != Format) throw Invalid(expectedId, "unsupported file format");
            var idToken = record["participantId"];
            if (idToken?.Type != JTokenType.Integer || (long)idToken != expectedId) throw Invalid(expectedId, "participant ID does not match its file name");
            if (!(record["calibration"] is JObject profile)) throw Invalid(expectedId, "missing calibration");
            if (profile["version"]?.Type != JTokenType.Integer || (int)profile["version"]! != 1 || (string?)profile["coordinateSystem"] != CoordinateSystem)
                throw Invalid(expectedId, "unsupported calibration version or coordinate system");
            ParticipantId = expectedId; Sha256 = sha256;
            try
            {
                var screen = (JObject)profile["screen"]!;
                double width = Number(screen["width"]), height = Number(screen["height"]), bottom = Number(screen["bottom"]);
                if (width < .001 || width > 100 || height < .001 || height > 100) throw Invalid(expectedId, "screen size out of range");
                var rotation = Vector(screen["rotation"]);
                var bottomCentre = new[] { Number(screen["x"]), bottom, Number(screen["z"]) };
                double[] Corner(double x, double y) => Add(bottomCentre, Rotate(new[] { x, y, 0 }, rotation));
                LowerLeftM = Unity(Corner(-width / 2, 0));
                LowerRightM = Unity(Corner(width / 2, 0));
                UpperLeftM = Unity(Corner(-width / 2, height));
                ScreenBottomHeightAboveFloorM = bottom;

                var head = (JObject)profile["head"]!; var weapon = (JObject)profile["weapon"]!;
                HeadEyeOffsetM = Unity(TrackerLocal(head, Vector(head["eye"]), false));
                WeaponMuzzleOffsetM = Unity(TrackerLocal(weapon, Vector(weapon["muzzle"]), false));
                // The saved bore correction rotates the weapon's local forward (−Z).
                var bore = Rotate(new double[] { 0, 0, -1 }, Vector(weapon["bore"]));
                WeaponBoreLocalDirection = Unity(TrackerLocal(weapon, bore, true)).Normalized();
            }
            catch (Exception exception) when (exception is InvalidCastException || exception is NullReferenceException || exception is ArgumentException && !(exception.Message.StartsWith("Participant ", StringComparison.Ordinal)))
            { throw new ArgumentException("Participant " + expectedId + "'s calibration is incomplete or invalid.", exception); }
        }

        /// <summary>
        /// True when the saved screen matches the running rig within <see cref="ScreenToleranceM"/>.
        /// False means the screen was re-measured after this ELTS build was staged (or
        /// the build predates any saved calibration); START-ELTS restages it.
        /// </summary>
        public bool ScreenMatches(RigConfiguration rig)
        {
            var display = rig.Display;
            return Near(LowerLeftM, display.Origin)
                && Near(LowerRightM, display.Origin + display.U * display.Width)
                && Near(UpperLeftM, display.Origin + display.V * display.Height)
                && Math.Abs(ScreenBottomHeightAboveFloorM - rig.ScreenBottomHeightAboveFloorM) <= ScreenToleranceM;
        }

        /// <summary>Returns the running rig with this participant's offsets; the screen is never replaced.</summary>
        public RigConfiguration ApplyTo(RigConfiguration rig)
            => rig.WithParticipantOffsets(HeadEyeOffsetM, WeaponMuzzleOffsetM, WeaponBoreLocalDirection, CalibrationId);

        private static bool Near(Vector3d a, Vector3d b) => (a - b).Length <= ScreenToleranceM;

        // Dashboard YXZ Euler rotation (degrees: pitch X, yaw Y, roll Z), applied
        // to a point as roll, then pitch, then yaw — identical to _rotate in Python.
        private static double[] Rotate(double[] vector, double[] degrees, bool inverse = false)
        {
            var steps = new (int A, int B, double Angle)[] { (0, 1, degrees[2]), (1, 2, degrees[0]), (2, 0, degrees[1]) };
            if (inverse) steps = steps.Reverse().Select(step => (step.A, step.B, -step.Angle)).ToArray();
            var result = (double[])vector.Clone();
            foreach (var (a, b, angle) in steps)
            {
                double radians = angle * Math.PI / 180, c = Math.Cos(radians), s = Math.Sin(radians);
                double first = c * result[a] - s * result[b], second = s * result[a] + c * result[b];
                result[a] = first; result[b] = second;
            }
            return result;
        }

        /// <summary>The dashboard mount is tracker-on-object; undo it to get tracker-local values.</summary>
        private static double[] TrackerLocal(JObject role, double[] point, bool direction)
        {
            var mount = (JObject)role["mount"]!;
            if (!direction) point = Add(point, Vector(mount["position"]).Select(value => -value).ToArray());
            return Rotate(point, Vector(mount["rotation"]), inverse: true);
        }

        private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        /// <summary>Dashboard axes are right-handed with +Z toward the viewer; Unity reflects Z.</summary>
        private static Vector3d Unity(double[] value) => new Vector3d(value[0], value[1], -value[2]);

        private static double[] Vector(JToken? token)
        {
            if (!(token is JArray array) || array.Count != 3) throw new ArgumentException("Expected three coordinates.");
            return array.Select(Number).ToArray();
        }

        private static double Number(JToken? token)
        {
            if (token == null || (token.Type != JTokenType.Integer && token.Type != JTokenType.Float))
                throw new ArgumentException("Expected a number.");
            double value = (double)token;
            if (!Double.IsFinite(value) || Math.Abs(value) > MaximumMagnitude) throw new ArgumentException("Expected a finite, bounded number.");
            return value;
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        private static ArgumentException Invalid(int id, string reason) =>
            new ArgumentException("Participant " + id + "'s calibration cannot be used: " + reason + ".");
    }
}
