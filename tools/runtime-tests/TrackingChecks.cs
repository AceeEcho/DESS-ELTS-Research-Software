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
        var weaponTracker = new TrackerId("SYNTHETIC-WEAPON-001");
        var pose = new RigidPose(new Vector3d(1, 2, 3), Quaterniond.Identity);
        var firstAcquisition = TrackingAcquisitionStamp.Capture(clock, 1);
        var valid = TrackingSample.Valid(firstAcquisition, tracker, pose);
        var pairedInvalid = TrackingSample.Invalid(firstAcquisition, weaponTracker, TrackingConnectionState.Connected, TrackingValidity.OutOfRange);
        clock.Advance(TimeSpan.FromTicks(1));
        var invalid = TrackingSample.Invalid(TrackingAcquisitionStamp.Capture(clock, 2), tracker, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable);
        True(valid.HasValidPose && valid.Pose.HasValue && valid.Validity == TrackingValidity.Valid && valid.Connection == TrackingConnectionState.Connected, "valid sample explicitly carries valid pose and connection state");
        True(!invalid.HasValidPose && !invalid.Pose.HasValue && invalid.Validity == TrackingValidity.Unavailable && invalid.Connection == TrackingConnectionState.Disconnected, "invalid sample explicitly has no usable pose");
        True(valid.Timestamp < invalid.Timestamp, "valid and invalid samples share ordering authority");
        True(valid.Sequence == pairedInvalid.Sequence && valid.Timestamp == pairedInvalid.Timestamp, "paired tracker observations reuse one capture stamp");
        True(!default(TrackingSample).HasValidPose && !default(TrackingSample).Pose.HasValue && default(TrackingSample).Validity == TrackingValidity.Unavailable, "default sample is explicitly unusable");
        True(default(TrackerId).GetHashCode() == 0, "default tracker identifier hashes safely");
        Throws<ArgumentException>(() => TrackingSample.Invalid(firstAcquisition, tracker, TrackingConnectionState.Connected, TrackingValidity.Valid), "invalid factory rejects valid status");
        Throws<ArgumentException>(() => TrackingSample.Invalid(firstAcquisition, default, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable), "sample factory rejects default tracker ID");
        Throws<ArgumentException>(() => TrackingSample.Invalid(firstAcquisition, tracker, TrackingConnectionState.Connected, (TrackingValidity)99), "invalid factory rejects unknown status");
        Throws<ArgumentException>(() => TrackingSample.Valid(firstAcquisition, tracker, default), "valid factory rejects default pose");
        Throws<ArgumentOutOfRangeException>(() => TrackingAcquisitionStamp.Capture(clock, 0), "acquisition sequence starts at one");
        Throws<ArgumentException>(() => new TrackingSourceIdentity(" "), "source identity is required");

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
