#nullable enable
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Tracking;

internal static class LoggingChecks
{
    private static int passed;
    private static readonly string Hash = new string('a', 64);
    private static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    private static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }

    private static int Main(string[] args)
    {
        try
        {
            int soakSeconds = ParseSoakSeconds(args);
            string? output = ParseOutput(args);
            if (soakSeconds > 0) RunSoak(soakSeconds, output);
            else RunChecks();
            Console.WriteLine("PASS: " + passed + " logging checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void RunChecks()
    {
        Throws<ArgumentException>(() => LoggingRunDirectory.ValidateRunId("../escape"), "run IDs reject traversal");
        Throws<ArgumentException>(() => LoggingRunDirectory.ValidateRunId("contains space"), "run IDs reject nonportable characters");
        Throws<ArgumentException>(() => new SessionProvenance("v", "r", "upper", Hash, Hash, true, DateTimeOffset.UtcNow), "hash provenance is strict");
        Throws<ArgumentException>(() => new SessionEvent(1, MonotonicTimestamp.Zero, "event", new[] { default(LogField) }), "event rejects an uninitialized field");

        string root = CreateRoot();
        try
        {
            var lease = LoggingRunDirectory.ReserveUnique(root, "synthetic-run");
            var rerun = LoggingRunDirectory.ReserveUnique(root, "synthetic-run");
            True(lease.RunId == "synthetic-run" && rerun.RunId == "synthetic-run-r0001", "run reservation uses a non-overwriting suffix");
            True(File.Exists(lease.ReservationPath), "run reservation remains as recovery evidence");
            RunSuccessfulSession(lease);
            ValidateSessionFiles(lease);
            RunOverflow(root);
            RunWriteFailure(root);
            RunCriticalOverflow(root);
            RunStalledClose(root);
        }
        finally { DeleteRoot(root); }
    }

    private static void RunSuccessfulSession(LoggingRunLease lease)
    {
        var clock = new ManualSharedClock(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        var head = new TrackerId("SYNTHETIC-HEAD-001");
        var weapon = new TrackerId("SYNTHETIC-WEAPON-001");
        var stamp = TrackingAcquisitionStamp.Capture(clock, 1);
        var pair = new TrackingSamplePair(
            TrackingSample.Valid(stamp, head, new RigidPose(new Vector3d(1, 2, 3), Quaterniond.Identity)),
            TrackingSample.Invalid(stamp, weapon, TrackingConnectionState.Reconnecting, TrackingValidity.OutOfRange));
        var writer = new SessionLogWriter(lease, Provenance(clock));
        writer.Start();
        True(writer.TryLogSample(pair), "sample enqueue succeeds");
        True(writer.TryLogTarget(new TargetSnapshot(1, stamp.Timestamp, "target-1", "block-1", TargetLifecycle.Spawned, new Vector3d(3, 4, 5), new Vector3d(0, 0, 1), 12, "synthetic-v1")), "target enqueue succeeds");
        True(writer.TryLogEvent(new SessionEvent(1, stamp.Timestamp, "trigger-edge", new[] { LogField.Boolean("pressed", true), LogField.NumberValue("pressure", 0.5) })), "event enqueue succeeds");
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(5));
        True(close.Completed && close.CompleteOutput, "normal close is complete");
    }

    private static void ValidateSessionFiles(LoggingRunLease lease)
    {
        string samples = File.ReadAllText(Path.Combine(lease.DirectoryPath, "samples.ndjson"), Encoding.UTF8);
        string events = File.ReadAllText(Path.Combine(lease.DirectoryPath, "events.ndjson"), Encoding.UTF8);
        string targets = File.ReadAllText(Path.Combine(lease.DirectoryPath, "targets.ndjson"), Encoding.UTF8);
        using JsonDocument sample = JsonDocument.Parse(samples);
        using JsonDocument evt = JsonDocument.Parse(events);
        using JsonDocument target = JsonDocument.Parse(targets);
        using JsonDocument summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(lease.DirectoryPath, "session-summary.json"), Encoding.UTF8));
        True(sample.RootElement.GetProperty("schemaVersion").GetString() == LogSchemaVersions.Samples, "sample schema identity is serialized");
        True(sample.RootElement.GetProperty("head").GetProperty("pose").ValueKind == JsonValueKind.Object && sample.RootElement.GetProperty("weapon").GetProperty("pose").ValueKind == JsonValueKind.Null, "raw valid and invalid poses retain their explicit state");
        True(evt.RootElement.GetProperty("payload").GetProperty("pressed").GetBoolean(), "event typed payload round trips");
        True(target.RootElement.GetProperty("worldPositionMeters").GetProperty("z").GetDouble() == 5, "target world position remains canonical");
        True(target.RootElement.GetProperty("schemaVersion").GetString() == "elts.targets.v2", "new target writer emits v2");
        string expiry = LogJson.Target(new TargetSnapshot(2, new MonotonicTimestamp(1), "target-1", "block-1", TargetLifecycle.Despawned, Vector3d.Zero, Vector3d.Zero, 12, "synthetic-v2"));
        True(expiry.Contains("\"schemaVersion\":\"elts.targets.v2\"") && expiry.Contains("\"lifecycle\":\"Despawned\""), "v2 expiry serializes explicitly");
        True(summary.RootElement.GetProperty("complete").GetBoolean(), "summary marks orderly session complete");
        foreach (string product in new[] { "samples.ndjson", "events.ndjson", "targets.ndjson" })
        {
            string expected = Sha256(File.ReadAllBytes(Path.Combine(lease.DirectoryPath, product)));
            string actual = summary.RootElement.GetProperty("checksumsSha256").GetProperty(product).GetString()!;
            True(expected == actual, product + " checksum covers written bytes");
        }
    }

    private static void RunOverflow(string root)
    {
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "overflow"), Provenance(new ManualSharedClock(DateTimeOffset.UtcNow)),
            new LoggingConfiguration(1, 4, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), new DelegatingSinkFactory(() => new SleepingSink(30)));
        writer.Start();
        TrackingSamplePair pair = Pair(1);
        for (int i = 0; i < 100; i++) writer.TryLogSample(pair);
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(5));
        True(writer.DroppedSampleCount > 0, "bounded sample queue reports overflow drops");
        True(close.Completed, "writer drains overflow test");
    }

    private static void RunWriteFailure(string root)
    {
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "write-failure"), Provenance(new ManualSharedClock(DateTimeOffset.UtcNow)),
            new LoggingConfiguration(), new DelegatingSinkFactory(() => new ThrowingSink()));
        writer.Start();
        writer.TryLogEvent(new SessionEvent(1, MonotonicTimestamp.Zero, "required-event"));
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(5));
        True(close.Completed && !close.CompleteOutput && !writer.Health.IsHealthy, "writer serialization failure makes the logger unhealthy");
    }

    private static void RunCriticalOverflow(string root)
    {
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "critical-overflow"), Provenance(new ManualSharedClock(DateTimeOffset.UtcNow)),
            new LoggingConfiguration(4, 1, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)), new DelegatingSinkFactory(() => new GateSink(entered, release)));
        writer.Start();
        True(writer.TryLogEvent(new SessionEvent(1, MonotonicTimestamp.Zero, "first")), "first critical event enqueues");
        True(entered.Wait(TimeSpan.FromSeconds(2)), "writer blocks at critical-event sink seam");
        True(writer.TryLogEvent(new SessionEvent(2, MonotonicTimestamp.Zero, "second")), "one queued critical event fits");
        True(!writer.TryLogEvent(new SessionEvent(3, MonotonicTimestamp.Zero, "third")) && !writer.Health.IsHealthy, "critical event queue overflow fails closed");
        release.Set();
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(5));
        True(close.Completed && !close.CompleteOutput, "critical queue overflow keeps closure incomplete");
    }
    private static void RunStalledClose(string root)
    {
        var entered = new ManualResetEventSlim(false);
        var release = new ManualResetEventSlim(false);
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "stalled-close"), Provenance(new ManualSharedClock(DateTimeOffset.UtcNow)),
            new LoggingConfiguration(), new DelegatingSinkFactory(() => new GateSink(entered, release)));
        writer.Start();
        True(entered.Wait(TimeSpan.FromSeconds(2)), "stall sink reaches injected seam");
        LogCloseResult close = writer.Close(TimeSpan.FromMilliseconds(20));
        True(!close.Completed && !close.CompleteOutput && !writer.Health.IsHealthy, "safe close reports an incomplete stalled writer");
        release.Set();
        Thread.Sleep(50);
    }

    private static void RunSoak(int seconds, string? output)
    {
        string root = output ?? CreateRoot();
        Directory.CreateDirectory(root);
        var clock = new SharedMonotonicClock();
        const int sampleRateHz = 250;
        const int sampleCapacity = 8192;
        const int criticalCapacity = 512;
        var start = new ProcessStartInfo("git", "rev-parse HEAD") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        using var git = Process.Start(start) ?? throw new Exception("Cannot resolve source revision");
        string revision = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit();
        if (git.ExitCode != 0 || revision.Length != 40) throw new Exception("Run the soak from the repository root");
        string configJson = JsonSerializer.Serialize(new { sampleRateHz, sampleCapacity, criticalCapacity, seconds, flushIntervalMilliseconds = 250 });
        string fixtureJson = "{\"mode\":\"synthetic\",\"head\":\"x=sequence%10,y=0,z=1 metres\",\"weapon\":\"connected,out-of-range,no-pose\",\"target\":\"stationary at (0,0,2) metres\"}";
        string fixtureHash = Sha256(Encoding.UTF8.GetBytes(fixtureJson));
        var provenance = new SessionProvenance("dev04-soak", revision, Sha256(Encoding.UTF8.GetBytes(configJson)), fixtureHash, fixtureHash, true, clock.UtcStartupAnchor);
        var writer = new SessionLogWriter(LoggingRunDirectory.ReserveUnique(root, "synthetic-soak"), provenance, new LoggingConfiguration(sampleCapacity, criticalCapacity));
        var sourceHashes = new Dictionary<string, string>();
        foreach (string folder in new[] { "unity/Assets/ELTS/Clock", "unity/Assets/ELTS/Geometry", "unity/Assets/ELTS/Tracking", "unity/Assets/ELTS/Logging" })
            foreach (string file in Directory.GetFiles(folder, "*.cs")) sourceHashes[file.Replace('\\', '/')] = Sha256(File.ReadAllBytes(file));
        sourceHashes["tools/runtime-tests/LoggingChecks.cs"] = Sha256(File.ReadAllBytes("tools/runtime-tests/LoggingChecks.cs"));
        var intervals = new List<double>();
        var enqueueTimes = new List<double>();
        long previousTick = -1;
        long reschedules = 0;
        writer.Start();
        Stopwatch wall = Stopwatch.StartNew();
        long sequence = 1;
        long cadenceTicks = Stopwatch.Frequency / sampleRateHz;
        long nextDueTick = Stopwatch.GetTimestamp();
        while (wall.Elapsed < TimeSpan.FromSeconds(seconds))
        {
            TrackingAcquisitionStamp stamp = TrackingAcquisitionStamp.Capture(clock, sequence);
            if (previousTick >= 0) intervals.Add((stamp.Timestamp.Ticks - previousTick) / (double)TimeSpan.TicksPerMillisecond);
            previousTick = stamp.Timestamp.Ticks;
            long enqueueStart = Stopwatch.GetTimestamp();
            bool accepted = writer.TryLogSample(new TrackingSamplePair(TrackingSample.Valid(stamp, new TrackerId("SYNTHETIC-HEAD"), new RigidPose(new Vector3d(sequence % 10, 0, 1), Quaterniond.Identity)), TrackingSample.Invalid(stamp, new TrackerId("SYNTHETIC-WEAPON"), TrackingConnectionState.Connected, TrackingValidity.OutOfRange)));
            enqueueTimes.Add((Stopwatch.GetTimestamp() - enqueueStart) * 1000.0 / Stopwatch.Frequency);
            if (!accepted || !writer.Health.IsHealthy) throw new Exception("Soak logger rejected a sample: " + writer.Health.Failure);
            if (sequence % sampleRateHz == 0)
            {
                if (!writer.TryLogEvent(new SessionEvent(sequence / sampleRateHz, stamp.Timestamp, "synthetic-second"))) throw new Exception("Soak event rejected");
                if (!writer.TryLogTarget(new TargetSnapshot(sequence / sampleRateHz, stamp.Timestamp, "target-1", "block-1", TargetLifecycle.Spawned, new Vector3d(0, 0, 2), Vector3d.Zero, 1729, "soak-v1"))) throw new Exception("Soak target rejected");
            }
            sequence++;
            nextDueTick += cadenceTicks;
            // Busy waiting preserves the configured 250 Hz cadence on Windows, where Sleep(1) can be 15 ms.
            // If a host falls behind, reschedule rather than emit a catch-up burst.
            while (true)
            {
                long remainingTicks = nextDueTick - Stopwatch.GetTimestamp();
                if (remainingTicks <= 0) { if (-remainingTicks > cadenceTicks) { nextDueTick = Stopwatch.GetTimestamp(); reschedules++; } break; }
                Thread.SpinWait(256);
            }
        }
        double elapsedSeconds = wall.Elapsed.TotalSeconds;
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(30));
        if (!close.CompleteOutput) throw new Exception("Soak close failed: " + close.Error);
        intervals.Sort(); enqueueTimes.Sort();
        double Percentile(List<double> values, double p) => values.Count == 0 ? 0 : values[(int)Math.Min(values.Count - 1, Math.Ceiling(values.Count * p) - 1)];
        string evidence = JsonSerializer.Serialize(new {
            requestedSeconds = seconds, elapsedSeconds, attemptedSamples = sequence - 1, effectiveRateHz = (sequence - 1) / elapsedSeconds,
            droppedSamples = writer.DroppedSampleCount, reschedules,
            intervalP50Milliseconds = Percentile(intervals, .5), intervalP95Milliseconds = Percentile(intervals, .95), intervalMaxMilliseconds = Percentile(intervals, 1),
            enqueueP95Milliseconds = Percentile(enqueueTimes, .95), enqueueMaxMilliseconds = Percentile(enqueueTimes, 1),
            sourceRevision = revision, sourceHashes, executableSha256 = Sha256(File.ReadAllBytes(typeof(LoggingChecks).Assembly.Location)),
            configuration = configJson, fixture = fixtureJson, complete = close.CompleteOutput,
            limitation = "Synthetic console producer on development PC; no Unity display load, tracking hardware, physical or study acceptance"
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, writer.Lease.RunId + "-soak-evidence.json"), evidence, new UTF8Encoding(false));
        Console.WriteLine("SOAK: seconds=" + seconds + " attemptedSamples=" + (sequence - 1) + " droppedSamples=" + writer.DroppedSampleCount + " run=" + writer.Lease.DirectoryPath);
        passed++;
    }

    private static TrackingSamplePair Pair(long sequence)
    {
        var clock = new ManualSharedClock(DateTimeOffset.UtcNow); clock.AdvanceTicks(sequence);
        TrackingAcquisitionStamp stamp = TrackingAcquisitionStamp.Capture(clock, sequence);
        return new TrackingSamplePair(TrackingSample.Valid(stamp, new TrackerId("H"), new RigidPose(Vector3d.Zero, Quaterniond.Identity)), TrackingSample.Valid(stamp, new TrackerId("W"), new RigidPose(Vector3d.Zero, Quaterniond.Identity)));
    }
    private static SessionProvenance Provenance(ISharedClock clock) => new SessionProvenance("dev04-unit-fixture", "unit-fixture", Hash, Hash, Hash, true, clock.UtcStartupAnchor);
    private static string Sha256(byte[] bytes) { using SHA256 sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant(); }
    private static string CreateRoot() { string path = Path.Combine(Path.GetTempPath(), "elts-logging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void DeleteRoot(string path) { try { Directory.Delete(path, true); } catch { } }
    private static int ParseSoakSeconds(string[] args) { for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--soak-seconds" && Int32.TryParse(args[i + 1], out int value) && value > 0) return value; return 0; }
    private static string? ParseOutput(string[] args) { for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "--output") return args[i + 1]; return null; }

    private sealed class DelegatingSinkFactory : ILogSinkFactory { private readonly Func<ILogSink> create; public DelegatingSinkFactory(Func<ILogSink> create) { this.create = create; } public ILogSink Open(LoggingRunLease lease) => create(); }
    private class MemorySink : ILogSink
    {
        protected readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public virtual void Write(string fileName, string utf8Json) { Values[fileName] = utf8Json; }
        public virtual void Flush(bool durable) { }
        public IReadOnlyDictionary<string, string> FileChecksums { get; } = new Dictionary<string, string>();
        public virtual void Dispose() { }
    }
    private sealed class SleepingSink : MemorySink { private readonly int milliseconds; public SleepingSink(int milliseconds) { this.milliseconds = milliseconds; } public override void Write(string fileName, string utf8Json) { Thread.Sleep(milliseconds); base.Write(fileName, utf8Json); } }
    private sealed class ThrowingSink : MemorySink { public override void Write(string fileName, string utf8Json) { throw new IOException("injected sink write failure"); } }
    private sealed class GateSink : MemorySink
    {
        private readonly ManualResetEventSlim entered; private readonly ManualResetEventSlim release;
        public GateSink(ManualResetEventSlim entered, ManualResetEventSlim release) { this.entered = entered; this.release = release; }
        public override void Write(string fileName, string utf8Json) { entered.Set(); release.Wait(); base.Write(fileName, utf8Json); }
        public override void Flush(bool durable) { entered.Set(); release.Wait(); }
    }
}
