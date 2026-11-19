#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;

namespace Elts.Calibration
{
    // Live tracking needs three facts, and only three:
    //   1. where the screen is in SteamVR's tracking frame        (6 numbers)
    //   2. where the sight line is in the gun tracker's own axes   (4 numbers)
    //   3. where the aiming eye is in the head tracker's own axes  (3 numbers)
    // Facts 1 and 2 are solved together from aimed captures: the shooter lines
    // the sights up on a dot drawn at a known place on the glass and pulls the
    // trigger, so the sight line must pass through that dot. Fact 3 follows,
    // because an aiming eye sits on the sight line.
    //
    // Frames. "Screen frame": origin at the lower-left corner of the lit screen,
    // +X right along it, +Y up along it, +Z into the screen, away from the
    // shooter, in metres. "Tracking frame": SteamVR's raw frame after the single
    // Z reflection in TrackingCoordinateConverter. Its origin and axes belong to
    // the base station, so nothing here assumes its Y axis is gravity.
    //
    // This is geometry fitted to captures. A small residual means the captures
    // agree with one another; it is not a physical accuracy measurement.

    /// <summary>One aimed capture: tracker poses in the tracking frame and the dot aimed at.</summary>
    public readonly struct AimCapture
    {
        public AimCapture(RigidPose gun, double u, double v, RigidPose? head = null, int station = 0)
        {
            if (!Finite(u) || !Finite(v)) throw new ArgumentException("Dot position must be finite.");
            Gun = gun; U = u; V = v; Head = head; Station = station;
        }
        /// <summary>Gun tracker pose, tracking frame.</summary>
        public RigidPose Gun { get; }
        /// <summary>Head tracker pose from the same moment, when it was tracked.</summary>
        public RigidPose? Head { get; }
        /// <summary>Dot position in metres right of the screen's lower-left corner.</summary>
        public double U { get; }
        /// <summary>Dot position in metres above the screen's lower-left corner.</summary>
        public double V { get; }
        /// <summary>Which standing spot this capture came from (0, 1, 2 ...).</summary>
        public int Station { get; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>The fitted screen pose and sight line. Immutable.</summary>
    public sealed class SightLineModel
    {
        public SightLineModel(RigidPose screenFromTracking, Vector3d lineOrigin, Vector3d lineDirection)
        {
            ScreenFromTracking = screenFromTracking;
            LineDirection = lineDirection.Normalized();
            // Keep one representation: the point of the line closest to the tracker origin.
            LineOrigin = lineOrigin - LineDirection * Vector3d.Dot(lineOrigin, LineDirection);
        }
        /// <summary>Maps tracking-frame points into the screen frame.</summary>
        public RigidPose ScreenFromTracking { get; }
        /// <summary>Point on the sight line, gun-tracker axes (metres), nearest the tracker origin.</summary>
        public Vector3d LineOrigin { get; }
        /// <summary>Unit direction of the sight line toward the target, gun-tracker axes.</summary>
        public Vector3d LineDirection { get; }

        /// <summary>Sight line in the screen frame for one gun-tracker pose.</summary>
        public Ray3d SightRay(RigidPose gun) => new Ray3d(
            ScreenFromTracking.TransformPoint(gun.TransformPoint(LineOrigin)),
            ScreenFromTracking.TransformDirection(gun.TransformDirection(LineDirection)));

        /// <summary>
        /// Where the sight line meets the glass, in metres from the lower-left corner.
        /// False when the gun points away from the screen or along it.
        /// </summary>
        public bool TryGlassPoint(RigidPose gun, out double u, out double v, out double rangeM)
        {
            var ray = SightRay(gun);
            u = v = rangeM = double.NaN;
            if (!(Math.Abs(ray.Direction.Z) > 1e-9)) return false;
            double t = -ray.Origin.Z / ray.Direction.Z;
            if (!(t > 1e-6)) return false;
            u = ray.Origin.X + t * ray.Direction.X;
            v = ray.Origin.Y + t * ray.Direction.Y;
            rangeM = t;
            return true;
        }

        public SightLineModel WithScreenFromTracking(RigidPose value) => new SightLineModel(value, LineOrigin, LineDirection);
        public SightLineModel WithLine(Vector3d origin, Vector3d direction) => new SightLineModel(ScreenFromTracking, origin, direction);
    }

    /// <summary>Adjustable solver values. They shape the fit; none is an acceptance limit.</summary>
    public sealed class SightLineSolverOptions
    {
        /// <summary>Assumed one-sigma hold error of one aimed capture (degrees). Scales residuals only.</summary>
        public double AimSigmaDegrees { get; set; } = 0.15;
        /// <summary>The sight line is assumed to pass within about this far of the gun tracker (metres).</summary>
        public double LineOffsetSigmaM { get; set; } = 0.15;
        /// <summary>A capture is set aside when it misses by more than this (degrees) and by more than OutlierFactor times the median.</summary>
        public double OutlierDegrees { get; set; } = 1.0;
        public double OutlierFactor { get; set; } = 4.0;
        /// <summary>At most this many captures are set aside.</summary>
        public int MaximumOutliers { get; set; } = 2;
        /// <summary>Rough gun-tracker-to-glass distances tried as starting guesses (metres).</summary>
        public double[] StartDistancesM { get; set; } = { 1.0, 1.6, 2.4 };
        /// <summary>How many of the best starting guesses are refined.</summary>
        public int RefinedStarts { get; set; } = 8;
        public int Iterations { get; set; } = 80;
    }

    /// <summary>Result of a fit, with what the operator needs to judge it.</summary>
    public sealed class SightLineFit
    {
        internal SightLineFit(SightLineModel model, double[] residualDegrees, IReadOnlyList<int> dropped, double minRange, double maxRange, double directionChangeDegrees)
        {
            Model = model; ResidualDegrees = residualDegrees; DroppedIndices = dropped;
            MinimumRangeM = minRange; MaximumRangeM = maxRange; DirectionChangeDegrees = directionChangeDegrees;
            double sum = 0, worst = -1; int count = 0, worstIndex = -1;
            for (int i = 0; i < residualDegrees.Length; i++)
            {
                double value = residualDegrees[i];
                if (double.IsNaN(value)) continue;
                sum += value * value; count++;
                if (value > worst) { worst = value; worstIndex = i; }
            }
            RmsDegrees = count > 0 ? Math.Sqrt(sum / count) : double.NaN;
            MaxDegrees = worst; WorstIndex = worstIndex; UsedCount = count;
        }
        public SightLineModel Model { get; }
        /// <summary>Angle by which each capture misses its dot after the fit; NaN for captures set aside.</summary>
        public IReadOnlyList<double> ResidualDegrees { get; }
        public double RmsDegrees { get; }
        public double MaxDegrees { get; }
        public int WorstIndex { get; }
        public int UsedCount { get; }
        /// <summary>Captures set aside as mis-aimed.</summary>
        public IReadOnlyList<int> DroppedIndices { get; }
        /// <summary>Nearest and farthest gun-tracker-to-glass distance along the sight line (metres).</summary>
        public double MinimumRangeM { get; }
        public double MaximumRangeM { get; }
        /// <summary>For a zero: how far the sight line direction moved (degrees). Zero for a rig fit.</summary>
        public double DirectionChangeDegrees { get; }
    }

    /// <summary>The aiming eye in head-tracker axes, with how well the captures agree.</summary>
    public readonly struct EyeEstimate
    {
        public EyeEstimate(Vector3d eyeInHeadTracker, double spreadM, int count) { EyeInHeadTracker = eyeInHeadTracker; SpreadM = spreadM; Count = count; }
        public Vector3d EyeInHeadTracker { get; }
        /// <summary>Largest distance of any single capture's estimate from the mean (metres).</summary>
        public double SpreadM { get; }
        public int Count { get; }
    }

    public static class SightLineSolver
    {
        private const double JacobianStep = 1e-6;
        /// <summary>Residual (in sigmas) given to a line that misses the glass, so such fits lose.</summary>
        private const double MissPenalty = 1e4;
        private static readonly int[] AllParameters = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        private static readonly int[] DirectionOnly = { 8, 9 };

        /// <summary>
        /// Rotation whose columns are the given axes: it turns (1,0,0) into x, (0,1,0) into y
        /// and (0,0,1) into z. The axes must be orthonormal with x cross y equal to z.
        /// </summary>
        public static Quaterniond FromBasis(Vector3d x, Vector3d y, Vector3d z)
        {
            double m00 = x.X, m01 = y.X, m02 = z.X, m10 = x.Y, m11 = y.Y, m12 = z.Y, m20 = x.Z, m21 = y.Z, m22 = z.Z;
            double trace = m00 + m11 + m22;
            if (trace > 0)
            {
                double s = Math.Sqrt(trace + 1) * 2;
                return new Quaterniond((m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s, 0.25 * s);
            }
            if (m00 > m11 && m00 > m22)
            {
                double s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
                return new Quaterniond(0.25 * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
            }
            if (m11 > m22)
            {
                double s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
                return new Quaterniond((m01 + m10) / s, 0.25 * s, (m12 + m21) / s, (m02 - m20) / s);
            }
            double w = Math.Sqrt(1 + m22 - m00 - m11) * 2;
            return new Quaterniond((m02 + m20) / w, (m12 + m21) / w, 0.25 * w, (m10 - m01) / w);
        }

        /// <summary>a then b: the pose that applies b first, then a.</summary>
        public static RigidPose Compose(RigidPose a, RigidPose b) =>
            new RigidPose(a.TransformPoint(b.Position), a.Orientation * b.Orientation);

        /// <summary>Pose of the screen frame in the room: turns screen-frame points into room points.</summary>
        public static RigidPose RoomFromScreen(ScreenPlane screen) =>
            new RigidPose(screen.Origin, FromBasis(screen.U, screen.V, screen.Normal));

        /// <summary>
        /// Solves the screen pose and the sight line together. Use captures from at
        /// least two standing spots at different distances; add a close spot to each
        /// side to pin the screen's tilt (and so the head position).
        /// </summary>
        public static SightLineFit SolveRig(IReadOnlyList<AimCapture> captures, SightLineSolverOptions? options = null)
        {
            options ??= new SightLineSolverOptions();
            if (captures == null || captures.Count < 6) throw new ArgumentException("Aim at six or more dots before solving.");
            if (DistinctDots(captures) < 4) throw new ArgumentException("Aim at four or more different dots before solving.");
            var used = new bool[captures.Count];
            for (int i = 0; i < used.Length; i++) used[i] = true;
            double sigma = options.AimSigmaDegrees * Math.PI / 180;
            Func<State, double[]> residuals = state => Residuals(state, captures, used, sigma, options.LineOffsetSigmaM);

            // Start nearest the middle of the dots: "the gun points square at the glass" is closest to true there.
            int centre = MostCentral(captures);
            var starts = new List<(double cost, State state)>();
            foreach (var direction in CandidateDirections())
                foreach (double distance in options.StartDistancesM)
                {
                    var start = InitialState(captures[centre], direction, distance);
                    starts.Add((SumSquares(residuals(start)), start));
                }
            starts.Sort((a, b) => a.cost.CompareTo(b.cost));

            State? best = null; double bestCost = double.PositiveInfinity;
            for (int i = 0; i < Math.Min(options.RefinedStarts, starts.Count); i++)
            {
                var refined = Minimize(residuals, starts[i].state, AllParameters, options.Iterations);
                double cost = SumSquares(residuals(refined));
                if (cost < bestCost && Plausible(refined, captures, used)) { best = refined; bestCost = cost; }
            }
            if (best == null)
                throw new InvalidOperationException("The captures do not fit one screen and one sight line. Check that every dot was aimed at through the sights, then capture again.");

            // Set aside a capture that disagrees wildly with the rest (a slipped trigger pull), then refit.
            var dropped = new List<int>();
            for (int round = 0; round < options.MaximumOutliers; round++)
            {
                var angles = ResidualDegrees(best.Value, captures, used);
                int worst = -1; double worstValue = 0;
                for (int i = 0; i < angles.Length; i++)
                    if (used[i] && angles[i] > worstValue) { worstValue = angles[i]; worst = i; }
                if (worst < 0 || worstValue <= options.OutlierDegrees || worstValue <= options.OutlierFactor * Median(angles, used)) break;
                if (CountUsed(used) - 1 < 6) break;
                used[worst] = false; dropped.Add(worst);
                best = Minimize(residuals, best.Value, AllParameters, options.Iterations);
            }
            return Fit(best.Value, captures, used, dropped, 0);
        }

        /// <summary>
        /// Re-aims the sight line for one shooter, or after SteamVR has shifted its
        /// frame: only the line's direction moves (two angles), pivoting at the gun.
        /// </summary>
        public static SightLineFit SolveZero(SightLineModel rig, IReadOnlyList<AimCapture> captures, SightLineSolverOptions? options = null)
        {
            options ??= new SightLineSolverOptions();
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (captures == null || captures.Count < 2) throw new ArgumentException("Aim at two or more dots to zero.");
            var used = new bool[captures.Count];
            for (int i = 0; i < used.Length; i++) used[i] = true;
            double sigma = options.AimSigmaDegrees * Math.PI / 180;
            var start = new State(rig.ScreenFromTracking, rig.LineOrigin, rig.LineDirection);
            // No offset prior: the line may only swing, so there is nothing for a prior to hold.
            var solved = Minimize(state => Residuals(state, captures, used, sigma, double.PositiveInfinity), start, DirectionOnly, options.Iterations);
            double change = Math.Acos(Math.Max(-1, Math.Min(1, Vector3d.Dot(solved.Direction, rig.LineDirection)))) * 180 / Math.PI;
            return Fit(solved, captures, used, new List<int>(), change);
        }

        /// <summary>Residuals of an existing model against captures, without changing it.</summary>
        public static SightLineFit Evaluate(SightLineModel model, IReadOnlyList<AimCapture> captures)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (captures == null || captures.Count == 0) throw new ArgumentException("At least one capture is required.");
            var used = new bool[captures.Count];
            for (int i = 0; i < used.Length; i++) used[i] = true;
            return Fit(new State(model.ScreenFromTracking, model.LineOrigin, model.LineDirection), captures, used, new List<int>(), 0);
        }

        /// <summary>
        /// The aiming eye in head-tracker axes. While aiming, the eye is on the sight
        /// line; along the line it is taken as the point nearest the head tracker,
        /// moved <paramref name="towardTargetM"/> metres toward the target (use a
        /// positive value for a tracker worn behind the eye). Captures without a
        /// tracked head are skipped.
        /// </summary>
        public static EyeEstimate EyeInHeadTracker(SightLineModel model, IReadOnlyList<AimCapture> captures, double towardTargetM = 0)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var estimates = new List<Vector3d>();
            foreach (var capture in captures)
            {
                if (!capture.Head.HasValue) continue;
                var head = capture.Head.Value;
                var origin = capture.Gun.TransformPoint(model.LineOrigin);
                var direction = capture.Gun.TransformDirection(model.LineDirection);
                double along = Vector3d.Dot(head.Position - origin, direction) + towardTargetM;
                estimates.Add(head.InverseTransformPoint(origin + direction * along));
            }
            if (estimates.Count == 0) throw new ArgumentException("No capture included a tracked head, so the eye cannot be placed.");
            var mean = Vector3d.Zero;
            foreach (var estimate in estimates) mean += estimate;
            mean /= estimates.Count;
            double spread = 0;
            foreach (var estimate in estimates) spread = Math.Max(spread, (estimate - mean).Length);
            return new EyeEstimate(mean, spread, estimates.Count);
        }

        // ---------------------------------------------------------------- internals

        /// <summary>Screen pose and line during a fit.</summary>
        private readonly struct State
        {
            public State(RigidPose screenFromTracking, Vector3d origin, Vector3d direction)
            {
                ScreenFromTracking = screenFromTracking;
                Direction = direction.Normalized();
                // The angular residual divides by the range measured from this point,
                // so it must be the same point of the line every time: the nearest one.
                Origin = origin - Direction * Vector3d.Dot(origin, Direction);
            }
            public readonly RigidPose ScreenFromTracking;
            public readonly Vector3d Origin, Direction;

            /// <summary>
            /// Parameters: 0-2 rotation of the screen pose (radians, screen axes), 3-5 its
            /// translation (metres), 6-7 line shift across itself (metres), 8-9 line tilt (radians).
            /// </summary>
            public State Apply(double[] delta, int[] free)
            {
                var full = new double[10];
                for (int k = 0; k < free.Length; k++) full[free[k]] = delta[k];
                var orientation = ScreenFromTracking.Orientation;
                double angle = Math.Sqrt(full[0] * full[0] + full[1] * full[1] + full[2] * full[2]);
                // Below this the turn is far smaller than any tracker can resolve, and too small to normalise.
                if (angle > 1e-10)
                    orientation = Quaterniond.FromAxisAngle(new Vector3d(full[0], full[1], full[2]), angle) * orientation;
                var position = ScreenFromTracking.Position + new Vector3d(full[3], full[4], full[5]);
                Perpendicular(Direction, out var u1, out var u2);
                var direction = Direction + u1 * full[8] + u2 * full[9];
                var origin = Origin + u1 * full[6] + u2 * full[7];
                return new State(new RigidPose(position, orientation), origin, direction);
            }
        }

        private static void Perpendicular(Vector3d direction, out Vector3d u1, out Vector3d u2)
        {
            var seed = Math.Abs(direction.X) < 0.8 ? new Vector3d(1, 0, 0) : new Vector3d(0, 1, 0);
            u1 = (seed - direction * Vector3d.Dot(seed, direction)).Normalized();
            u2 = Vector3d.Cross(direction, u1);
        }

        /// <summary>Where the state's line meets the glass for one capture.</summary>
        private static bool Glass(State state, RigidPose gun, out double u, out double v, out double range)
        {
            var origin = state.ScreenFromTracking.TransformPoint(gun.TransformPoint(state.Origin));
            var direction = state.ScreenFromTracking.TransformDirection(gun.TransformDirection(state.Direction));
            u = v = range = double.NaN;
            if (!(Math.Abs(direction.Z) > 1e-9)) return false;
            double t = -origin.Z / direction.Z;
            if (!(t > 1e-6)) return false;
            u = origin.X + t * direction.X; v = origin.Y + t * direction.Y; range = t;
            return true;
        }

        /// <summary>
        /// Two residuals per used capture: how far the line lands from the dot, as an
        /// angle at the gun, in units of the assumed hold error. Then three that keep
        /// the line near the tracker, which pins a direction the dots cannot.
        /// </summary>
        private static double[] Residuals(State state, IReadOnlyList<AimCapture> captures, bool[] used, double sigmaRadians, double offsetSigmaM)
        {
            var result = new List<double>(captures.Count * 2 + 3);
            for (int i = 0; i < captures.Count; i++)
            {
                if (!used[i]) continue;
                if (Glass(state, captures[i].Gun, out double u, out double v, out double range))
                {
                    result.Add((u - captures[i].U) / range / sigmaRadians);
                    result.Add((v - captures[i].V) / range / sigmaRadians);
                }
                else { result.Add(MissPenalty); result.Add(MissPenalty); }
            }
            if (!double.IsInfinity(offsetSigmaM))
            {
                result.Add(state.Origin.X / offsetSigmaM); result.Add(state.Origin.Y / offsetSigmaM); result.Add(state.Origin.Z / offsetSigmaM);
            }
            return result.ToArray();
        }

        private static double[] ResidualDegrees(State state, IReadOnlyList<AimCapture> captures, bool[] used)
        {
            var result = new double[captures.Count];
            for (int i = 0; i < captures.Count; i++)
            {
                if (!used[i]) { result[i] = double.NaN; continue; }
                result[i] = Glass(state, captures[i].Gun, out double u, out double v, out double range)
                    ? Math.Atan2(Math.Sqrt((u - captures[i].U) * (u - captures[i].U) + (v - captures[i].V) * (v - captures[i].V)), range) * 180 / Math.PI
                    : 180;
            }
            return result;
        }

        private static SightLineFit Fit(State state, IReadOnlyList<AimCapture> captures, bool[] used, IReadOnlyList<int> dropped, double directionChange)
        {
            double minimum = double.PositiveInfinity, maximum = 0;
            for (int i = 0; i < captures.Count; i++)
                if (used[i] && Glass(state, captures[i].Gun, out _, out _, out double range))
                { minimum = Math.Min(minimum, range); maximum = Math.Max(maximum, range); }
            return new SightLineFit(new SightLineModel(state.ScreenFromTracking, state.Origin, state.Direction),
                ResidualDegrees(state, captures, used), dropped, minimum, maximum, directionChange);
        }

        /// <summary>The shooter is in front of the glass, the right way up, at a believable distance.</summary>
        private static bool Plausible(State state, IReadOnlyList<AimCapture> captures, bool[] used)
        {
            // SteamVR's Y is roughly up; a fit that turns it upside down is a mirror solution.
            if (state.ScreenFromTracking.TransformDirection(new Vector3d(0, 1, 0)).Y < 0.3) return false;
            for (int i = 0; i < captures.Count; i++)
            {
                if (!used[i]) continue;
                if (state.ScreenFromTracking.TransformPoint(captures[i].Gun.Position).Z > -0.05) return false;
                if (!Glass(state, captures[i].Gun, out _, out _, out double range) || range < 0.05 || range > 12) return false;
            }
            return true;
        }

        /// <summary>
        /// First guess from one capture: the gun points square at its dot from the
        /// given distance, and SteamVR's Y is roughly up. Both are only good enough
        /// to start from; the fit then moves every number.
        /// </summary>
        private static State InitialState(AimCapture capture, Vector3d localDirection, double distanceM)
        {
            var forward = capture.Gun.TransformDirection(localDirection).Normalized();
            var right = Vector3d.Cross(new Vector3d(0, 1, 0), forward);
            if (right.Length < 0.2) right = Vector3d.Cross(new Vector3d(1, 0, 0), forward); // pointing almost straight up or down
            right = right.Normalized();
            var up = Vector3d.Cross(forward, right);
            // Turns aim axes (x right, y up, z forward) into tracking axes; its inverse is the first screen rotation.
            var rotation = FromBasis(right, up, forward).Inverse;
            var gunInScreen = new Vector3d(capture.U, capture.V, -distanceM);
            return new State(new RigidPose(gunInScreen - rotation.Rotate(capture.Gun.Position), rotation), Vector3d.Zero, localDirection);
        }

        /// <summary>Twenty-six directions spread over the sphere; the true line is within about 30 degrees of one.</summary>
        private static IEnumerable<Vector3d> CandidateDirections()
        {
            for (int x = -1; x <= 1; x++)
                for (int y = -1; y <= 1; y++)
                    for (int z = -1; z <= 1; z++)
                        if (x != 0 || y != 0 || z != 0) yield return new Vector3d(x, y, z).Normalized();
        }

        private static int MostCentral(IReadOnlyList<AimCapture> captures)
        {
            double meanU = 0, meanV = 0;
            foreach (var capture in captures) { meanU += capture.U; meanV += capture.V; }
            meanU /= captures.Count; meanV /= captures.Count;
            int best = 0; double bestDistance = double.PositiveInfinity;
            for (int i = 0; i < captures.Count; i++)
            {
                // Prefer the first standing spot, where the shooter is asked to stand square to the screen.
                double distance = Math.Sqrt(Math.Pow(captures[i].U - meanU, 2) + Math.Pow(captures[i].V - meanV, 2)) + (captures[i].Station == captures[0].Station ? 0 : 10);
                if (distance < bestDistance) { bestDistance = distance; best = i; }
            }
            return best;
        }

        private static int DistinctDots(IReadOnlyList<AimCapture> captures)
        {
            var seen = new List<(double u, double v)>();
            foreach (var capture in captures)
            {
                bool known = false;
                foreach (var dot in seen)
                    if (Math.Abs(dot.u - capture.U) < 0.01 && Math.Abs(dot.v - capture.V) < 0.01) { known = true; break; }
                if (!known) seen.Add((capture.U, capture.V));
            }
            return seen.Count;
        }

        private static int CountUsed(bool[] used) { int count = 0; foreach (bool value in used) if (value) count++; return count; }

        private static double Median(double[] values, bool[] used)
        {
            var kept = new List<double>();
            for (int i = 0; i < values.Length; i++) if (used[i]) kept.Add(values[i]);
            kept.Sort();
            return kept.Count == 0 ? 0 : kept[kept.Count / 2];
        }

        private static double SumSquares(double[] values)
        {
            double sum = 0;
            foreach (double value in values)
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) return double.PositiveInfinity;
                sum += value * value;
            }
            return sum;
        }

        /// <summary>Levenberg-Marquardt with a numeric Jacobian. Small and dense: at most ten parameters.</summary>
        private static State Minimize(Func<State, double[]> residuals, State start, int[] free, int iterations)
        {
            int n = free.Length;
            var state = start;
            double[] r0 = residuals(state);
            double c0 = SumSquares(r0), lambda = 1e-3;
            if (double.IsInfinity(c0)) return state;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var jacobian = new double[n][];
                for (int j = 0; j < n; j++)
                {
                    var delta = new double[n]; delta[j] = JacobianStep;
                    double[] r1 = residuals(state.Apply(delta, free));
                    jacobian[j] = new double[r0.Length];
                    for (int i = 0; i < r0.Length; i++) jacobian[j][i] = (r1[i] - r0[i]) / JacobianStep;
                }
                var normal = new double[n, n]; var gradient = new double[n];
                for (int a = 0; a < n; a++)
                {
                    for (int b = a; b < n; b++)
                    {
                        double sum = 0;
                        for (int i = 0; i < r0.Length; i++) sum += jacobian[a][i] * jacobian[b][i];
                        normal[a, b] = normal[b, a] = sum;
                    }
                    double g = 0;
                    for (int i = 0; i < r0.Length; i++) g += jacobian[a][i] * r0[i];
                    gradient[a] = -g;
                }
                bool improved = false;
                for (int attempt = 0; attempt < 12 && !improved; attempt++)
                {
                    var damped = (double[,])normal.Clone();
                    for (int a = 0; a < n; a++) damped[a, a] += lambda * (normal[a, a] + 1e-12);
                    double[]? step = SolveLinear(damped, gradient);
                    if (step != null)
                    {
                        State next; double[] r1; double c1;
                        // A wild trial step can leave the range of finite geometry; treat it as a failed step.
                        try { next = state.Apply(step, free); r1 = residuals(next); c1 = SumSquares(r1); }
                        catch (ArgumentException) { lambda *= 4; continue; }
                        if (c1 < c0)
                        {
                            bool meaningful = c0 - c1 > 1e-14 * (1 + c0);
                            state = next; r0 = r1; c0 = c1; lambda /= 3;
                            if (!meaningful) return state;
                            improved = true;
                            continue;
                        }
                    }
                    lambda *= 4;
                }
                if (!improved) break;
            }
            return state;
        }

