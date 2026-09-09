#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using Elts.Tracking;

static class SessionInteropFixture
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--output") throw new ArgumentException("Usage: SessionInteropFixture --output DIRECTORY");
            WriteFixture(args[1]); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void WriteFixture(string output)
    {
        string root = Path.GetFullPath(output); Directory.CreateDirectory(root);
        string repo = FindRepositoryRoot(); var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        string source = HashSources(repo, "unity/Assets/ELTS/Session/SessionRuntime.cs", "unity/Assets/ELTS/Session/SessionRecordingAdapter.cs",
            "unity/Assets/ELTS/Scenario/ScenarioRuntime.cs", "unity/Assets/ELTS/Logging/LoggingContracts.cs", "unity/Assets/ELTS/Logging/LogJson.cs",
            "unity/Assets/ELTS/Logging/SessionLogWriter.cs", "unity/Assets/ELTS/Tracking/TrackingContracts.cs", "tools/runtime-tests/SessionInteropFixture.cs");
        var provenance = new SessionProvenance("session-interop-v2", GitRevision(repo), HashFile(Path.Combine(repo, "config", "development", "scenario.json")), source,
            HashText("session-interop-v2|manual-clock|four-300s-blocks|sample-target-shot-note-marker|abort-close-reserve-rerun"), true, clock.UtcStartupAnchor);
        string completed = RunCompleted(root, clock, provenance);
        var retry = RunAbortedAndRerun(root, clock, provenance);
        var report = new { schema = "elts.session-interop-report.v2", recordedUtc = DateTimeOffset.UtcNow, clockAnchorUtc = clock.UtcStartupAnchor,
            sourceRevision = provenance.SourceRevision, configurationHash = provenance.ConfigurationHash, scenarioHash = provenance.ScenarioHash, fixtureHash = provenance.FixtureHash,
            completedRunDirectory = completed, abortedRunDirectory = retry.aborted, rerunRunDirectory = retry.rerun,
            completedEvents = CountLines(completed, "events.ndjson"), completedSamples = CountLines(completed, "samples.ndjson"), completedTargets = CountLines(completed, "targets.ndjson"),
            abortedEvents = CountLines(retry.aborted, "events.ndjson"), rerunEvents = CountLines(retry.rerun, "events.ndjson"),
            abortedSummaryHash = HashFile(Path.Combine(retry.aborted, "session-summary.json")), rerunSummaryHash = HashFile(Path.Combine(retry.rerun, "session-summary.json")) };
        string reportPath = WriteReportOnce(root, report);
        Console.WriteLine("PASS: session interop fixture; completed=" + completed + "; aborted=" + retry.aborted + "; rerun=" + retry.rerun + "; report=" + reportPath);
    }

    static string RunCompleted(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("session-four-block-300s").GetAwaiter().GetResult(), "reserve completed run");
        var sequence = new SessionEventSequence(); var engine = NewEngine(clock, adapter, sequence);
        Require(engine.StartSetup(Ready()).SetupAccepted, "setup completed run"); engine.Advance(); engine.Advance(); engine.Tick();
        for (int i = 0; i < 4; i++)
        {
            Require(engine.State == SessionState.BlockReady, "block ready " + i); engine.StartBlock();
            RecordCanonicalBlockActivity(engine, adapter, clock, sequence, i, i == 0);
            clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick(); Require(engine.State == SessionState.BlockEnded, "normal 300-second completion " + i);
            engine.Advance(); engine.Advance();
        }
        Require(engine.State == SessionState.SessionComplete && adapter.CloseAsync().GetAwaiter().GetResult(), "completed run publishes");
        ValidateRun(adapter.RunDirectory!, 20, 4, 8); return adapter.RunDirectory!;
    }

    static (string aborted, string rerun) RunAbortedAndRerun(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("session-abort-rerun").GetAwaiter().GetResult(), "reserve abort run");
        var sequence = new SessionEventSequence(); var engine = NewEngine(clock, adapter, sequence);
        Require(engine.StartSetup(Ready()).SetupAccepted, "setup abort run"); engine.Advance(); engine.Advance(); engine.Tick(); engine.StartBlock();
        RecordCanonicalBlockActivity(engine, adapter, clock, sequence, 10, false); clock.Advance(TimeSpan.FromSeconds(12)); engine.Abort("synthetic-fixture-abort");
        Require(engine.State == SessionState.Aborted && engine.CanRerun && adapter.CloseAsync().GetAwaiter().GetResult(), "aborted run publishes before rerun");
        string aborted = adapter.RunDirectory!; ValidateRun(aborted, 6, 1, 2); string priorHash = HashFile(Path.Combine(aborted, "session-summary.json"));
        Require(adapter.ReserveAndStartAsync("session-abort-rerun").GetAwaiter().GetResult(), "reserve distinct rerun output"); string rerunPath = adapter.RunDirectory!;
        Require(rerunPath != aborted && Path.GetFileName(rerunPath).Contains("-r", StringComparison.Ordinal), "rerun reserve never overwrites aborted recording");
        engine.RerunCurrentBlock(); Require(engine.CurrentBlockId!.EndsWith("attempt-02", StringComparison.Ordinal), "engine rerun identity is retained after reopening recording");
        engine.StartBlock(); RecordCanonicalBlockActivity(engine, adapter, clock, sequence, 11, false); clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick();
        Require(engine.State == SessionState.BlockEnded && adapter.CloseAsync().GetAwaiter().GetResult(), "rerun output publishes");
        Require(HashFile(Path.Combine(aborted, "session-summary.json")) == priorHash, "closed aborted evidence remains unchanged"); ValidateRun(rerunPath, 6, 1, 2);
        return (aborted, rerunPath);
    }

    static void RecordCanonicalBlockActivity(SessionEngine engine, SessionRecordingAdapter adapter, ManualSharedClock clock, SessionEventSequence sequence, int number, bool includeOperatorAnnotations)
    {
        var head = new TrackerId("SYNTHETIC-HEAD-001"); var weapon = new TrackerId("SYNTHETIC-WEAPON-001");
        var stamp = TrackingAcquisitionStamp.Capture(clock, number + 1); var pose = new RigidPose(Vector3d.Zero, Quaterniond.Identity);
        Require(adapter.TryLogSample(new TrackingSamplePair(TrackingSample.Valid(stamp, head, pose), TrackingSample.Valid(stamp, weapon, pose))), "raw paired sample " + number);
        var target = new TargetEntity("target-" + number, new Vector3d(0, 0, 5), Vector3d.Zero, .1, clock.Now); target.Activate();
        string blockId = engine.CurrentBlockId!; long targetSequence = number * 2L + 1;
        Require(adapter.TryLogTarget(new TargetSnapshot(targetSequence, clock.Now, target.Id, blockId, TargetLifecycle.Spawned, target.Position, target.Velocity, number, "session-interop-v2")), "spawned target " + number);
        // Separate the observation from the removal so the fixture has an
        // unambiguous active target at the paired sample's logical timestamp.
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var shotModel = new HitscanShotModel(sequence, TimeSpan.FromMilliseconds(20));
        var shot = new ShotContext(clock.Now, new Ray3d(Vector3d.Zero, new Vector3d(0, 0, 1)), true);
        Require(engine.Fire(shotModel, shot, new[] { target }, _ => true), "shot accepted and recorded");
        Require(!engine.Fire(shotModel, shot, new[] { target }, _ => true), "lockout does not claim a recorded shot");
        Require(target.State == TargetState.Destroyed, "engine fire destroys canonical target " + number);
        Require(adapter.TryLogTarget(new TargetSnapshot(targetSequence + 1, clock.Now, target.Id, blockId, TargetLifecycle.Destroyed, target.Position, target.Velocity, number, "session-interop-v2")), "destroyed target " + number);
        if (includeOperatorAnnotations) { engine.AddNote("synthetic fixture annotation"); engine.EmitProvisionalSyncMarker("synthetic fixture marker"); }
    }

    static SessionEngine NewEngine(ManualSharedClock clock, SessionRecordingAdapter adapter, SessionEventSequence sequence) => new SessionEngine(clock, sequence,
        new SessionPlan("fixture-p1", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }), new SessionTiming(0, 0, true), adapter, new FixtureLink());
    static SessionReadinessInput Ready() => new SessionReadinessInput { TwoDisplays = true, SteamVrRunning = true, TrackersBoundAndValidForTwoSeconds = true, ConfigurationHashesComputed = true, DiskFreeBytes = 2L * 1024 * 1024 * 1024, LogWriterAlive = true, WeLinkReachable = true };
    static void ValidateRun(string directory, int minEvents, int minSamples, int minTargets) { Require(File.Exists(Path.Combine(directory, "session-summary.json")), "published summary"); Require(CountLines(directory, "events.ndjson") >= minEvents && CountLines(directory, "samples.ndjson") >= minSamples && CountLines(directory, "targets.ndjson") >= minTargets, "canonical recording products"); }
    static int CountLines(string directory, string name) => File.ReadAllLines(Path.Combine(directory, name)).Length;
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Fixture failed: " + message); }
    static string WriteReportOnce(string root, object report) { for (int i = 0; ; i++) { string name = i == 0 ? "fixture-report.json" : "fixture-report-r" + i.ToString("D4") + ".json"; try { using var output = new FileStream(Path.Combine(root, name), FileMode.CreateNew, FileAccess.Write, FileShare.None); JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true }); return output.Name; } catch (IOException) { } } }
    static string HashSources(string repo, params string[] paths) { var text = new StringBuilder(); foreach (var path in paths) text.Append(HashFile(Path.Combine(repo, path))); return HashText(text.ToString()); }
    static string HashFile(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant(); }
    static string HashText(string text) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(); }
    static string FindRepositoryRoot() { var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); while (dir != null) { if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName; dir = dir.Parent; } throw new DirectoryNotFoundException("Repository root was not found."); }
    static string GitRevision(string root) { using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false }); if (process == null) throw new InvalidOperationException("Cannot read git revision."); string text = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Cannot read git revision."); return text; }
    sealed class FixtureLink : ISessionLink { public bool TrySetEnabled(bool enabled) => true; }
}
