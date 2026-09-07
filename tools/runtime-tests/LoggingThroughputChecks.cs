#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Tracking;

internal static class LoggingThroughputChecks
{
    private static readonly string Hash = new string('b', 64);
    private static int passed;
    private static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    private static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "elts-logging-throughput-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            RejectsMalformedPairs(root);
            EnforcesSchemaBoundIdentifiers(root);
            DrainsSamplesDuringCriticalFlood(root);
            Console.WriteLine("PASS: " + passed + " logging throughput checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void RejectsMalformedPairs(string root)
    {
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "malformed"), Provenance());
        writer.Start();
        Throws<ArgumentException>(() => writer.TryLogSample(default), "default paired sample is rejected before enqueue");
        True(writer.Close(TimeSpan.FromSeconds(2)).CompleteOutput, "malformed sample does not poison session close");
    }

    private static void EnforcesSchemaBoundIdentifiers(string root)
    {
        Throws<ArgumentException>(() => LoggingRunDirectory.ValidateRunId("é"), "run ID rejects non-ASCII characters");
        string maximum = new string('a', 80);
        LoggingRunDirectory.ReserveUnique(root, maximum);
        LoggingRunLease rerun = LoggingRunDirectory.ReserveUnique(root, maximum);
        True(rerun.RunId.Length <= 80 && rerun.RunId.EndsWith("-r0001", StringComparison.Ordinal), "rerun suffix remains inside schema run-ID bound");
        var clock = new ManualSharedClock(DateTimeOffset.UtcNow);
        TrackingAcquisitionStamp stamp = TrackingAcquisitionStamp.Capture(clock, 1);
        Throws<ArgumentException>(() => new TrackingSamplePair(TrackingSample.Valid(stamp, new TrackerId("not schema compatible"), new RigidPose(Vector3d.Zero, Quaterniond.Identity)), TrackingSample.Valid(stamp, new TrackerId("WEAPON-001"), new RigidPose(Vector3d.Zero, Quaterniond.Identity))), "tracker identifier incompatible with sample schema is rejected");
    }
    private static void DrainsSamplesDuringCriticalFlood(string root)
    {
        const int sampleCount = 200;
        const int eventCount = 1024;
        var clock = new ManualSharedClock(DateTimeOffset.UtcNow);
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "throughput");
        var writer = new SessionLogWriter(lease, Provenance(), new LoggingConfiguration(512, 2048, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)));
        writer.Start();
        for (int i = 1; i <= sampleCount; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(4));
            TrackingAcquisitionStamp stamp = TrackingAcquisitionStamp.Capture(clock, i);
            var head = TrackingSample.Valid(stamp, new TrackerId("HEAD-001"), new RigidPose(new Vector3d(i, 0, 1), Quaterniond.Identity));
            var weapon = TrackingSample.Invalid(stamp, new TrackerId("WEAPON-001"), TrackingConnectionState.Connected, TrackingValidity.OutOfRange);
            True(writer.TryLogSample(new TrackingSamplePair(head, weapon)), "sample " + i + " enqueues");
        }
        for (int i = 1; i <= eventCount; i++) True(writer.TryLogEvent(new SessionEvent(i, clock.Now, "critical-event")), "event " + i + " enqueues");
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(1));
        True(close.Completed && close.CompleteOutput, "critical traffic does not impose a per-sample queue timeout");
        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(lease.DirectoryPath, "session-summary.json")));
        JsonElement counts = summary.RootElement.GetProperty("counts");
        True(counts.GetProperty("writtenSamples").GetInt64() == sampleCount, "all samples drain during critical flood");
        True(counts.GetProperty("writtenEvents").GetInt64() == eventCount, "all critical events drain during flood");
    }

    private static SessionProvenance Provenance() => new SessionProvenance("throughput-test", "0220025", Hash, Hash, Hash, true, DateTimeOffset.UtcNow);
}
