#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using Elts.Geometry;

namespace Elts.Calibration
{
    /// <summary>Development-only calibration math. Inputs use Unity room metres and unit rotations; its output can never make a study ready.</summary>
    public sealed class CalibrationDevelopmentThresholds
    {
        public CalibrationDevelopmentThresholds(double pivotRmsMeters = .002, double cornerDistanceMeters = .005, double cornerAngleDegrees = .5, double verificationMeanDegrees = 1, double verificationMaxDegrees = 2)
        {
            if (pivotRmsMeters <= 0 || cornerDistanceMeters <= 0 || cornerAngleDegrees <= 0 || verificationMeanDegrees <= 0 || verificationMaxDegrees <= 0) throw new ArgumentOutOfRangeException();
            PivotRmsMeters = pivotRmsMeters; CornerDistanceMeters = cornerDistanceMeters; CornerAngleDegrees = cornerAngleDegrees; VerificationMeanDegrees = verificationMeanDegrees; VerificationMaxDegrees = verificationMaxDegrees;
        }
        public double PivotRmsMeters { get; } public double CornerDistanceMeters { get; } public double CornerAngleDegrees { get; } public double VerificationMeanDegrees { get; } public double VerificationMaxDegrees { get; }
    }

    public sealed class PivotSolution
    {
        internal PivotSolution(Vector3d offset, Vector3d point, double rms, int count) { TipOffsetMeters = offset; PivotWorldPointMeters = point; RmsResidualMeters = rms; SampleCount = count; }
        public Vector3d TipOffsetMeters { get; } public Vector3d PivotWorldPointMeters { get; } public double RmsResidualMeters { get; } public int SampleCount { get; }
    }

    public static class PivotCalibrationSolver
    {
        /// <summary>Solves R_i * tipOffset - pivotPoint = -trackerPosition_i by least squares. Diverse rotations are required for conditioning.</summary>
        public static PivotSolution Solve(IReadOnlyList<RigidPose> poses, int minimumSamples = 200)
        {
            if (poses == null || poses.Count < minimumSamples) throw new ArgumentException("At least the configured number of valid poses is required.", nameof(poses));
            var normal = new double[6, 6]; var rhs = new double[6];
            foreach (var pose in poses)
            {
                var r = Matrix(pose.Orientation); var a = new double[3, 6];
                for (int row = 0; row < 3; row++) { for (int col = 0; col < 3; col++) a[row, col] = r[row, col]; a[row, row + 3] = -1; }
                AddNormal(a, new[] { -pose.Position.X, -pose.Position.Y, -pose.Position.Z }, normal, rhs);
            }
            var x = SolveLinear(normal, rhs); var offset = new Vector3d(x[0], x[1], x[2]); var point = new Vector3d(x[3], x[4], x[5]); double sum = 0;
            foreach (var pose in poses) { var e = pose.TransformPoint(offset) - point; sum += e.LengthSquared; }
            return new PivotSolution(offset, point, Math.Sqrt(sum / poses.Count), poses.Count);
        }
        static double[,] Matrix(Quaterniond q) { return new[,] { { 1-2*(q.Y*q.Y+q.Z*q.Z), 2*(q.X*q.Y-q.Z*q.W), 2*(q.X*q.Z+q.Y*q.W) }, { 2*(q.X*q.Y+q.Z*q.W), 1-2*(q.X*q.X+q.Z*q.Z), 2*(q.Y*q.Z-q.X*q.W) }, { 2*(q.X*q.Z-q.Y*q.W), 2*(q.Y*q.Z+q.X*q.W), 1-2*(q.X*q.X+q.Y*q.Y) } }; }
        static void AddNormal(double[,] a, double[] b, double[,] n, double[] rhs) { for(int i=0;i<6;i++) for(int j=0;j<6;j++) for(int k=0;k<3;k++) n[i,j]+=a[k,i]*a[k,j]; for(int i=0;i<6;i++) for(int k=0;k<3;k++) rhs[i]+=a[k,i]*b[k]; }
        static double[] SolveLinear(double[,] a, double[] b) { var m=(double[,])a.Clone(); var x=(double[])b.Clone(); for(int c=0;c<6;c++) { int p=c; for(int r=c+1;r<6;r++) if(Math.Abs(m[r,c])>Math.Abs(m[p,c]))p=r; if(Math.Abs(m[p,c])<1e-10) throw new ArgumentException("Pivot poses are degenerate; rotate through multiple axes."); for(int j=c;j<6;j++){var t=m[c,j];m[c,j]=m[p,j];m[p,j]=t;} var tb=x[c];x[c]=x[p];x[p]=tb; double d=m[c,c]; for(int j=c;j<6;j++)m[c,j]/=d; x[c]/=d; for(int r=0;r<6;r++) if(r!=c){double f=m[r,c];for(int j=c;j<6;j++)m[r,j]-=f*m[c,j];x[r]-=f*x[c];} } return x; }
    }

