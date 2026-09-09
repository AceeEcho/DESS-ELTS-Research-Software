#nullable enable
using System;
using Elts.Clock;
using Elts.Geometry;
using Elts.Tracking;

static class SyntheticTrackingChecks
{
    sealed class CountingClock : ISharedClock
    {
        public int Reads { get; private set; }
        public long Ticks { get; set; }
        public DateTimeOffset UtcStartupAnchor => DateTimeOffset.UnixEpoch;
        public MonotonicTimestamp Now { get { Reads++; return new MonotonicTimestamp(Ticks); } }
    }
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name); } catch (T) { passed++; } }
    static SyntheticTrackingSource Make(ManualSharedClock clock) => new(new SyntheticTrackingSettings(clock, dropouts: new[] { new SyntheticDropout(3, 2) }, triggerWindows: new[] { new SyntheticTriggerWindow(2, 4), new SyntheticTriggerWindow(8, 9) }));
    static void Main()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UtcNow); using var source = Make(clock); var samples = new System.Collections.Generic.List<TrackingSample>();
        while (samples.Count < 20) { True(source.TryGetNext(out var firstSample), "sample due"); samples.Add(firstSample); True(source.TryGetNext(out var secondSample), "paired sample due"); samples.Add(secondSample); if (samples.Count < 20) clock.Advance(TimeSpan.FromMilliseconds(4)); }
        True(!source.TryGetNext(out _), "unchanged clock does not create unbounded samples");
        True(source.Source.Value == "synthetic", "source identity"); True(samples.Count == 20, "paired sample count");
        True(samples[0].Sequence == samples[1].Sequence && samples[0].Timestamp == samples[1].Timestamp, "paired acquisition stamp");
        True(samples[0].HasValidPose && samples[1].HasValidPose, "valid first pair"); True(!samples[4].HasValidPose && !samples[5].HasValidPose, "scripted dropout");
        True(samples[8].HasValidPose && samples[8].Sequence == 5, "dropout recovery and order");
        var triggerCount = 0; while (source.TryGetNextTrigger(out var trigger)) { True(trigger.Edge == "falling", "falling edge identity"); triggerCount++; }
        True(triggerCount == 1, "disconnect does not synthesize a release shot");
        var counting = new CountingClock(); using var counted = new SyntheticTrackingSource(new SyntheticTrackingSettings(counting)); True(counted.TryGetNext(out _), "counted first head"); True(counting.Reads == 1, "one clock read for acquisition"); True(counted.TryGetNext(out _), "counted first weapon"); True(counting.Reads == 1, "pending pair does not reread clock"); True(!counted.TryGetNext(out _), "counted unchanged clock gated"); True(counting.Reads == 2, "gated poll reads clock once");
        var clock2 = new ManualSharedClock(DateTimeOffset.UtcNow); using var repeat = Make(clock2); var clock3 = new ManualSharedClock(DateTimeOffset.UtcNow); using var repeat2 = Make(clock3);
        for (var i = 0; i < 10; i++) { var aa = repeat.TryGetNext(out var a); var bb = repeat2.TryGetNext(out var b); True(aa && bb, "repeat sample available"); True(a.Sequence == b.Sequence && a.Validity == b.Validity && a.Pose.Equals(b.Pose), "seeded repeat"); clock2.Advance(TimeSpan.FromMilliseconds(4)); clock3.Advance(TimeSpan.FromMilliseconds(4)); }
        var skippedClock = new ManualSharedClock(DateTimeOffset.UtcNow); using var skipped = new SyntheticTrackingSource(new SyntheticTrackingSettings(skippedClock)); True(skipped.TryGetNext(out _), "skipped source first acquisition"); True(skipped.TryGetNext(out _), "skipped source paired acquisition"); skippedClock.Advance(TimeSpan.FromMilliseconds(100)); True(skipped.TryGetNext(out _), "skipped source emits current pair"); True(skipped.SkippedAcquisitionCount > 0, "skipped acquisitions are counted");
        var lateClock = new ManualSharedClock(DateTimeOffset.UtcNow); using var late = new SyntheticTrackingSource(new SyntheticTrackingSettings(lateClock)); True(late.TryGetNext(out _), "slight-late first head"); True(late.TryGetNext(out _), "slight-late first weapon"); lateClock.Advance(TimeSpan.FromTicks(40100)); True(late.TryGetNext(out _), "slight-late current head"); True(late.SkippedAcquisitionCount == 0, "slight lateness does not invent a skipped poll");
        var motionClock = new ManualSharedClock(DateTimeOffset.UtcNow); var motion = new SyntheticMotionSettings(Vector3d.Zero, new Vector3d(1, 0, 0), 1, Vector3d.Zero, Vector3d.Zero, 0, 0, 0); using var motionSource = new SyntheticTrackingSource(new SyntheticTrackingSettings(motionClock, seed: 0, motion: motion)); True(motionSource.TryGetNext(out var motionAtZero), "motion zero head"); True(motionSource.TryGetNext(out _), "motion zero weapon"); motionClock.Advance(TimeSpan.FromMilliseconds(250)); True(motionSource.TryGetNext(out var motionQuarter), "motion quarter head"); True(Math.Abs(motionQuarter.Pose!.Value.Position.X - 1) < 1e-9, "motion uses captured time and Hz");
        var overflowClock = new ManualSharedClock(DateTimeOffset.UtcNow); using var overflow = new SyntheticTrackingSource(new SyntheticTrackingSettings(overflowClock, triggerLockout: TimeSpan.FromMilliseconds(1), triggerQueueCapacity: 1, triggerWindows: new[] { new SyntheticTriggerWindow(2, 2), new SyntheticTriggerWindow(4, 4) })); for (var i = 0; i < 6; i++) { True(overflow.TryGetNext(out _), "overflow fixture head"); True(overflow.TryGetNext(out _), "overflow fixture weapon"); overflowClock.Advance(TimeSpan.FromMilliseconds(4)); } True(overflow.TriggerOverflowed && overflow.DroppedTriggerCount == 1, "trigger overflow is bounded and explicit");
        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(clock, sampleRateHz: 0), "sample rate validation"); Throws<ArgumentException>(() => new SyntheticTrackingSettings(clock, headTracker: "same", weaponTracker: "same"), "tracker identity validation"); Throws<ArgumentOutOfRangeException>(() => new SyntheticDropout(0, 1), "dropout validation"); Throws<ArgumentException>(() => new SyntheticTrackingSettings(clock, dropouts: new[] { default(SyntheticDropout) }), "default dropout validation"); Throws<ArgumentException>(() => new SyntheticTrackingSettings(clock, triggerWindows: new[] { default(SyntheticTriggerWindow) }), "default trigger validation");
        var terminalClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        terminalClock.AdvanceTicks(long.MaxValue);
        using var terminal = new SyntheticTrackingSource(new SyntheticTrackingSettings(terminalClock));
        True(terminal.TryGetNext(out _) && terminal.TryGetNext(out _), "terminal timestamp permits one pair");
        True(!terminal.TryGetNext(out _), "terminal timestamp cannot cause an infinite drain");
        source.Dispose(); True(!source.TryGetNext(out _), "dispose stops samples"); True(!source.TryGetNextTrigger(out _), "dispose stops triggers"); Console.WriteLine($"PASS: {passed} synthetic tracking checks");
    }
}
