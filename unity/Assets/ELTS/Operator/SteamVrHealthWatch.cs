#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// <summary>One thing SteamVR's own log said about tracking.</summary>
    internal readonly struct SteamVrHealthEvent
    {
        public SteamVrHealthEvent(string kind, string detail, double degrees) { Kind = kind; Detail = detail; Degrees = degrees; }
        /// <summary>frame-shift, base-station-disturbed, tracker-reset, imu-mismatch or imu-saturated.</summary>
        public string Kind { get; }
        public string Detail { get; }
        /// <summary>Size of a frame shift in degrees; NaN for other kinds.</summary>
        public double Degrees { get; }
    }

    /// <summary>
    /// Follows SteamVR's server log, read-only, for the events that invalidate a
    /// tracking calibration: SteamVR re-estimating which way is up (it turns its
    /// whole frame about the base station), the base station being bumped, and a
    /// tracker being reset. SteamVR offers no API for these; the log is the only
    /// place they appear. The text format is undocumented, so this is advice for
    /// the operator and a note in the recording, never a gate on data.
    /// Call from the Unity main thread.
    /// </summary>
    internal sealed class SteamVrHealthWatch : IDisposable
    {
        private const double PollSeconds = 1;
        private const int MaximumReadBytes = 256 * 1024;
        private static readonly Regex Tilt = new Regex(@"Setting universe tilt .*pitch (-?[0-9.]+) deg roll (-?[0-9.]+) deg", RegexOptions.CultureInvariant);
        private static readonly Regex Serial = new Regex(@"(LHR-[0-9A-F]+)", RegexOptions.CultureInvariant);
        private static readonly SteamVrHealthEvent[] Nothing = Array.Empty<SteamVrHealthEvent>();
        private readonly string path;
        private FileStream? stream;
        private readonly StringBuilder partial = new StringBuilder();
        private double nextPoll;
        // The tilt SteamVR last applied to its frame, and the one in force when the aim was last confirmed.
        // Either is unknown until SteamVR writes a tilt line while this watch is running.
        private double? appliedPitch, appliedRoll, confirmedPitch, confirmedRoll;
        /// <summary>Tracker resets since the aim was last confirmed.</summary>
        public int TrackerResets { get; private set; }
        /// <summary>Times SteamVR re-set its frame since the aim was last confirmed.</summary>
        public int FrameChanges { get; private set; }
        public bool BaseStationDisturbed { get; private set; }
        public bool Following => stream != null;

        public SteamVrHealthWatch() { path = FindLog(); }

        /// <summary>
        /// How far SteamVR's frame has turned since the aim was last confirmed
        /// (degrees). Zero when it has not changed; NaN when it changed by an amount
        /// the log did not let us measure.
        /// </summary>
        public double FrameShiftDegrees
        {
            get
            {
                if (FrameChanges == 0) return 0;
                if (!appliedPitch.HasValue || !confirmedPitch.HasValue) return Double.NaN;
                return Math.Sqrt(Math.Pow(appliedPitch.Value - confirmedPitch.Value, 2) + Math.Pow(appliedRoll!.Value - confirmedRoll!.Value, 2));
            }
        }

        /// <summary>The aim was just confirmed against dots: later changes are measured from here.</summary>
        public void MarkAimConfirmed()
        {
            confirmedPitch = appliedPitch; confirmedRoll = appliedRoll;
            BaseStationDisturbed = false; TrackerResets = 0; FrameChanges = 0;
        }

        /// <summary>Reads any new log lines. Returns what they reported, oldest first.</summary>
        public IReadOnlyList<SteamVrHealthEvent> Poll(double seconds)
        {
            // Called every frame; only look at the file once a second, and allocate nothing in between.
            if (seconds < nextPoll || path.Length == 0) return Nothing;
            nextPoll = seconds + PollSeconds;
            var events = new List<SteamVrHealthEvent>();
            try
            {
                if (stream == null)
                {
                    if (!File.Exists(path)) return events;
                    stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    // Only what happens from now on matters; history belongs to earlier sessions.
                    stream.Seek(0, SeekOrigin.End);
                }
                long length = stream.Length;
                if (length < stream.Position) { Close(); return events; } // SteamVR restarted and began a new log
                int count = (int)Math.Min(MaximumReadBytes, length - stream.Position);
                if (count <= 0) return events;
                var buffer = new byte[count];
                int read = stream.Read(buffer, 0, count);
                partial.Append(Encoding.UTF8.GetString(buffer, 0, read));
                string text = partial.ToString();
                int lineEnd;
                int start = 0;
                while ((lineEnd = text.IndexOf('\n', start)) >= 0)
                {
                    Interpret(text.Substring(start, lineEnd - start), events);
                    start = lineEnd + 1;
                }
                partial.Clear(); partial.Append(text, start, text.Length - start);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { Close(); }
            return events;
        }

        private void Interpret(string line, List<SteamVrHealthEvent> events)
        {
            var tilt = Tilt.Match(line);
            if (tilt.Success &&
                Double.TryParse(tilt.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double pitch) &&
                Double.TryParse(tilt.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double roll))
            {
                // The first line seen says the frame was re-set, but not from where: its size is unknown.
                double step = appliedPitch.HasValue ? Math.Sqrt(Math.Pow(pitch - appliedPitch.Value, 2) + Math.Pow(roll - appliedRoll!.Value, 2)) : Double.NaN;
                appliedPitch = pitch; appliedRoll = roll;
                FrameChanges++;
                events.Add(new SteamVrHealthEvent("frame-shift", "SteamVR re-set its tracking frame", step));
                return;
            }
            string serial = Serial.Match(line).Success ? Serial.Match(line).Groups[1].Value : "tracker";
            if (line.Contains("reset count changed"))
            { BaseStationDisturbed = true; events.Add(new SteamVrHealthEvent("base-station-disturbed", "Base station bumped or power cycled", Double.NaN)); }
            else if (line.Contains("IMU misalignment unreasonably large"))
            { TrackerResets++; events.Add(new SteamVrHealthEvent("imu-mismatch", serial + " optical and inertial poses disagreed", Double.NaN)); }
            else if (line.Contains("Resetting tracking"))
            { TrackerResets++; events.Add(new SteamVrHealthEvent("tracker-reset", serial + " lost sight of the base station", Double.NaN)); }
            else if (line.Contains("IMU went off scale"))
                events.Add(new SteamVrHealthEvent("imu-saturated", serial + " took a hard knock", Double.NaN));
        }

        /// <summary>SteamVR's registered log folder, falling back to Steam's default install.</summary>
        private static string FindLog()
        {
            try
            {
                string registry = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "openvr", "openvrpaths.vrpath");
                if (File.Exists(registry) && JObject.Parse(File.ReadAllText(registry))["log"] is JArray folders && folders.Count > 0)
                    return Path.Combine((string?)folders[0] ?? "", "vrserver.txt");
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is Newtonsoft.Json.JsonException) { /* fall through */ }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "logs", "vrserver.txt");
        }

        private void Close() { stream?.Dispose(); stream = null; partial.Clear(); }
        public void Dispose() { Close(); }
    }
}