    public sealed class CornerSolution
    {
        internal CornerSolution(Vector3d origin, Vector3d u, Vector3d v, double error) { OriginMeters=origin; U= u; V=v; BottomRightConsistencyMeters=error; }
        public Vector3d OriginMeters { get; } public Vector3d U { get; } public Vector3d V { get; } public double WidthMeters => U.Length; public double HeightMeters => V.Length; public double BottomRightConsistencyMeters { get; }
    }
    public static class CornerCalibrationSolver
    {
        /// <summary>Each input is the one-second mean of valid probe-tip samples, collected by the caller.</summary>
        public static CornerSolution Solve(Vector3d topLeft, Vector3d topRight, Vector3d bottomLeft, Vector3d bottomRight)
        {
            var u=topRight-topLeft; var raw=bottomLeft-topLeft; double width=u.Length, height=raw.Length; if(width<1e-8||height<1e-8)throw new ArgumentException("Corner captures are degenerate."); var un=u/width; var perpendicular=raw-un*Vector3d.Dot(raw,un); if(perpendicular.Length<1e-8)throw new ArgumentException("Corner axes are collinear."); var v=perpendicular.Normalized()*height; double error=(bottomRight-(topLeft+u+v)).Length; return new CornerSolution(topLeft,u,v,error);
        }
    }

    public static class WeaponZeroSolver
    {
        public static Quaterniond Solve(Vector3d observedBoreDirection, Vector3d sightingDirection)
        {
            var from=observedBoreDirection.Normalized(); var to=sightingDirection.Normalized(); double dot=Math.Max(-1,Math.Min(1,Vector3d.Dot(from,to)));
            if(dot>1-1e-12)return Quaterniond.Identity; if(dot<-1+1e-12){var axis=Math.Abs(from.X)<.8?Vector3d.Cross(from,new Vector3d(1,0,0)):Vector3d.Cross(from,new Vector3d(0,1,0));return Quaterniond.FromAxisAngle(axis,Math.PI);}
            var cross=Vector3d.Cross(from,to); return new Quaterniond(cross.X,cross.Y,cross.Z,1+dot);
        }
    }

    public enum DominantEye { Left, Right }
    public sealed class EyeOffsetInput { public EyeOffsetInput(DominantEye eye, Vector3d trackerToEyeMeters) { Eye=eye; TrackerToEyeMeters=trackerToEyeMeters; } public DominantEye Eye { get; } public Vector3d TrackerToEyeMeters { get; } }
    public sealed class AngularVerificationSummary { internal AngularVerificationSummary(double[] values){ ResidualDegrees=values; double sum=0,max=0;foreach(var v in values){if(v<0||Double.IsNaN(v)||Double.IsInfinity(v))throw new ArgumentException();sum+=v;if(v>max)max=v;}MeanDegrees=sum/values.Length;MaxDegrees=max;} public IReadOnlyList<double> ResidualDegrees{get;} public double MeanDegrees{get;} public double MaxDegrees{get;} }
    public static class AngularVerification { public static AngularVerificationSummary Summarize(IReadOnlyList<double> residualDegrees){if(residualDegrees==null||residualDegrees.Count!=9)throw new ArgumentException("Exactly nine 3x3 residuals are required.");var a=new double[9];for(int i=0;i<9;i++)a[i]=residualDegrees[i];return new AngularVerificationSummary(a);} }

    public enum CalibrationWizardState { Idle, Capturing, Review, Accepted }
    public sealed class SyntheticCalibrationRecord { public string Mode => "synthetic"; public bool StudyReady => false; public string Provenance { get; set; } = "synthetic-development-only"; public CalibrationWizardState State { get; set; } public string? AcceptedSummary { get; set; } public string ToJson() => JsonSerializer.Serialize(this); }
    public sealed class CalibrationWizard
    {
        public CalibrationWizardState State { get; private set; } = CalibrationWizardState.Idle;
        public void BeginCapture(){if(State!=CalibrationWizardState.Idle)throw new InvalidOperationException();State=CalibrationWizardState.Capturing;} public void Review(){if(State!=CalibrationWizardState.Capturing)throw new InvalidOperationException();State=CalibrationWizardState.Review;} public void Redo(){if(State!=CalibrationWizardState.Review)throw new InvalidOperationException();State=CalibrationWizardState.Idle;} public SyntheticCalibrationRecord Accept(string summary){if(State!=CalibrationWizardState.Review||String.IsNullOrWhiteSpace(summary))throw new InvalidOperationException();State=CalibrationWizardState.Accepted;return new SyntheticCalibrationRecord{State=State,AcceptedSummary=summary};}
    }
}
