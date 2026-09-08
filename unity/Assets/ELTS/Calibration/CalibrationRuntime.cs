#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Elts.Geometry;

namespace Elts.Calibration
{
    /// <summary>Configurable development thresholds. They are not D-17 study approval criteria.</summary>
    public sealed class CalibrationDevelopmentThresholds
    {
        public CalibrationDevelopmentThresholds(double pivotRmsMeters = .002, double cornerDistanceMeters = .005,
            double cornerAngleDegrees = .5, double verificationMeanDegrees = 1, double verificationMaxDegrees = 2)
        {
            RequirePositiveFinite(pivotRmsMeters, nameof(pivotRmsMeters));
            RequirePositiveFinite(cornerDistanceMeters, nameof(cornerDistanceMeters));
            RequirePositiveFinite(cornerAngleDegrees, nameof(cornerAngleDegrees));
            RequirePositiveFinite(verificationMeanDegrees, nameof(verificationMeanDegrees));
            RequirePositiveFinite(verificationMaxDegrees, nameof(verificationMaxDegrees));
            PivotRmsMeters = pivotRmsMeters;
            CornerDistanceMeters = cornerDistanceMeters;
            CornerAngleDegrees = cornerAngleDegrees;
            VerificationMeanDegrees = verificationMeanDegrees;
            VerificationMaxDegrees = verificationMaxDegrees;
        }
        public double PivotRmsMeters { get; }
        public double CornerDistanceMeters { get; }
        public double CornerAngleDegrees { get; }
        public double VerificationMeanDegrees { get; }
        public double VerificationMaxDegrees { get; }
        internal static void RequirePositiveFinite(double value, string name) { if (value <= 0 || Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name); }
    }

    public sealed class PivotSolution
    {
        internal PivotSolution(Vector3d offset, Vector3d point, double rms, int count) { TipOffsetMeters = offset; PivotWorldPointMeters = point; RmsResidualMeters = rms; SampleCount = count; }
        public Vector3d TipOffsetMeters { get; }
        public Vector3d PivotWorldPointMeters { get; }
        public double RmsResidualMeters { get; }
        public int SampleCount { get; }
    }

