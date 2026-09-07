#nullable enable
using System;
using Elts.Clock;
using Elts.Tracking;

static class Dev03ReviewChecks
{
    private sealed class SteppingClock : ISharedClock
    {
        private readonly long stepTicks;
        private long nextTicks;
        public SteppingClock(TimeSpan step) { stepTicks = step.Ticks; }
        public DateTimeOffset UtcStartupAnchor => DateTimeOffset.UnixEpoch;
        public MonotonicTimestamp Now
        {
            get
            {
                long result = nextTicks;
                nextTicks += stepTicks;
                return new MonotonicTimestamp(result);
            }
        }
    }

    private static void True(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
    }

    private static void Near(double actual, double expected, double tolerance, string name)
    {
        True(Math.Abs(actual - expected) <= tolerance, name + ": " + actual + " != " + expected);
    }

    private static void Throws<T>(Action action, string name) where T : Exception
    {
        try { action(); throw new Exception("FAIL: " + name + " did not throw"); }
        catch (T) { }
    }

    private static TrackingSample ReadHead(SyntheticTrackingSource source)
    {
        True(source.TryGetNext(out var head), "head sample available");
        True(source.TryGetNext(out _), "weapon sample available");
        return head;
    }

    private static void Main()
    {
        // EnsurePending reads Now to schedule, then TrackingAcquisitionStamp.Capture
        // reads it again. A clock that advances once per read exposes the split poll.
        using var steppingSource = new SyntheticTrackingSource(new SyntheticTrackingSettings(new SteppingClock(TimeSpan.FromMilliseconds(4))));
        var first = ReadHead(steppingSource);
        var second = ReadHead(steppingSource);
        True(first.Timestamp.Ticks == TimeSpan.TicksPerMillisecond * 4, "first emitted stamp came from second clock read");
        True(second.Timestamp.Ticks == TimeSpan.TicksPerMillisecond * 12, "second emitted stamp came from fourth clock read");
        True(steppingSource.SkippedAcquisitionCount == 1, "split reads report one skipped interval");

        var manualClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var skippedSettings = new SyntheticTrackingSettings(manualClock);
        using var skippedSource = new SyntheticTrackingSource(skippedSettings);
        _ = ReadHead(skippedSource);
        manualClock.Advance(TimeSpan.FromMilliseconds(100));
        var current = ReadHead(skippedSource);
        True(current.Timestamp.Ticks == TimeSpan.TicksPerMillisecond * 100, "catch-up sample carries current clock timestamp");
        True(current.Sequence == 2, "catch-up emits one current acquisition");
        True(skippedSource.SkippedAcquisitionCount == 24, "100 ms gap records missed 4 ms intervals");
        var phase = (skippedSettings.Seed % 360) * Math.PI / 180.0;
        var sampleIndexTime = 1.0 / skippedSettings.SampleRateHz;
        var expectedHeadX = skippedSettings.Motion.HeadBasePosition.X + skippedSettings.Motion.HeadSwayAmplitude.X * Math.Sin(sampleIndexTime * skippedSettings.Motion.HeadSwayFrequencyHz + phase);
        Near(current.Pose!.Value.Position.X, expectedHeadX, 1e-12, "catch-up pose follows sample index rather than 100 ms stamp");

        var stationaryClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var stationary = new SyntheticMotionSettings(new Elts.Geometry.Vector3d(1, 2, 3), Elts.Geometry.Vector3d.Zero, 0, new Elts.Geometry.Vector3d(4, 5, 6), Elts.Geometry.Vector3d.Zero, 0, 0, 0);
        using var stationarySource = new SyntheticTrackingSource(new SyntheticTrackingSettings(stationaryClock, motion: stationary));
        var stationaryFirst = ReadHead(stationarySource);
        stationaryClock.Advance(TimeSpan.FromMilliseconds(40));
        var stationarySecond = ReadHead(stationarySource);
        True(stationaryFirst.Pose.Equals(stationarySecond.Pose), "zero-amplitude fixture records stationary raw poses");

        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(manualClock, sampleRateHz: double.NaN), "NaN sample rate is rejected");
        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(manualClock, sampleRateHz: double.PositiveInfinity), "infinite sample rate is rejected");
        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(manualClock, sampleRateHz: 2000.1), "sample rate above bound is rejected");
        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(manualClock, triggerQueueCapacity: 0), "zero trigger capacity is rejected");
        Console.WriteLine("PASS: DEV-03 review counterexamples reproduce split-clock scheduling and sample-index motion conditions");
    }
}
