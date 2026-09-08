#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Elts.Calibration;
using Elts.Geometry;
using Elts.Session;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    public sealed partial class DevelopmentSessionPanel
    {
        // Virtual fixture timing is data, not a claim about real tracker capture.
        private const int CalibrationPoseCount = 240;
        private const double FixtureCaptureHz = 200;
        private const double FixtureCornerSeconds = 1;
        private const double FixtureZeroSeconds = 2;
        private const double FixtureNoiseMeters = 0.0001;
        private CalibrationWizard calibration = new CalibrationWizard();
        private string? calibrationJson;
        private bool calibrationWithinLimits;
        public string? AcceptedCalibrationPath { get; private set; }

        private void InitializeCalibrationControls()
        {
            var eye = root.Q<DropdownField>("dominantEye");
            eye.choices = new List<string> { "Left", "Right" };
            eye.value = "Right";
            var offset = view.Configuration.Rig.HeadEyeOffsetM;
            Field("eyeOffset").value = String.Join(",", Components(offset).Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
            Bind("captureCalibration", () => Act(CaptureCalibrationFixture));
            Bind("acceptCalibration", () => Act(AcceptCalibrationFixture));
            Bind("redoCalibration", () => Act(() => {
                calibration.Redo(); calibrationJson = null;
                Label("calibrationReview", "Fixture discarded. Adjust inputs and generate again.");
            }));
        }

        private void ResetCalibration()
        {
            calibration = new CalibrationWizard();
            calibrationJson = null;
            calibrationWithinLimits = false;
            AcceptedCalibrationPath = null;
            Label("calibrationReview", "No fixture captured.");
        }

        private void AdvanceWithCalibration()
        {
            if (engine!.State == SessionState.Calibration && calibration.State != CalibrationWizardState.Accepted)
                throw new InvalidOperationException("Review and accept a synthetic calibration fixture before practice.");
            engine.Advance();
            root.Q<Foldout>("calibrationWizard").value = engine.State == SessionState.Calibration;
        }

        private void CaptureCalibrationFixture()
        {
            if (engine!.State != SessionState.Calibration || calibration.State != CalibrationWizardState.Idle)
                throw new InvalidOperationException("Capture is available only in the calibration phase. Use Redo to replace a review.");
            var values = ParseNumbers(Field("calibrationThresholds").value, 5);
            var limits = new CalibrationDevelopmentThresholds(values[0] / 1000, values[1] / 1000, values[2], values[3], values[4]);
            var eyeValues = ParseNumbers(Field("eyeOffset").value, 3);
            var eye = new EyeOffsetInput((DominantEye)Enum.Parse(typeof(DominantEye), root.Q<DropdownField>("dominantEye").value),
                new Vector3d(eyeValues[0], eyeValues[1], eyeValues[2]));
            var rig = view.Configuration.Rig;
            var screen = rig.Display;
            var pivotPoint = screen.Origin + screen.U * (screen.Width * .5) + screen.V * (screen.Height * .5);
            var knownTip = rig.WeaponMuzzleOffsetM;
            var poses = new List<RigidPose>();
            for (int i = 0; i < CalibrationPoseCount; i++)
            {
                // Deterministic rotations exercise two axes over +/-30 degrees.
                double phase = i * 2 * Math.PI / CalibrationPoseCount;
                var rotation = Quaterniond.FromAxisAngle(new Vector3d(1, 0, 0), Math.Sin(phase) * Math.PI / 6)
                    * Quaterniond.FromAxisAngle(new Vector3d(0, 1, 0), Math.Cos(phase) * Math.PI / 6);
                var noise = new Vector3d(Math.Sin(i * 1.7), Math.Cos(i * 2.3), Math.Sin(i * .9)) * FixtureNoiseMeters;
                poses.Add(new RigidPose(pivotPoint - rotation.Rotate(knownTip) + noise, rotation));
            }
            var pivot = PivotCalibrationSolver.Solve(poses);
            // Each stationary corner has 200 virtual samples. Retain these raw
            // means and the fixture recipe; do not reinterpret them as measured.
            var corners = new[] { screen.Origin, screen.Origin + screen.U * screen.Width,
                screen.Origin + screen.V * screen.Height, screen.Origin + screen.U * screen.Width + screen.V * screen.Height };
            var corner = CornerCalibrationSolver.Solve(corners[0], corners[1], corners[2], corners[3]);
            double rawAngle = Math.Abs(90 - Math.Acos(Math.Max(-1, Math.Min(1,
                Vector3d.Dot((corners[1]-corners[0]).Normalized(), (corners[2]-corners[0]).Normalized())))) * 180 / Math.PI);
            var observed = rig.WeaponBoreLocalDirection;
            var sighting = rig.WeaponZero.Rotate(observed);
            var zero = WeaponZeroSolver.Solve(observed, sighting);
            // Compare known and solved directions at all nine screen locations.
            var residuals = new List<double>();
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                var target = screen.Origin + screen.U * (screen.Width * (x+1)/4) + screen.V * (screen.Height * (y+1)/4);
                var origin = pivotPoint - screen.Normal * 2;
                var expected = (target-origin).Normalized();
                var reconstructed = (target-origin+(pivot.TipOffsetMeters-knownTip)).Normalized();
                residuals.Add(Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(expected, reconstructed)))) * 180 / Math.PI);
            }
            var verification = AngularVerification.Summarize(residuals);
            calibrationWithinLimits = pivot.RmsResidualMeters <= limits.PivotRmsMeters
                && corner.BottomRightConsistencyMeters <= limits.CornerDistanceMeters
                && rawAngle <= limits.CornerAngleDegrees
                && verification.MeanDegrees <= limits.VerificationMeanDegrees
                && verification.MaxDegrees <= limits.VerificationMaxDegrees;
            var record = new {
                schemaVersion = "elts.synthetic-calibration.v1", mode = "synthetic", studyReady = false,
                measured = false, fixtureId = "calibration-known-transform-v1",
                sourceRevision = Application.isEditor ? "editor-unversioned" : "build-" + Application.version,
                configurationSha256 = view.Configuration.EffectiveSha256,
                createdUtc = DateTimeOffset.UtcNow.ToString("O"), sharedClockTicks = clock!.Now.Ticks,
                frame = "Unity room; metres; quaternion x,y,z,w", provisionalThresholds = limits,
                withinDevelopmentLimits = calibrationWithinLimits,
                inputs = new {
                    poseCount = CalibrationPoseCount, virtualCaptureHz = FixtureCaptureHz,
                    virtualCornerDurationSeconds = FixtureCornerSeconds, virtualZeroDurationSeconds = FixtureZeroSeconds,
                    noiseAmplitudeMeters = FixtureNoiseMeters, knownTipOffsetMeters = Components(knownTip),
                    pivotWorldMeters = Components(pivotPoint),
                    poses = poses.Select((p,i) => new { virtualSeconds = i / FixtureCaptureHz,
                        position = Components(p.Position), orientation = Components(p.Orientation) }).ToArray(),
                    cornerMeansMeters = corners.Select(Components).ToArray(),
                    observedBore = Components(observed), sightingDirection = Components(sighting),
                    dominantEye = eye.Eye.ToString(), trackerToEyeMeters = Components(eye.TrackerToEyeMeters)
                },
                output = new { tipOffsetMeters = Components(pivot.TipOffsetMeters), pivot.RmsResidualMeters,
                    cornerOriginMeters = Components(corner.OriginMeters), uMeters = Components(corner.U), vMeters = Components(corner.V),
                    corner.BottomRightConsistencyMeters, rawCornerAngleErrorDegrees = rawAngle,
                    zeroQuaternion = Components(zero), verification.ResidualDegrees, verification.MeanDegrees, verification.MaxDegrees },
                limitation = "Fixture review only; runtime retains startup rig configuration. Physical calibration and D-17 approval pending."
            };
            calibrationJson = JsonConvert.SerializeObject(record, Formatting.Indented);
            calibration.BeginCapture(); calibration.Review();
            Label("calibrationReview", $"Pivot {pivot.SampleCount} poses: {pivot.RmsResidualMeters*1000:F3} mm RMS. " +
                $"Corner {corner.BottomRightConsistencyMeters*1000:F3} mm, {rawAngle:F3}°. " +
                $"3×3 mean {verification.MeanDegrees:F4}°, max {verification.MaxDegrees:F4}°.\n" +
                (calibrationWithinLimits ? "Within provisional limits. Review, accept or redo." : "Outside provisional limits. Redo required."));
        }

        private void AcceptCalibrationFixture()
        {
            if (engine!.State != SessionState.Calibration || calibration.State != CalibrationWizardState.Review
                || !calibrationWithinLimits || calibrationJson == null || recording?.RunDirectory == null)
                throw new InvalidOperationException("A reviewed fixture within development limits is required.");
            string path = Path.Combine(recording.RunDirectory, "synthetic-calibration-" + Guid.NewGuid().ToString("N") + ".json");
            // Create-new prevents redo or rerun from overwriting prior evidence.
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(output, new UTF8Encoding(false))) writer.Write(calibrationJson);
            engine.AddNote("Synthetic calibration fixture accepted: " + Path.GetFileName(path) + " sha256=" + Hash(calibrationJson) + "; measured=false; studyReady=false");
            if (engine.State == SessionState.Failed) throw new IOException("Calibration note could not be logged; fixture retained for inspection.");
            calibration.Accept("Computed fixture checked against recorded provisional thresholds.");
            AcceptedCalibrationPath = path;
            Label("calibrationReview", "Development fixture accepted and saved. Startup rig configuration remains active. Continue to practice. Study unavailable.");
        }

        private void RefreshCalibrationControls()
        {
            bool active = !IsBusy && engine?.State == SessionState.Calibration;
            root.Q<Button>("captureCalibration").SetEnabled(active && calibration.State == CalibrationWizardState.Idle);
            root.Q<Button>("acceptCalibration").SetEnabled(active && calibration.State == CalibrationWizardState.Review && calibrationWithinLimits);
            root.Q<Button>("redoCalibration").SetEnabled(active && calibration.State == CalibrationWizardState.Review);
            if (engine?.State == SessionState.Calibration)
                root.Q<Button>("advance").SetEnabled(!IsBusy && calibration.State == CalibrationWizardState.Accepted);
            foreach (var name in new[] { "eyeOffset", "calibrationThresholds" }) Field(name).SetEnabled(active && calibration.State == CalibrationWizardState.Idle);
            root.Q<DropdownField>("dominantEye").SetEnabled(active && calibration.State == CalibrationWizardState.Idle);
        }

        private static double[] ParseNumbers(string text, int count)
        {
            var parts = text.Split(',');
            if (parts.Length != count) throw new ArgumentException($"Enter {count} comma-separated numbers using a decimal point.");
            var values = new double[count];
            for (int i=0; i<count; i++)
                if (!Double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || Double.IsNaN(values[i]) || Double.IsInfinity(values[i]))
                    throw new ArgumentException("Calibration inputs must be finite numbers.");
            return values;
        }
        private static double[] Components(Vector3d v) => new[] { v.X, v.Y, v.Z };
        private static double[] Components(Quaterniond q) => new[] { q.X, q.Y, q.Z, q.W };
    }
}