    public static class PivotCalibrationSolver
    {
        public static PivotSolution Solve(IReadOnlyList<RigidPose> poses, int minimumSamples = 200)
        {
            if (minimumSamples < 200) throw new ArgumentOutOfRangeException(nameof(minimumSamples), "Development pivot solving requires at least 200 samples.");
            if (poses == null || poses.Count < minimumSamples) throw new ArgumentException("At least the configured number of valid poses is required.", nameof(poses));
            var normal = new double[6, 6]; var rhs = new double[6];
            foreach (var pose in poses) AddPose(normal, rhs, pose);
            var x = SolveLinear(normal, rhs);
            var offset = new Vector3d(x[0], x[1], x[2]); var point = new Vector3d(x[3], x[4], x[5]);
            double sumSquares = 0;
            foreach (var pose in poses) sumSquares += (pose.TransformPoint(offset) - point).LengthSquared;
            return new PivotSolution(offset, point, Math.Sqrt(sumSquares / poses.Count), poses.Count);
        }
        private static void AddPose(double[,] normal, double[] rhs, RigidPose pose)
        {
            var q = pose.Orientation; var r = new[,] { { 1 - 2 * (q.Y * q.Y + q.Z * q.Z), 2 * (q.X * q.Y - q.Z * q.W), 2 * (q.X * q.Z + q.Y * q.W) }, { 2 * (q.X * q.Y + q.Z * q.W), 1 - 2 * (q.X * q.X + q.Z * q.Z), 2 * (q.Y * q.Z - q.X * q.W) }, { 2 * (q.X * q.Z - q.Y * q.W), 2 * (q.Y * q.Z + q.X * q.W), 1 - 2 * (q.X * q.X + q.Y * q.Y) } };
            var a = new double[3, 6]; for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) a[row, col] = r[row, col]; a[row, row + 3] = -1; }
            var b = new[] { -pose.Position.X, -pose.Position.Y, -pose.Position.Z };
            for (int i = 0; i < 6; i++) { for (int j = 0; j < 6; j++) for (int k = 0; k < 3; k++) normal[i, j] += a[k, i] * a[k, j]; for (int k = 0; k < 3; k++) rhs[i] += a[k, i] * b[k]; }
        }
        private static double[] SolveLinear(double[,] normal, double[] rhs)
        {
            var matrix = (double[,])normal.Clone(); var result = (double[])rhs.Clone();
            double scale = 0; for (int row = 0; row < 6; row++) for (int column = 0; column < 6; column++) scale = Math.Max(scale, Math.Abs(matrix[row, column]));
            if (scale == 0) throw new ArgumentException("Pivot poses are degenerate; rotate through multiple axes.");
            double cutoff = scale * 1e-10;
            for (int column = 0; column < 6; column++) { int pivot = column; for (int row = column + 1; row < 6; row++) if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column])) pivot = row; if (Math.Abs(matrix[pivot, column]) < cutoff) throw new ArgumentException("Pivot poses are degenerate or poorly conditioned; rotate through multiple axes."); Swap(matrix, result, column, pivot); double divisor = matrix[column, column]; for (int j = column; j < 6; j++) matrix[column, j] /= divisor; result[column] /= divisor; for (int row = 0; row < 6; row++) if (row != column) { double factor = matrix[row, column]; for (int j = column; j < 6; j++) matrix[row, j] -= factor * matrix[column, j]; result[row] -= factor * result[column]; } }
            return result;
        }
        private static void Swap(double[,] matrix, double[] values, int a, int b) { for (int j = 0; j < 6; j++) { double t = matrix[a, j]; matrix[a, j] = matrix[b, j]; matrix[b, j] = t; } double v = values[a]; values[a] = values[b]; values[b] = v; }
    }

    public sealed class CornerSolution
    {
        internal CornerSolution(Vector3d origin, Vector3d u, Vector3d v, double rawAngle, double error) { OriginMeters = origin; U = u; V = v; RawAngleDegrees = rawAngle; BottomRightConsistencyMeters = error; }
        public Vector3d OriginMeters { get; }
        public Vector3d U { get; }
        public Vector3d V { get; }
        /// <summary>Angle between raw top-right and bottom-left axes before V is orthogonalized.</summary>
        public double RawAngleDegrees { get; }
        public double WidthMeters => U.Length;
        public double HeightMeters => V.Length;
        public double BottomRightConsistencyMeters { get; }
    }
    public static class CornerCalibrationSolver
    {
        public static CornerSolution Solve(Vector3d topLeft, Vector3d topRight, Vector3d bottomLeft, Vector3d bottomRight)
        {
            var u = topRight - topLeft; var raw = bottomLeft - topLeft; double width = u.Length, height = raw.Length;
            if (width < 1e-8 || height < 1e-8) throw new ArgumentException("Corner captures are degenerate.");
            double rawAngle = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(u / width, raw / height)))) * 180 / Math.PI;
            var unitU = u / width; var perpendicular = raw - unitU * Vector3d.Dot(raw, unitU);
            if (perpendicular.Length < 1e-8) throw new ArgumentException("Corner axes are collinear.");
            var v = perpendicular.Normalized() * height; double error = (bottomRight - (topLeft + u + v)).Length;
            return new CornerSolution(topLeft, u, v, rawAngle, error);
        }
    }

    public static class WeaponZeroSolver
    {
        public static Quaterniond Solve(Vector3d observedBoreDirection, Vector3d sightingDirection)
        {
            var from = observedBoreDirection.Normalized(); var to = sightingDirection.Normalized(); double dot = Math.Max(-1, Math.Min(1, Vector3d.Dot(from, to)));
            if (dot > 1 - 1e-12) return Quaterniond.Identity;
            if (dot < -1 + 1e-12) { var axis = Math.Abs(from.X) < .8 ? Vector3d.Cross(from, new Vector3d(1, 0, 0)) : Vector3d.Cross(from, new Vector3d(0, 1, 0)); return Quaterniond.FromAxisAngle(axis, Math.PI); }
            var cross = Vector3d.Cross(from, to); return new Quaterniond(cross.X, cross.Y, cross.Z, 1 + dot);
        }
    }

    public enum DominantEye { Left, Right }

    public sealed class EyeOffsetInput
    {
        public EyeOffsetInput(DominantEye eye, Vector3d trackerToEyeMeters)
        {
            if (!Enum.IsDefined(typeof(DominantEye), eye)) throw new ArgumentOutOfRangeException(nameof(eye));
            Eye = eye;
            TrackerToEyeMeters = trackerToEyeMeters;
        }

        public DominantEye Eye { get; }
        public Vector3d TrackerToEyeMeters { get; }
    }

    public sealed class AngularVerificationSummary
    {
        internal AngularVerificationSummary(double[] values)
        {
            ResidualDegrees = values;
            double sum = 0;
            double maximum = 0;
            foreach (double value in values)
            {
                if (value < 0 || Double.IsNaN(value) || Double.IsInfinity(value))
                    throw new ArgumentException("Residuals must be finite and nonnegative.");
                sum += value;
                maximum = Math.Max(maximum, value);
            }
            MeanDegrees = sum / values.Length;
            MaxDegrees = maximum;
        }

        public IReadOnlyList<double> ResidualDegrees { get; }
        public double MeanDegrees { get; }
        public double MaxDegrees { get; }
    }

    public static class AngularVerification
    {
        public static AngularVerificationSummary Summarize(IReadOnlyList<double> residualDegrees)
        {
            if (residualDegrees == null || residualDegrees.Count != 9)
                throw new ArgumentException("Exactly nine 3x3 residuals are required.");
            var values = new double[9];
            for (int index = 0; index < values.Length; index++) values[index] = residualDegrees[index];
            return new AngularVerificationSummary(values);
        }
    }

    public enum CalibrationWizardState { Idle, Capturing, Review, Accepted }

    public sealed class SyntheticCalibrationRecord
    {
        public string Mode => "synthetic";
        public bool StudyReady => false;
        public string Provenance { get; set; } = "synthetic-development-only";
        public CalibrationWizardState State { get; set; }
        public string? AcceptedSummary { get; set; }

        /// <summary>Small self-contained camelCase serialization for the synthetic state helper.</summary>
        public string ToJson()
        {
            return "{\"mode\":\"synthetic\",\"studyReady\":false,\"provenance\":\"" + Escape(Provenance)
                + "\",\"state\":\"" + State + "\",\"acceptedSummary\":\"" + Escape(AcceptedSummary ?? String.Empty) + "\"}";
        }

        private static string Escape(string value)
        {
            var escaped = new StringBuilder(value.Length + 8);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': escaped.Append("\\\""); break;
                    case '\\': escaped.Append("\\\\"); break;
                    case '\b': escaped.Append("\\b"); break;
                    case '\f': escaped.Append("\\f"); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default:
                        if (character < 0x20) escaped.Append("\\u").Append(((int)character).ToString("x4"));
                        else escaped.Append(character);
                        break;
                }
            }
            return escaped.ToString();
        }
    }

    public sealed class CalibrationWizard
    {
        public CalibrationWizardState State { get; private set; } = CalibrationWizardState.Idle;

        public void BeginCapture()
        {
            Require(CalibrationWizardState.Idle);
            State = CalibrationWizardState.Capturing;
        }

        public void Review()
        {
            Require(CalibrationWizardState.Capturing);
            State = CalibrationWizardState.Review;
        }

        public void Redo()
        {
            Require(CalibrationWizardState.Review);
            State = CalibrationWizardState.Idle;
        }

        /// <summary>Creates a synthetic state record only. The caller must evaluate configured development thresholds before calling.</summary>
        public SyntheticCalibrationRecord Accept(string summary)
        {
            Require(CalibrationWizardState.Review);
            if (String.IsNullOrWhiteSpace(summary)) throw new ArgumentException("A summary is required.", nameof(summary));
            State = CalibrationWizardState.Accepted;
            return new SyntheticCalibrationRecord { State = State, AcceptedSummary = summary };
        }

        private void Require(CalibrationWizardState expected)
        {
            if (State != expected) throw new InvalidOperationException("Invalid wizard transition.");
        }
    }

}
