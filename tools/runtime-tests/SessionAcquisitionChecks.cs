#nullable enable
using System;
using System.Threading;
using Elts.Clock;
using Elts.Operator;
using Elts.Tracking;

internal static class SessionAcquisitionChecks
{
    private static int passed;
    private static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }

    private static int Main()
    {
        CadenceAndPairs(); SourceStallIsObservable(); StopTimeoutIsBounded();
        Console.WriteLine($"PASS: {passed} session acquisition checks"); return 0;
    }

    private static void CadenceAndPairs()
    {
        var acquisition = new DevelopmentSessionAcquisition(new SyntheticTrackingSettings(new SharedMonotonicClock(), sampleRateHz: 250), _ => true);
        acquisition.Start(); Thread.Sleep(500); acquisition.StopAsync().GetAwaiter().GetResult();
        var pair = acquisition.Snapshot;
        True(pair.HasValue && pair.Value.IsValid, "latest pair is synchronized and loggable");
        True(acquisition.AcquiredCount > 50, "configured cadence acquires pairs");
        True(acquisition.ActualElapsedRateHz > 100 && acquisition.ActualElapsedRateHz < 350, "observed rate is bounded and reported");
    }

    private static void SourceStallIsObservable()
    {
        var acquisition = new DevelopmentSessionAcquisition(new SyntheticTrackingSettings(new SharedMonotonicClock(), sampleRateHz: 250), _ => { Thread.Sleep(12); return true; });
        acquisition.Start(); Thread.Sleep(120); acquisition.StopAsync().GetAwaiter().GetResult();
        True(acquisition.SkippedCount > 0, "source gating stalls are exposed");
    }

    private static void StopTimeoutIsBounded()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var acquisition = new DevelopmentSessionAcquisition(new SyntheticTrackingSettings(new SharedMonotonicClock(), sampleRateHz: 250), _ => { entered.Set(); release.Wait(); return true; });
        acquisition.Start(); True(entered.Wait(1000), "logger callback entered");
        try { acquisition.StopAsync().GetAwaiter().GetResult(); throw new Exception("FAIL: stop timeout did not throw"); }
        catch (TimeoutException) { passed++; }
        finally { release.Set(); }
        acquisition.StopAsync().GetAwaiter().GetResult();
        True(!acquisition.IsRunning, "producer eventually joins after bounded timeout");
    }
}
