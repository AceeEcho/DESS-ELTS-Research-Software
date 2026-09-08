#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elts.Clock;
using Elts.EltsLink;
using Elts.Geometry;
using Elts.Logging;
using Elts.Rendering;
using Elts.Scenario;
using Elts.Session;
using Elts.Tracking;

/// <summary>
/// DEV12's bounded, synthetic end-to-end recording fixture. It uses the actual
/// session engine, recording writer, session-to-ELTS adapter, and replay reader;
/// the link underneath is MockEltsLink and therefore carries no physical claim.
/// </summary>
static class DevelopmentInteropFixture
{
    private const double BlockSeconds = 300.0;
    private const int ConditionCount = 4;
    private const string ScenarioVersion = "dev12-synthetic-v1";

    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--output") throw new ArgumentException("Usage: DevelopmentInteropFixture --output DIRECTORY");
            WriteFixture(args[1]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void WriteFixture(string output)
    {
        string root = Path.GetFullPath(output);
        Directory.CreateDirectory(root);
        string repo = FindRepositoryRoot();
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        string scenarioPath = Path.Combine(repo, "config", "development", "scenario.json");
        string sourceHash = HashSources(repo,
            "unity/Assets/ELTS/Clock/SharedMonotonicClock.cs", "unity/Assets/ELTS/Elts/EltsLinkContracts.cs",
            "unity/Assets/ELTS/Elts/SimulatedEltsLink.cs", "unity/Assets/ELTS/Geometry/Geometry.cs",
            "unity/Assets/ELTS/Logging/LoggingContracts.cs", "unity/Assets/ELTS/Logging/LogJson.cs",
            "unity/Assets/ELTS/Logging/SessionLogWriter.cs", "unity/Assets/ELTS/Scenario/ScenarioRuntime.cs",
            "unity/Assets/ELTS/Session/SessionRuntime.cs", "unity/Assets/ELTS/Session/SessionRecordingAdapter.cs",
            "unity/Assets/ELTS/Session/SessionEltsLinkAdapter.cs", "unity/Assets/ELTS/Rendering/RecordedReplay.cs",
            "unity/Assets/ELTS/Tracking/TrackingContracts.cs", "tools/runtime-tests/DevelopmentInteropFixture.cs");
        // This fixture uses its own declared inputs, rather than pretending the
        // normal scenario template supplied the fixed geometry below.
        string fixtureConfiguration = JsonSerializer.Serialize(new {
            blockSeconds = BlockSeconds, conditionCount = ConditionCount, practiceSeconds = 0,
            breakSeconds = 0, headAndWeaponPositionMeters = new[] { 0, 0, 0 },
            targetPositionMeters = new[] { 0, 0, 5 }, targetRadiusMeters = 0.1,
            shotDelaySeconds = 0.001, triggerLockoutSeconds = 0.020,
            conditions = new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }
        });
        var provenance = new SessionProvenance("dev12-development-fixture", GitRevision(repo), HashText(fixtureConfiguration), HashFile(scenarioPath),
            HashText("dev12|real-session-engine|real-recording-adapter|real-session-elts-adapter|mock-link|four-300s-blocks|exact-shot-order"), true, clock.UtcStartupAnchor);

        string completed = RunCompleted(root, clock, provenance);
        var retry = RunAbortedAndReconnected(root, clock, provenance);
        RecordedReplay replay = RecordedReplay.Load(completed);
        Require(replay.FrameCount >= ConditionCount, "replay reader loaded completed samples");
        var initialFrame = replay.FrameAt(0.0);
        Require(initialFrame.Head.HasValue && initialFrame.Weapon.HasValue
            && initialFrame.Head.Value.Position.Length < 1e-12 && initialFrame.Weapon.Value.Position.Length < 1e-12,
            "replay preserves the known raw poses");
        Require(replay.TargetsAt(0.0).Count == 1, "replay starts with the spawned target");
        Require(replay.TargetsAt(0.001).Count == 0, "replay removes the destroyed target");

        var report = new
        {
            schema = "elts.dev12-development-fixture-report.v1",
            result = "pass",
            mode = "synthetic-development",
            studyReady = false,
            recordedUtc = DateTimeOffset.UtcNow,
            sourceRevision = provenance.SourceRevision,
            sourceHash,
            fixtureConfiguration = JsonDocument.Parse(fixtureConfiguration).RootElement,
            newtonsoftAssemblySha256 = HashFile(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location),
            configurationHash = provenance.ConfigurationHash,
            scenarioHash = provenance.ScenarioHash,
            fixtureHash = provenance.FixtureHash,
            completedRunDirectory = completed,
            abortedRunDirectory = retry.aborted,
            rerunRunDirectory = retry.rerun,
            completedEvents = CountLines(completed, "events.ndjson"),
            completedSamples = CountLines(completed, "samples.ndjson"),
            completedTargets = CountLines(completed, "targets.ndjson"),
            completedReplayFrames = replay.FrameCount,
            abortedEvents = CountLines(retry.aborted, "events.ndjson"),
            rerunEvents = CountLines(retry.rerun, "events.ndjson"),
            abortedSummaryHash = HashFile(Path.Combine(retry.aborted, "session-summary.json")),
            rerunSummaryHash = HashFile(Path.Combine(retry.rerun, "session-summary.json")),
            syntheticLimitations = "MockEltsLink and deterministic tracking are synthetic; no hardware, safety, or measured calibration gate is satisfied."
        };
        string reportPath = WriteReportOnce(root, report);
        Console.WriteLine("PASS: DEV12 development fixture; completed=" + completed + "; aborted=" + retry.aborted + "; rerun=" + retry.rerun + "; report=" + reportPath);
    }

