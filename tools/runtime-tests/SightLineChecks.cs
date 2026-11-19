#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using Elts.Calibration;
using Elts.Geometry;

// Synthetic checks of the sight-line calibration algebra. Poses are generated
// from a known screen pose, gun mount and eye, with invented noise. Passing
// shows the solver recovers what it was given; it says nothing about how well
// a real tracker measures or how steadily a person aims.
static class SightLineChecks
{
    const double Width = 1.42875, Height = 0.803275; // Samsung QB65R-B lit area, metres
    const double Deg = Math.PI / 180;
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); } catch (T) { passed++; return; }
        throw new Exception("FAIL: " + name);
    }

    /// <summary>Small deterministic generator so every run sees the same captures.</summary>
    sealed class Rng
    {
        ulong state;
        public Rng(ulong seed) { state = seed * 2685821657736338717UL + 1442695040888963407UL; }
        public double Uniform()
        {
            state ^= state << 13; state ^= state >> 7; state ^= state << 17;
            return (state >> 11) * (1.0 / 9007199254740992.0);
        }
        public double Normal() => Math.Sqrt(-2 * Math.Log(1 - Uniform())) * Math.Cos(2 * Math.PI * Uniform());
        public Quaterniond Rotation()
        {
            // Uniform random rotation from four normal deviates.
            return new Quaterniond(Normal(), Normal(), Normal(), Normal());
        }
        public Quaterniond Small(double sigmaDegrees)
        {
            var axis = new Vector3d(Normal(), Normal(), Normal());
            double angle = axis.Length * sigmaDegrees * Deg;
            return angle < 1e-12 ? Quaterniond.Identity : Quaterniond.FromAxisAngle(axis, angle);
        }
    }

    static readonly Vector3d AxisX = new Vector3d(1, 0, 0), AxisY = new Vector3d(0, 1, 0), AxisZ = new Vector3d(0, 0, 1);

    /// <summary>A known rig. The gun's sight line is its own +Z axis through its origin.</summary>
    sealed class World
    {
        public RigidPose TrackingFromScreen, TrackerInGun;
        public Vector3d EyeInGun = new Vector3d(0, 0, -0.5), EyeInHeadTracker = new Vector3d(0.03, -0.11, 0.07);
        public double NoiseM, NoiseDeg, WobbleDeg, CantDeg;
        public Rng Random = new Rng(1);

        public static World Create(Rng random, bool noisy)
        {
            // Base station anywhere in front of the screen, yawed freely, with "up" a couple of degrees off.
            var orientation = Quaterniond.FromAxisAngle(AxisY, random.Uniform() * 2 * Math.PI) * random.Small(1.5);
            var basePosition = new Vector3d(Width / 2 + (random.Uniform() - 0.5) * 4, 0.3 + random.Uniform() * 1.2, -0.2 - random.Uniform() * 3);
            var screenFromTracking = new RigidPose(basePosition, orientation);
            return new World
            {
                TrackingFromScreen = screenFromTracking.Inverse,
                // Tracker within 15 cm of the sight line, turned any way at all.
                TrackerInGun = new RigidPose(new Vector3d((random.Uniform() - 0.5) * 0.24, (random.Uniform() - 0.5) * 0.24, random.Uniform() * 0.3), random.Rotation()),
                NoiseM = noisy ? 0.001 : 0, NoiseDeg = noisy ? 0.1 : 0, WobbleDeg = noisy ? 0.15 : 0, CantDeg = noisy ? 4 : 0, Random = random,
            };
        }

        public SightLineModel Truth()
        {
            var gunFromTracker = TrackerInGun.Inverse;
            return new SightLineModel(TrackingFromScreen.Inverse, gunFromTracker.TransformPoint(Vector3d.Zero), gunFromTracker.TransformDirection(AxisZ));
        }

        /// <summary>The shooter's eye is at <paramref name="eye"/> (screen frame) and the sights are on the dot.</summary>
        public AimCapture Aim(Vector3d eye, double u, double v, int station = 0, bool perfect = false, double biasPitchDeg = 0, double biasYawDeg = 0, double extraErrorDeg = 0)
        {
            var forward = (new Vector3d(u, v, 0) - eye).Normalized();
            var right = Vector3d.Cross(AxisY, forward).Normalized();
            var gunOrientation = SightLineSolver.FromBasis(right, Vector3d.Cross(forward, right), forward);
            double wobble = perfect ? 0 : WobbleDeg, cant = perfect ? 0 : CantDeg * Random.Normal();
            gunOrientation = gunOrientation
                * Quaterniond.FromAxisAngle(AxisX, (biasPitchDeg + extraErrorDeg + wobble * Random.Normal()) * Deg + 1e-300)
                * Quaterniond.FromAxisAngle(AxisY, (biasYawDeg + wobble * Random.Normal()) * Deg + 1e-300)
                * Quaterniond.FromAxisAngle(AxisZ, cant * Deg + 1e-300);
            var gun = new RigidPose(eye - gunOrientation.Rotate(EyeInGun), gunOrientation);
            var headOrientation = gunOrientation * Quaterniond.FromAxisAngle(AxisZ, -8 * Deg) * Quaterniond.FromAxisAngle(AxisY, 6 * Deg);
            var head = new RigidPose(eye - headOrientation.Rotate(EyeInHeadTracker), headOrientation);
            return new AimCapture(Observe(SightLineSolver.Compose(gun, TrackerInGun), perfect), u, v, Observe(head, perfect), station);
        }

        private RigidPose Observe(RigidPose inScreen, bool perfect)
        {
            var tracked = SightLineSolver.Compose(TrackingFromScreen, inScreen);
            if (perfect || (NoiseM == 0 && NoiseDeg == 0)) return tracked;
            return new RigidPose(tracked.Position + new Vector3d(Random.Normal(), Random.Normal(), Random.Normal()) * NoiseM,
                tracked.Orientation * Random.Small(NoiseDeg));
        }
    }

    // Standing spots in the screen frame: eye 60 cm above the screen's lower edge, 5 ft back; then far, close-left, close-right.
    static readonly Vector3d Centre = new Vector3d(Width / 2, 0.60, -1.524), Far = new Vector3d(Width / 2 + 0.35, 0.57, -2.3),
        CloseLeft = new Vector3d(Width / 2 - 0.8, 0.55, -1.0), CloseRight = new Vector3d(Width / 2 + 0.8, 0.55, -1.0),
        Moved = new Vector3d(Width / 2 - 0.4, 0.55, -2.134);
    static readonly double[][] Nine = { new[] { .5, .5 }, new[] { .2, .8 }, new[] { .8, .8 }, new[] { .8, .2 }, new[] { .2, .2 }, new[] { .5, .8 }, new[] { .8, .5 }, new[] { .5, .2 }, new[] { .2, .5 } };
    static readonly double[][] Five = { new[] { .5, .5 }, new[] { .15, .85 }, new[] { .85, .85 }, new[] { .85, .15 }, new[] { .15, .15 } };

    static List<AimCapture> RigCaptures(World world)
    {
        var captures = new List<AimCapture>();
        void Station(Vector3d eye, double[][] dots, int index)
        { foreach (var dot in dots) captures.Add(world.Aim(eye + new Vector3d(world.Random.Normal(), world.Random.Normal(), world.Random.Normal()) * (world.NoiseM > 0 ? 0.01 : 0), dot[0] * Width, dot[1] * Height, index)); }
        Station(Centre, Nine, 0); Station(Far, Five, 1); Station(CloseLeft, Five, 2); Station(CloseRight, Five, 3);
        return captures;
    }

    /// <summary>Mean distance on the glass (metres) between perfectly aimed dots and where the model says the sights point.</summary>
    static double GlassError(World world, SightLineModel model, Vector3d eye, double biasPitchDeg = 0, double biasYawDeg = 0)
    {
        double sum = 0; int count = 0;
        foreach (double fu in new[] { .1, .3, .5, .7, .9 })
            foreach (double fv in new[] { .15, .5, .85 })
            {
                var capture = world.Aim(eye, fu * Width, fv * Height, 0, perfect: true, biasPitchDeg, biasYawDeg);
                if (!model.TryGlassPoint(capture.Gun, out double u, out double v, out _)) return double.PositiveInfinity;
                sum += Math.Sqrt(Math.Pow(u - fu * Width, 2) + Math.Pow(v - fv * Height, 2)); count++;
            }
        return sum / count;
    }

    static void Main()
    {
        var random = new Rng(20261119);

        // Basis and frame helpers.
        for (int i = 0; i < 20; i++)
        {
            var q = random.Rotation();
            var basis = SightLineSolver.FromBasis(q.Rotate(AxisX), q.Rotate(AxisY), q.Rotate(AxisZ));
            True((basis.Rotate(AxisX) - q.Rotate(AxisX)).Length < 1e-9 && (basis.Rotate(AxisY) - q.Rotate(AxisY)).Length < 1e-9 && (basis.Rotate(AxisZ) - q.Rotate(AxisZ)).Length < 1e-9,
                "basis rotation reproduces its three axes");
        }
        var tilted = new ScreenPlane(new Vector3d(-0.7, 1.05, 0.2), new Vector3d(0.98, 0, 0.199), new Vector3d(0, 1, 0), Width, Height);
        var roomFromScreen = SightLineSolver.RoomFromScreen(tilted);
        True((roomFromScreen.TransformPoint(new Vector3d(0.3, 0.2, 0)) - (tilted.Origin + tilted.U * 0.3 + tilted.V * 0.2)).Length < 1e-9
            && (roomFromScreen.TransformDirection(AxisZ) - tilted.Normal).Length < 1e-9, "screen frame maps onto the room's screen plane and normal");

        // Exact captures: any mount orientation, any base-station placement.
        var timer = Stopwatch.StartNew(); double slowest = 0;
        for (int trial = 0; trial < 12; trial++)
        {
            var world = World.Create(random, noisy: false);
            var before = Stopwatch.StartNew();
            var fit = SightLineSolver.SolveRig(RigCaptures(world));
            slowest = Math.Max(slowest, before.Elapsed.TotalMilliseconds);
            True(fit.RmsDegrees < 0.01 && fit.DroppedIndices.Count == 0, "exact captures fit to under a hundredth of a degree (trial " + trial + ", rms " + fit.RmsDegrees + ")");
            True(GlassError(world, fit.Model, Centre) < 0.0005 && GlassError(world, fit.Model, Moved) < 0.0005, "exact fit predicts fresh aims within half a millimetre");
            var truth = world.Truth();
            True(Math.Acos(Math.Min(1, Vector3d.Dot(fit.Model.LineDirection, truth.LineDirection))) / Deg < 0.2, "sight-line direction recovered for an arbitrary mount");
            True(fit.MinimumRangeM > 0.1 && fit.MaximumRangeM < 4, "shooter is placed in front of the glass at a believable distance");
            True(fit.Model.ScreenFromTracking.TransformDirection(AxisY).Y > 0.9, "the fit keeps the room the right way up");
        }

        // Invented hold wobble, cant and tracker noise.
        double sameSum = 0, movedSum = 0, rmsSum = 0; const int Trials = 24;
        for (int trial = 0; trial < Trials; trial++)
        {
            var world = World.Create(random, noisy: true);
            var fit = SightLineSolver.SolveRig(RigCaptures(world));
            sameSum += GlassError(world, fit.Model, Centre); movedSum += GlassError(world, fit.Model, Moved); rmsSum += fit.RmsDegrees;
        }
        True(sameSum / Trials < 0.006 && movedSum / Trials < 0.009, "noisy captures still predict the glass point within a centimetre on average (" + (sameSum / Trials * 1000).ToString("F1") + " mm, " + (movedSum / Trials * 1000).ToString("F1") + " mm)");
        True(rmsSum / Trials > 0.05 && rmsSum / Trials < 0.6, "reported residual reflects the injected noise");

        // One slipped trigger pull is set aside.
        {
            var world = World.Create(random, noisy: true);
            var captures = RigCaptures(world);
            captures[11] = world.Aim(Far, 0.85 * Width, 0.85 * Height, 1, extraErrorDeg: 6);
            var fit = SightLineSolver.SolveRig(captures);
            True(fit.DroppedIndices.Count == 1 && fit.DroppedIndices[0] == 11 && double.IsNaN(fit.ResidualDegrees[11]), "a capture six degrees off is set aside");
            True(GlassError(world, fit.Model, Centre) < 0.012, "the fit survives the mis-aimed capture");
        }

        // Personal zero: a shooter who consistently sights 0.4 degrees off.
        {
            var world = World.Create(random, noisy: true);
            var rig = SightLineSolver.SolveRig(RigCaptures(world)).Model;
            var stance = new Vector3d(Width / 2 + 0.15, 0.67, -1.829);
            double untouched = GlassError(world, rig, stance, 0.3, -0.27);
            var shots = new List<AimCapture>();
            foreach (var dot in Five) shots.Add(world.Aim(stance, dot[0] * Width, dot[1] * Height, 0, false, 0.3, -0.27));
            var zero = SightLineSolver.SolveZero(rig, shots);
            double zeroed = GlassError(world, zero.Model, stance, 0.3, -0.27);
            True(untouched > 0.007 && zeroed < 0.006 && zeroed < untouched, "a five-dot zero removes a consistent sighting offset (" + (untouched * 1000).ToString("F1") + " mm to " + (zeroed * 1000).ToString("F1") + " mm)");
            True(zero.DirectionChangeDegrees > 0.2 && zero.DirectionChangeDegrees < 0.7, "the zero reports how far it moved the line");
            True((zero.Model.ScreenFromTracking.Position - rig.ScreenFromTracking.Position).Length == 0, "a zero never moves the screen");
            var checkOnly = SightLineSolver.Evaluate(zero.Model, shots);
            True(Math.Abs(checkOnly.RmsDegrees - zero.RmsDegrees) < 1e-9, "evaluate reports the same residuals without refitting");
        }

        // Eye from the sight line.
        {
            var world = World.Create(random, noisy: false);
            var rig = SightLineSolver.SolveRig(RigCaptures(world)).Model;
            var shots = new List<AimCapture>();
            foreach (var dot in Five) shots.Add(world.Aim(Centre, dot[0] * Width, dot[1] * Height));
            // Along the line, the eye sits this far past the point nearest the head tracker (known here from the truth).
            var truth = world.Truth();
            var first = shots[0];
            var lineOrigin = first.Gun.TransformPoint(truth.LineOrigin); var lineDirection = first.Gun.TransformDirection(truth.LineDirection);
            var trueEyeTracking = first.Head!.Value.TransformPoint(world.EyeInHeadTracker);
            double towardTarget = Vector3d.Dot(trueEyeTracking - lineOrigin, lineDirection) - Vector3d.Dot(first.Head.Value.Position - lineOrigin, lineDirection);
            var placed = SightLineSolver.EyeInHeadTracker(rig, shots, towardTarget);
            True((placed.EyeInHeadTracker - world.EyeInHeadTracker).Length < 0.005 && placed.Count == 5 && placed.SpreadM < 0.005, "the aiming eye is found on the sight line within half a centimetre");
            var unaided = SightLineSolver.EyeInHeadTracker(rig, shots);
            var miss = unaided.EyeInHeadTracker - world.EyeInHeadTracker;
            True(miss.Length < 0.12, "without the along-line distance the eye is still within the tracker-to-eye span");
            var headless = new List<AimCapture>();
            foreach (var shot in shots) headless.Add(new AimCapture(shot.Gun, shot.U, shot.V));
            Throws<ArgumentException>(() => SightLineSolver.EyeInHeadTracker(rig, headless), "eye placement needs a tracked head");
        }

        // Refusals.
        {
            var world = World.Create(random, noisy: false);
            var captures = RigCaptures(world);
            Throws<ArgumentException>(() => SightLineSolver.SolveRig(captures.GetRange(0, 5)), "too few captures are refused");
            var repeated = new List<AimCapture>();
            for (int i = 0; i < 8; i++) repeated.Add(world.Aim(Centre, 0.5 * Width, 0.5 * Height));
            Throws<ArgumentException>(() => SightLineSolver.SolveRig(repeated), "one dot aimed at repeatedly is refused");
            Throws<ArgumentException>(() => SightLineSolver.SolveZero(world.Truth(), captures.GetRange(0, 1)), "a one-dot zero is refused");
            var away = world.Aim(Centre, 0.5 * Width, 0.5 * Height, 0, true);
            var turned = new RigidPose(away.Gun.Position, away.Gun.Orientation * Quaterniond.FromAxisAngle(world.Truth().LineDirection.X > 0.9 ? AxisY : AxisX, Math.PI));
            True(!world.Truth().TryGlassPoint(new RigidPose(turned.Position, FlipLine(away.Gun.Orientation, world.Truth().LineDirection)), out _, out _, out _), "a gun pointing away has no glass point");
        }

        Console.WriteLine("INFO: slowest rig solve " + slowest.ToString("F0") + " ms; total " + timer.Elapsed.TotalSeconds.ToString("F1") + " s");
        Console.WriteLine("PASS: " + passed + " sight-line checks");
    }

    /// <summary>Turns a tracker pose half a turn about an axis across the sight line, so the line points backward.</summary>
    static Quaterniond FlipLine(Quaterniond orientation, Vector3d lineDirection)
    {
        var seed = Math.Abs(lineDirection.X) < 0.8 ? AxisX : AxisY;
        var across = (seed - lineDirection * Vector3d.Dot(seed, lineDirection)).Normalized();
        return orientation * Quaterniond.FromAxisAngle(across, Math.PI);
    }
}
