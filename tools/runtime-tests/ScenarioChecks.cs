#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using Elts.Tracking;

static class ScenarioChecks
{
    static int passed;
    static void True(bool yes, string name) { if (!yes) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); throw new Exception("FAIL: " + name); } catch (T) { passed++; } }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "--output") { WriteInteropFixture(args[1]); return 0; }
            if (args.Length != 0) throw new ArgumentException("Usage: ScenarioChecks [--output DIRECTORY]");
            RunChecks();
            Console.WriteLine("PASS: " + passed + " scenario checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void RunChecks()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var start = clock.Now;
        var volume = new SpawnVolume(new Vector3d(-1, -1, 4), new Vector3d(1, 1, 6));
        var fixedDefinition = new ScenarioDefinition("synthetic-fixed", .1, volume, 2, 30, 5, "Fixed", "Hitscan", "TargetsDestroyedCount");

        var population = new ScenarioPopulation(fixedDefinition, new MaintainCountSpawnRule(44));
        var first = new List<TargetEntity>(population.Reconcile(start, _ => true));
        var repeat = new List<TargetEntity>(population.Reconcile(start, _ => true));
        True(first.Count == 2 && first[0].State == TargetState.Active && volume.Contains(first[0].Position), "maintain count spawns visible world targets");
        True(repeat.Count == 2 && Object.ReferenceEquals(first[0], repeat[0]), "reconcile preserves active population instead of spawning N repeatedly");
        var populationRepeat = new ScenarioPopulation(fixedDefinition, new MaintainCountSpawnRule(44));
        var repeatedFixture = populationRepeat.Reconcile(start, _ => true);
        True(first[0].Position.Equals(repeatedFixture[0].Position) && first[1].Position.Equals(repeatedFixture[1].Position), "seeded initial spawns are repeatable");
        first[0].Destroy();
        True(population.Reconcile(start, _ => true).Count == 1, "destroyed target respects respawn delay");
        clock.Advance(TimeSpan.FromSeconds(5));
        True(population.Reconcile(clock.Now, _ => true).Count == 2 && population.History.Count == 3, "population restores maintain count after configured delay");
        True(fixedDefinition.BlockSeed("p-01", "WE_FT") == fixedDefinition.BlockSeed("p-01", "WE_FT"), "block seed is repeatable and nonnegative");

        var fixedMove = ScenarioMovementFactory.Create("Fixed", 9, 1, 1, .1);
        var original = first[0].Position;
        fixedMove.Step(first[0], 1, volume);
        True(first[0].Position.Equals(original), "FT resolves to a fixed target behavior");

        var movingA = ActiveTarget("moving", start);
        var movingB = ActiveTarget("moving", start);
        var movementA = ScenarioMovementFactory.Create("RandomWalk", 9, 1, 1, .1);
        var movementB = ScenarioMovementFactory.Create("RandomWalk", 9, 1, 1, .1);
        movementA.Step(movingA, .2, new SpawnVolume(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)));
        movementB.Step(movingB, .2, new SpawnVolume(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)));
        True(movingA.Position.Length > 0 && movingA.Position.Equals(movingB.Position), "MT random-walk placeholder is seeded and repeatable");
        var orderOneA = ActiveTarget("order-a", start); var orderOneB = ActiveTarget("order-b", start);
        var orderTwoA = ActiveTarget("order-a", start); var orderTwoB = ActiveTarget("order-b", start);
        var orderOne = new RandomWalkMovement(18, 1, 1, .1); var orderTwo = new RandomWalkMovement(18, 1, 1, .1);
        var walkBounds = new SpawnVolume(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2));
        orderOne.Step(orderOneA, .2, walkBounds); orderOne.Step(orderOneB, .2, walkBounds);
        orderTwo.Step(orderTwoB, .2, walkBounds); orderTwo.Step(orderTwoA, .2, walkBounds);
        True(orderOneA.Position.Equals(orderTwoA.Position) && orderOneB.Position.Equals(orderTwoB.Position), "MT trajectories do not depend on target update order");
        Throws<ArgumentException>(() => ScenarioMovementFactory.Create("Unknown", 9, 1, 1, .1), "unknown movement is rejected rather than silently selected");
        Throws<ArgumentException>(() => new ScenarioDefinition("bad", double.NaN, volume, 1, 1, 0, "Fixed", "Hitscan", "TargetsDestroyedCount"), "NaN scenario values are rejected");
        Throws<ArgumentException>(() => new ScenarioDefinition("bad", .1, default, 1, 1, 0, "Fixed", "Hitscan", "TargetsDestroyedCount"), "default spawn volume is rejected");
        Throws<ArgumentOutOfRangeException>(() => new FixedMovement().Step(first[1], double.PositiveInfinity, volume), "nonfinite motion delta is rejected");

        var near = ActiveTarget("near", start, 5);
        var far = ActiveTarget("far", start, 8);
        var model = new HitscanShotModel(new SessionEventSequence(), TimeSpan.FromMilliseconds(20));
        var hitEvents = model.Fire(new ShotContext(clock.Now, ForwardRay(), true), "block-1", new[] { far, near }, _ => true);
        True(near.State == TargetState.Destroyed && far.State == TargetState.Active && hitEvents.Count == 3, "trigger-edge hitscan selects the nearest active target");
        True(TextField(hitEvents[2], "blockId") == "block-1" && TextField(hitEvents[2], "targetId") == "near", "TargetDestroyed has scalar blockId and targetId payloads");
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), true, false), "block-1", new[] { far }, _ => true).Count == 0, "non-falling trigger observations are ignored");
        clock.Advance(TimeSpan.FromMilliseconds(19));
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), true), "block-1", new[] { far }, _ => true).Count == 0, "configured trigger lockout is honored");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), true), "block-1", new[] { far }, _ => false).Count == 1, "known miss emits ShotFired without target credit");
        Throws<ArgumentException>(() => model.Fire(new ShotContext(start, ForwardRay(), true), "block-1", new[] { far }, _ => true), "shot timestamps cannot move backward in a shared log");
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), false), "block-1", new[] { far }, _ => true).Count == 0, "invalid tracking contributes no shot events");

        var scoringClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var logSequence = new SessionEventSequence();
        var block = new ScenarioBlockController(scoringClock, logSequence, "block-1", fixedDefinition.BlockSeed("p-01", "WE_FT"));
        var blockEvents = new List<SessionEvent>(block.Start());
        var scoredTarget = ActiveTarget("scored", scoringClock.Now, 5);
        blockEvents.AddRange(block.Fire(new HitscanShotModel(logSequence), new ShotContext(scoringClock.Now, ForwardRay(), true), new[] { scoredTarget }, _ => true));
        scoringClock.Advance(TimeSpan.FromSeconds(300));
        True(block.Fire(new HitscanShotModel(logSequence), new ShotContext(scoringClock.Now, ForwardRay(), true), new[] { ActiveTarget("late", scoringClock.Now, 5) }, _ => true).Count == 0, "block controller refuses shots at or after the deadline");
        blockEvents.AddRange(block.Update());
        var scorer = new TargetsDestroyedCount();
        True(block.State == ScenarioBlockState.Completed && blockEvents[0].Timestamp.Ticks == 0 && blockEvents[blockEvents.Count - 1].Timestamp.Elapsed == TimeSpan.FromSeconds(300), "completed block uses shared monotonic exact 300-second bounds");
        True(TextField(blockEvents[0], "blockId") == "block-1" && TextField(blockEvents[blockEvents.Count - 1], "blockId") == "block-1", "block boundary events carry blockId");
        True(IsStrictSequence(blockEvents), "block and hitscan events share one strict session sequence");
        True(scorer.ScoreCompletedPrimaryBlock("block-1", blockEvents) == 1, "primary score derives only from in-window TargetDestroyed events");
        var atEnd = new SessionEvent(90, scoringClock.Now, "TargetDestroyed", new[] { LogField.String("blockId", "block-1"), LogField.String("targetId", "at-end") });
        blockEvents.Add(atEnd);
        True(scorer.ScoreCompletedPrimaryBlock("block-1", blockEvents) == 1, "half-open block end excludes TargetDestroyed at 300 seconds");
        var duplicate = new List<SessionEvent>(blockEvents) { blockEvents[3] };
        Throws<ArgumentException>(() => scorer.ScoreCompletedPrimaryBlock("block-1", duplicate), "duplicate target destruction is rejected");

        var abortedClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var aborted = new ScenarioBlockController(abortedClock, new SessionEventSequence(), "aborted", 2);
        var abortedEvents = new List<SessionEvent>(aborted.Start());
        abortedClock.Advance(TimeSpan.FromSeconds(12));
        abortedEvents.AddRange(aborted.Abort("operator-stop"));
        Throws<ArgumentException>(() => scorer.ScoreCompletedPrimaryBlock("aborted", abortedEvents), "early aborted block cannot produce a completed primary DV");

        var lifecycleClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var lifecycleBlock = new ScenarioBlockController(lifecycleClock, new SessionEventSequence(), "lifecycle-only", 3);
        var lifecycleEvents = new List<SessionEvent>(lifecycleBlock.Start());
        var destroyedButUnlogged = ActiveTarget("lifecycle-target", lifecycleClock.Now, 5);
        destroyedButUnlogged.Destroy();
        lifecycleClock.Advance(TimeSpan.FromSeconds(300));
        lifecycleEvents.AddRange(lifecycleBlock.Update());
        True(scorer.ScoreCompletedPrimaryBlock("lifecycle-only", lifecycleEvents) == 0, "TargetState.Destroyed alone is not a primary-score event");

        var plan = new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" });
        True(plan.ConditionDurationSeconds == 300 && !plan.AllowsDevelopmentOnlyDurationOverride, "session preserves the fixed design duration");
        Throws<ArgumentException>(() => new SessionPlan("p-01", new[] { "WE_MT", "WE_MT", "NE_FT", "NE_MT" }), "duplicate condition order is rejected");
        Throws<ArgumentException>(() => new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 299), "duration override must be explicit");
        True(new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 10, true).AllowsDevelopmentOnlyDurationOverride, "development duration override is explicit and auditable");
        Throws<ArgumentException>(() => new ScenarioBlockController(clock, new SessionEventSequence(), "test", 1, 10), "block duration override must be explicit");
    }

    /// <summary>
    /// Writes one compact completed synthetic block for the Python/analysis
    /// consumers. The ManualSharedClock advances logical experiment time; it
    /// does not wait for 300 seconds of wall-clock time.
    /// </summary>
    static void WriteInteropFixture(string outputDirectory)
    {
        if (String.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        string root = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(root);
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        string repository = FindRepositoryRoot();
        string scenarioSource = Path.Combine(repository, "unity", "Assets", "ELTS", "Scenario", "ScenarioRuntime.cs");
        string sessionSource = Path.Combine(repository, "unity", "Assets", "ELTS", "Session", "SessionRuntime.cs");
        string developmentScenario = Path.Combine(repository, "config", "development", "scenario.json");
        string sourceHash = HashText(HashFile(scenarioSource) + HashFile(sessionSource));
        string fixtureHash = HashText("scenario-checks-interop-v1|one-valid-pair|one-target|one-hitscan|manual-300s");
        string revision = GitRevision(repository);
        var provenance = new SessionProvenance("scenario-checks-interop-v1", revision, HashFile(developmentScenario), sourceHash, fixtureHash, true, clock.UtcStartupAnchor);
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "scenario-interop-300s");
        using var writer = new SessionLogWriter(lease, provenance);
        writer.Start();

        var head = new TrackerId("SYNTHETIC-HEAD-001");
        var weapon = new TrackerId("SYNTHETIC-WEAPON-001");
        var stamp = TrackingAcquisitionStamp.Capture(clock, 1);
        var pair = new TrackingSamplePair(TrackingSample.Valid(stamp, head, new RigidPose(Vector3d.Zero, Quaterniond.Identity)), TrackingSample.Valid(stamp, weapon, new RigidPose(Vector3d.Zero, Quaterniond.Identity)));
        Require(writer.TryLogSample(pair), "fixture paired sample");

        var sequence = new SessionEventSequence();
        var block = new ScenarioBlockController(clock, sequence, "WE_FT", 1729);
        var target = ActiveTarget("target-1", clock.Now, 5);
        Require(writer.TryLogTarget(new TargetSnapshot(1, clock.Now, target.Id, "WE_FT", TargetLifecycle.Spawned, target.Position, target.Velocity, 1729, "scenario-checks-interop-v1")), "fixture spawned target");
        var events = new List<SessionEvent>(block.Start());

        clock.Advance(TimeSpan.FromSeconds(1));
        events.AddRange(block.Fire(new HitscanShotModel(sequence), new ShotContext(clock.Now, ForwardRay(), true), new[] { target }, _ => true));
        True(target.State == TargetState.Destroyed, "fixture hitscan destroys target");
        Require(writer.TryLogTarget(new TargetSnapshot(2, clock.Now, target.Id, "WE_FT", TargetLifecycle.Destroyed, target.Position, target.Velocity, 1729, "scenario-checks-interop-v1")), "fixture destroyed target");

        clock.Advance(TimeSpan.FromSeconds(299));
        events.AddRange(block.Update());
        if (block.State != ScenarioBlockState.Completed || events[events.Count - 1].Timestamp.Elapsed != TimeSpan.FromSeconds(300) || !IsStrictSequence(events))
            throw new Exception("Fixture did not produce a strict completed 300-second block.");
        foreach (SessionEvent item in events) Require(writer.TryLogEvent(item), "fixture event " + item.EventType);
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(10));
        if (!close.Completed || !close.CompleteOutput) throw new Exception("Fixture log close failed: " + close.Error);

        int samples = CountLines(Path.Combine(lease.DirectoryPath, "samples.ndjson"));
        int loggedEvents = CountLines(Path.Combine(lease.DirectoryPath, "events.ndjson"));
        int targets = CountLines(Path.Combine(lease.DirectoryPath, "targets.ndjson"));
        if (samples != 1 || loggedEvents != 5 || targets != 2) throw new Exception("Fixture output counts were unexpected.");
        Console.WriteLine("FIXTURE: directory=" + lease.DirectoryPath + " samples=" + samples + " events=" + loggedEvents + " targets=" + targets + " complete=true manualClockSeconds=300");
    }

    static TargetEntity ActiveTarget(string id, MonotonicTimestamp timestamp, double z = 0)
    { var target = new TargetEntity(id, new Vector3d(0, 0, z), Vector3d.Zero, 1, timestamp); target.Activate(); return target; }
    static Ray3d ForwardRay() => new Ray3d(Vector3d.Zero, new Vector3d(0, 0, 1));
    static string? TextField(SessionEvent item, string name)
    { foreach (var field in item.Fields) if (field.Name == name) return field.Text; return null; }
    static bool IsStrictSequence(IReadOnlyList<SessionEvent> events)
    { for (int i = 1; i < events.Count; i++) if (events[i].Sequence != events[i - 1].Sequence + 1) return false; return true; }
    static void Require(bool accepted, string name) { if (!accepted) throw new Exception("Unable to enqueue " + name + "."); }
    static int CountLines(string path) => File.ReadAllLines(path, Encoding.UTF8).Length;
    static string HashFile(string path) { if (!File.Exists(path)) throw new FileNotFoundException("Required source file is missing.", path); return HashBytes(File.ReadAllBytes(path)); }
    static string HashText(string value) => HashBytes(Encoding.UTF8.GetBytes(value));
    static string HashBytes(byte[] bytes) { using SHA256 hash = SHA256.Create(); return Convert.ToHexString(hash.ComputeHash(bytes)).ToLowerInvariant(); }
    static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null) { if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName; current = current.Parent; }
        throw new DirectoryNotFoundException("Unable to locate the repository root for fixture provenance.");
    }
    static string GitRevision(string repository)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = repository, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.Start(); string output = process.StandardOutput.ReadToEnd().Trim(); string error = process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0 || output.Length != 40) throw new InvalidOperationException("Unable to obtain the current Git revision: " + error);
        return output;
    }
}