    private static string RunCompleted(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("dev12-four-condition").GetAwaiter().GetResult(), "reserve completed run");
        var sequence = new SessionEventSequence();
        var controller = new SimulatedEltsController(clock);
        var engine = NewEngine(clock, adapter, sequence, new MockEltsLink(controller));
        Begin(engine);
        for (int i = 0; i < ConditionCount; i++)
        {
            Require(engine.State == SessionState.BlockReady, "block ready " + i);
            engine.StartBlock();
            RecordCanonicalActivity(engine, adapter, clock, sequence, i, true);
            clock.Advance(TimeSpan.FromMilliseconds((long)(BlockSeconds * 1000) - 1));
            engine.Tick();
            Require(engine.State == SessionState.BlockEnded, "exact 300-second completion " + i);
            engine.Advance();
            engine.Advance();
        }
        Require(engine.State == SessionState.SessionComplete && adapter.CloseAsync().GetAwaiter().GetResult(), "completed run publishes");
        ValidateRun(adapter.RunDirectory!, minimumEvents: 60, minimumSamples: ConditionCount, minimumTargets: ConditionCount * 2);
        return adapter.RunDirectory!;
    }

    private static (string aborted, string rerun) RunAbortedAndReconnected(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("dev12-abort-reconnect").GetAwaiter().GetResult(), "reserve abort run");
        var sequence = new SessionEventSequence();
        var controller = new SimulatedEltsController(clock);
        var link = new MockEltsLink(controller);
        var engine = NewEngine(clock, adapter, sequence, link);
        Begin(engine);
        engine.StartBlock();
        RecordCanonicalActivity(engine, adapter, clock, sequence, 100, false);
        // Abort while the synthetic heartbeat is still healthy so the abort
        // event, stop command, and closed evidence are all recorded explicitly.
        clock.Advance(TimeSpan.FromSeconds(1));
        engine.Abort("synthetic-fixture-abort-before-reconnect");
        Require(engine.State == SessionState.Aborted && engine.CanRerun && adapter.CloseAsync().GetAwaiter().GetResult(), "aborted run publishes before rerun");
        string aborted = adapter.RunDirectory!;
        ValidateRun(aborted, 12, 1, 2);
        string priorHash = HashFile(Path.Combine(aborted, "session-summary.json"));

        // The link epoch is explicitly reset. A new recording reservation and
        // RerunCurrentBlock are required; no closed output is silently resumed.
        link.SimulateReconnect();
        Require(adapter.ReserveAndStartAsync("dev12-abort-reconnect").GetAwaiter().GetResult(), "reserve distinct reconnect rerun");
        string rerun = adapter.RunDirectory!;
        Require(rerun != aborted && Path.GetFileName(rerun).Contains("-r", StringComparison.Ordinal), "reconnect rerun receives new directory");
        engine.RerunCurrentBlock();
        Require(engine.CurrentBlockId!.EndsWith("attempt-02", StringComparison.Ordinal), "explicit new attempt identity");
        engine.StartBlock();
        RecordCanonicalActivity(engine, adapter, clock, sequence, 101, false);
        clock.Advance(TimeSpan.FromMilliseconds((long)(BlockSeconds * 1000) - 1));
        engine.Tick();
        Require(engine.State == SessionState.BlockEnded && adapter.CloseAsync().GetAwaiter().GetResult(), "reconnect rerun publishes");
        Require(HashFile(Path.Combine(aborted, "session-summary.json")) == priorHash, "aborted evidence remains immutable");
        ValidateRun(rerun, 12, 1, 2);
        return (aborted, rerun);
    }

    private static void Begin(SessionEngine engine)
    {
        Require(engine.StartSetup(new SessionReadinessInput { TwoDisplays = true, SteamVrRunning = true, TrackersBoundAndValidForTwoSeconds = true,
            ConfigurationHashesComputed = true, DiskFreeBytes = 2L * 1024 * 1024 * 1024, LogWriterAlive = true, WeLinkReachable = true }).SetupAccepted, "setup accepted");
        engine.Advance(); engine.Advance(); engine.Tick();
    }

    private static void RecordCanonicalActivity(SessionEngine engine, SessionRecordingAdapter adapter, ManualSharedClock clock, SessionEventSequence sequence, int index, bool notes)
    {
        var head = new TrackerId("SYNTHETIC-HEAD-001");
        var weapon = new TrackerId("SYNTHETIC-WEAPON-001");
        var stamp = TrackingAcquisitionStamp.Capture(clock, index + 1);
        var pose = new RigidPose(Vector3d.Zero, Quaterniond.Identity);
        Require(adapter.TryLogSample(new TrackingSamplePair(TrackingSample.Valid(stamp, head, pose), TrackingSample.Valid(stamp, weapon, pose))), "paired sample " + index);
        var target = new TargetEntity("target-dev12-" + index.ToString("D3"), new Vector3d(0, 0, 5), Vector3d.Zero, .1, clock.Now);
        target.Activate();
        string blockId = engine.CurrentBlockId!;
        long targetSequence = index * 2L + 1;
        Require(adapter.TryLogTarget(new TargetSnapshot(targetSequence, clock.Now, target.Id, blockId, TargetLifecycle.Spawned, target.Position, target.Velocity, index, ScenarioVersion)), "spawn target " + index);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        var shot = new ShotContext(clock.Now, new Ray3d(Vector3d.Zero, new Vector3d(0, 0, 1)), true);
        var shotModel = new HitscanShotModel(sequence, TimeSpan.FromMilliseconds(20));
        Require(engine.Fire(shotModel, shot, new[] { target }, _ => true), "shot recorded " + index);
        Require(!engine.Fire(shotModel, shot, new[] { target }, _ => true), "lockout rejects duplicate " + index);
        Require(target.State == TargetState.Destroyed, "target destroyed " + index);
        Require(adapter.TryLogTarget(new TargetSnapshot(targetSequence + 1, clock.Now, target.Id, blockId, TargetLifecycle.Destroyed, target.Position, target.Velocity, index, ScenarioVersion)), "destroy target " + index);
        if (notes) { engine.AddNote("DEV12 synthetic shot note"); engine.EmitProvisionalSyncMarker("DEV12 synthetic marker"); }
    }

    private static SessionEngine NewEngine(ManualSharedClock clock, SessionRecordingAdapter adapter, SessionEventSequence sequence, IEltsLink link) =>
        new SessionEngine(clock, sequence, new SessionPlan("dev12-participant", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }),
            new SessionTiming(0, 0, true), adapter, new SessionEltsLinkAdapter(clock, sequence, adapter, link));

    private static void ValidateRun(string directory, int minimumEvents, int minimumSamples, int minimumTargets)
    {
        Require(File.Exists(Path.Combine(directory, "session-summary.json")), "published summary");
        Require(CountLines(directory, "events.ndjson") >= minimumEvents && CountLines(directory, "samples.ndjson") >= minimumSamples && CountLines(directory, "targets.ndjson") >= minimumTargets, "canonical outputs");
    }
    private static int CountLines(string directory, string name) => File.ReadAllLines(Path.Combine(directory, name)).Length;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Fixture failed: " + message); }
    private static string WriteReportOnce(string root, object report)
    {
        for (int i = 0; ; i++)
        {
            string name = i == 0 ? "fixture-report.json" : "fixture-report-r" + i.ToString("D4") + ".json";
            string path = Path.Combine(root, name);
            try
            {
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
                return path;
            }
            catch (IOException) when (File.Exists(path)) { /* Reserve a new report name. */ }
        }
    }
    private static string HashSources(string repo, params string[] paths) { var text = new StringBuilder(); foreach (var path in paths) text.Append(HashFile(Path.Combine(repo, path))); return HashText(text.ToString()); }
    private static string HashFile(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant(); }
    private static string HashText(string text) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(); }
    private static string FindRepositoryRoot() { var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); while (dir != null) { if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName; dir = dir.Parent; } throw new DirectoryNotFoundException("Repository root was not found."); }
    private static string GitRevision(string root) { using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false }); if (process == null) throw new InvalidOperationException("Cannot read git revision."); string text = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Cannot read git revision."); return text; }
}
