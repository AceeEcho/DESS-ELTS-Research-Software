#nullable enable
using System.IO;
using Elts.Config;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Elts.Operator.Tests
{
    /// <summary>
    /// Writes a synthetic participant calibration (the Calibration Environment's
    /// example setup) into the automated-test profile folder. Not a measurement.
    /// </summary>
    internal static class ParticipantCalibrationFixture
    {
        public static string Save(DevelopmentSessionPanel session, int id, string name = "", double[]? eye = null)
        {
            string defaults = Path.Combine(Application.dataPath, "..", "..", "Calibration Environment", "defaults.json");
            var profile = JObject.Parse(File.ReadAllText(defaults));
            if (eye != null) profile["head"]!["eye"] = new JArray(eye[0], eye[1], eye[2]);
            Directory.CreateDirectory(session.CalibrationProfileDirectory);
            string path = Path.Combine(session.CalibrationProfileDirectory, ParticipantCalibration.FileNameFor(id));
            File.WriteAllText(path, new JObject
            {
                ["format"] = ParticipantCalibration.Format, ["participantId"] = id, ["name"] = name,
                ["savedUtc"] = "2026-01-01T00:00:00+00:00", ["calibration"] = profile,
            }.ToString());
            return path;
        }
    }
}
