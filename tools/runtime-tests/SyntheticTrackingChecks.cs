#nullable enable
using System;
using Elts.Clock;
using Elts.Tracking;

static class SyntheticTrackingChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name); } catch (T) { passed++; } }
    static SyntheticTrackingSource Make(ManualSharedClock clock) => new(new SyntheticTrackingSettings(clock, dropouts: new[] { new SyntheticDropout(3, 2) }, triggerWindows: new[] { new SyntheticTriggerWindow(2, 4), new SyntheticTriggerWindow(8, 9) }));
    static void Main()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UtcNow); using var source = Make(clock); var samples = new System.Collections.Generic.List<TrackingSample>();
        while (source.TryGetNext(out var sample)) { samples.Add(sample); if (samples.Count == 20) break; }
        True(source.Source.Value == "synthetic", "source identity"); True(samples.Count == 20, "paired sample count");
        True(samples[0].Sequence == samples[1].Sequence && samples[0].Timestamp == samples[1].Timestamp, "paired acquisition stamp");
        True(samples[0].HasValidPose && samples[1].HasValidPose, "valid first pair"); True(!samples[4].HasValidPose && !samples[5].HasValidPose, "scripted dropout");
        True(samples[8].HasValidPose && samples[8].Sequence == 5, "dropout recovery and order");
        while (source.TryGetNextTrigger(out var trigger)) True(trigger.Edge == "falling", "falling edge identity");
        var clock2 = new ManualSharedClock(DateTimeOffset.UtcNow); using var repeat = Make(clock2); var clock3 = new ManualSharedClock(DateTimeOffset.UtcNow); using var repeat2 = Make(clock3);
        for (var i = 0; i < 10; i++) { var aa = repeat.TryGetNext(out var a); var bb = repeat2.TryGetNext(out var b); True(aa && bb, "repeat sample available"); True(a.Sequence == b.Sequence && a.Validity == b.Validity && a.Pose.Equals(b.Pose), "seeded repeat"); }
        Throws<ArgumentOutOfRangeException>(() => new SyntheticTrackingSettings(clock, sampleRateHz: 0), "sample rate validation"); Throws<ArgumentException>(() => new SyntheticTrackingSettings(clock, headTracker: "same", weaponTracker: "same"), "tracker identity validation"); Throws<ArgumentOutOfRangeException>(() => new SyntheticDropout(0, 1), "dropout validation");
        source.Dispose(); True(!source.TryGetNext(out _), "dispose stops samples"); True(!source.TryGetNextTrigger(out _), "dispose stops triggers"); Console.WriteLine($"PASS: {passed} synthetic tracking checks");
    }
}