        /// <summary>Gauss-Jordan elimination with partial pivoting; null when singular.</summary>
        private static double[]? SolveLinear(double[,] matrix, double[] rhs)
        {
            int n = rhs.Length;
            var m = new double[n, n + 1];
            for (int r = 0; r < n; r++) { for (int c = 0; c < n; c++) m[r, c] = matrix[r, c]; m[r, n] = rhs[r]; }
            for (int c = 0; c < n; c++)
            {
                int pivot = c;
                for (int r = c + 1; r < n; r++) if (Math.Abs(m[r, c]) > Math.Abs(m[pivot, c])) pivot = r;
                if (Math.Abs(m[pivot, c]) < 1e-14) return null;
                if (pivot != c) for (int k = 0; k <= n; k++) { double swap = m[c, k]; m[c, k] = m[pivot, k]; m[pivot, k] = swap; }
                for (int r = 0; r < n; r++)
                {
                    if (r == c) continue;
                    double factor = m[r, c] / m[c, c];
                    if (factor == 0) continue;
                    for (int k = c; k <= n; k++) m[r, k] -= factor * m[c, k];
                }
            }
            var solution = new double[n];
            for (int r = 0; r < n; r++)
            {
                solution[r] = m[r, n] / m[r, r];
                if (double.IsNaN(solution[r]) || double.IsInfinity(solution[r])) return null;
            }
            return solution;
        }
    }
}
