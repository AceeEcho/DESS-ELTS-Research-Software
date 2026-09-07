#nullable enable
using System;
using Elts.Clock;
using Elts.Geometry;
using Elts.Tracking;

static class TrackingChecks
{
    static int passed;
    static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    static void Near(double actual, double expected, double tolerance, string name) => True(Math.Abs(actual - expected) <= tolerance, name + ": " + actual + " != " + expected);
    static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }

    static void Main()
    {
        var anchor = new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);
        var clock = new ManualSharedClock(anchor);
        True(clock.UtcStartupAnchor == anchor, "UTC startup anchor is preserved");
        True(clock.Now == MonotonicTimestamp.Zero, "manual clock starts at zero");
        var first = clock.Advance(TimeSpan.FromMilliseconds(5));
        var second = clock.AdvanceTicks(7);
        True(first < second && second.Ticks == TimeSpan.TicksPerMillisecond * 5 + 7, "manual clock produces ordered timestamps");
        Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)), "manual clock rejects backward time");

        var tracker = new TrackerId("SYNTHETIC-HEAD-001");
        var pose = new RigidPose(new Vector3d(1, 2, 3), Quaterniond.Identity);
        var valid = TrackingSample.Valid(clock, tracker, pose);
        clock.Advance(TimeSpan.FromTicks(1));
        var invalid = TrackingSample.Invalid(clock, tracker, TrackingValidity.Unavailable);
        True(valid.HasValidPose && valid.Pose.HasValue && valid.Validity == TrackingValidity.Valid, "valid sample explicitly carries valid pose");
        True(!invalid.HasValidPose && !invalid.Pose.HasValue && invalid.Validity == TrackingValidity.Unavailable, "invalid sample explicitly has no usable pose");
        True(valid.Timestamp < invalid.Timestamp, "valid and invalid samples share ordering authority");
        Throws<ArgumentException>(() => TrackingSample.Invalid(clock, tracker, TrackingValidity.Valid), "invalid factory rejects valid status");
        Throws<ArgumentException>(() => TrackingSample.Invalid(clock, default, TrackingValidity.Unavailable), "sample factory rejects default tracker ID");
        Throws<ArgumentException>(() => TrackingSample.Invalid(clock, tracker, (TrackingValidity)99), "invalid factory rejects unknown status");
        Throws<ArgumentException>(() => TrackingSample.Valid(clock, tracker, default), "valid factory rejects default pose");

        var half = Math.Sqrt(0.5);
        var native = new NativeTrackingPose(new NativeTrackingVector(2, 3, 4), new NativeTrackingQuaternion(0, half, 0, half));
        var unity = TrackingCoordinateConverter.Convert(native);
        Near(unity.Position.X, 2, 1e-12, "handedness preserves x");
        Near(unity.Position.Y, 3, 1e-12, "handedness preserves y");
        Near(unity.Position.Z, -4, 1e-12, "handedness flips z once");
        var rotated = unity.TransformDirection(new Vector3d(1, 0, 0));
        Near(rotated.X, 0, 1e-12, "converted quarter turn x");
        Near(rotated.Y, 0, 1e-12, "converted quarter turn y");
        Near(rotated.Z, 1, 1e-12, "converted quarter turn forward");
        Throws<ArgumentException>(() => TrackingCoordinateConverter.Convert(new NativeTrackingPose(new NativeTrackingVector(0, 0, 0), new NativeTrackingQuaternion(0, 0, 0, 0))), "degenerate native orientation is rejected");

        var production = new SharedMonotonicClock();
        var productionFirst = production.Now;
        var productionSecond = production.Now;
        True(productionSecond >= productionFirst, "production clock is monotonic");
        True(production.UtcStartupAnchor.Offset == TimeSpan.Zero, "production UTC anchor uses UTC");
        Console.WriteLine("PASS: " + passed + " tracking checks");
    }
}
