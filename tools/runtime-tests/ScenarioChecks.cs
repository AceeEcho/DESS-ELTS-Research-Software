#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

static class ScenarioChecks
{
    static int passed;
    static void True(bool yes, string name) { if (!yes) throw new Exception("FAIL: " + name); passed++; }
    static void Throws<T>(Action action, string name) where T : Exception
    { try { action(); throw new Exception("FAIL: " + name); } catch (T) { passed++; } }

    static void Main()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var start = clock.Now;
        var volume = new SpawnVolume(new Vector3d(-1, -1, 4), new Vector3d(1, 1, 6));
        var fixedDefinition = new ScenarioDefinition("synthetic-fixed", .1, volume, 2, 5, 0, "Fixed", "Hitscan", "TargetsDestroyedCount");

        var first = new List<TargetEntity>(new MaintainCountSpawnRule(44).Spawn(fixedDefinition, start, Vector3d.Zero, _ => true));
        var repeat = new List<TargetEntity>(new MaintainCountSpawnRule(44).Spawn(fixedDefinition, start, Vector3d.Zero, _ => true));
        True(first.Count == 2 && first[0].State == TargetState.Active && volume.Contains(first[0].Position), "maintain count spawns visible world targets");
        True(first[0].Position.Equals(repeat[0].Position) && first[1].Position.Equals(repeat[1].Position), "seeded spawns are repeatable");
        True(fixedDefinition.BlockSeed("p-01", "WE_FT") == fixedDefinition.BlockSeed("p-01", "WE_FT"), "block seed is repeatable and nonnegative");

        var fixedMove = ScenarioMovementFactory.Create("Fixed", 9, 1, 1, .1);
        var original = first[0].Position;
        fixedMove.Step(first[0], 1, volume);
        True(first[0].Position.Equals(original), "FT resolves to a fixed target behavior");

        var movingA = ActiveTarget("moving-a", start);
        var movingB = ActiveTarget("moving-b", start);
        var movementA = ScenarioMovementFactory.Create("RandomWalk", 9, 1, 1, .1);
        var movementB = ScenarioMovementFactory.Create("RandomWalk", 9, 1, 1, .1);
        movementA.Step(movingA, .2, new SpawnVolume(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)));
        movementB.Step(movingB, .2, new SpawnVolume(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)));
        True(movingA.Position.Length > 0 && movingA.Position.Equals(movingB.Position), "MT random-walk placeholder is seeded and repeatable");
        Throws<ArgumentException>(() => ScenarioMovementFactory.Create("Unknown", 9, 1, 1, .1), "unknown movement is rejected rather than silently selected");

        var near = ActiveTarget("near", start, 5);
        var far = ActiveTarget("far", start, 8);
        var model = new HitscanShotModel(TimeSpan.FromMilliseconds(20));
        var hitEvents = model.Fire(new ShotContext(start, ForwardRay(), true), "block-1", new[] { far, near }, _ => true);
        True(near.State == TargetState.Destroyed && far.State == TargetState.Active && hitEvents.Count == 3, "trigger-edge hitscan selects the nearest active target");
        True(TextField(hitEvents[2], "blockId") == "block-1" && TextField(hitEvents[2], "targetId") == "near", "TargetDestroyed has scalar blockId and targetId payloads");
        True(model.Fire(new ShotContext(start, ForwardRay(), true, false), "block-1", new[] { far }, _ => true).Count == 0, "non-falling trigger observations are ignored");
        clock.Advance(TimeSpan.FromMilliseconds(19));
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), true), "block-1", new[] { far }, _ => true).Count == 0, "configured trigger lockout is honored");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), true), "block-1", new[] { far }, _ => false).Count == 1, "known miss emits ShotFired without target credit");
        True(model.Fire(new ShotContext(clock.Now, ForwardRay(), false), "block-1", new[] { far }, _ => true).Count == 0, "invalid tracking contributes no shot events");

        var scoringClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var block = new ScenarioBlockController(scoringClock, "block-1", fixedDefinition.BlockSeed("p-01", "WE_FT"));
        var blockEvents = new List<SessionEvent>(block.Start());
        var scoredTarget = ActiveTarget("scored", scoringClock.Now, 5);
        blockEvents.AddRange(new HitscanShotModel().Fire(new ShotContext(scoringClock.Now, ForwardRay(), true), "block-1", new[] { scoredTarget }, _ => true));
        scoringClock.Advance(TimeSpan.FromSeconds(300));
        blockEvents.AddRange(block.Update());
        var scorer = new TargetsDestroyedCount();
        True(block.State == ScenarioBlockState.Completed && blockEvents[0].Timestamp.Ticks == 0 && blockEvents[blockEvents.Count - 1].Timestamp.Elapsed == TimeSpan.FromSeconds(300), "completed block uses shared monotonic exact 300-second bounds");
        True(TextField(blockEvents[0], "blockId") == "block-1" && TextField(blockEvents[blockEvents.Count - 1], "blockId") == "block-1", "block boundary events carry blockId");
        True(scorer.ScoreCompletedPrimaryBlock("block-1", blockEvents) == 1, "primary score derives only from in-window TargetDestroyed events");
        var atEnd = new SessionEvent(90, scoringClock.Now, "TargetDestroyed", new[] { LogField.String("blockId", "block-1"), LogField.String("targetId", "at-end") });
        blockEvents.Add(atEnd);
        True(scorer.ScoreCompletedPrimaryBlock("block-1", blockEvents) == 1, "half-open block end excludes TargetDestroyed at 300 seconds");
        var duplicate = new List<SessionEvent>(blockEvents) { blockEvents[3] };
        Throws<ArgumentException>(() => scorer.ScoreCompletedPrimaryBlock("block-1", duplicate), "duplicate target destruction is rejected");

        var abortedClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var aborted = new ScenarioBlockController(abortedClock, "aborted", 2);
        var abortedEvents = new List<SessionEvent>(aborted.Start());
        abortedClock.Advance(TimeSpan.FromSeconds(12));
        abortedEvents.AddRange(aborted.Abort("operator-stop"));
        Throws<ArgumentException>(() => scorer.ScoreCompletedPrimaryBlock("aborted", abortedEvents), "early aborted block cannot produce a completed primary DV");

        var lifecycleClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var lifecycleBlock = new ScenarioBlockController(lifecycleClock, "lifecycle-only", 3);
        var lifecycleEvents = new List<SessionEvent>(lifecycleBlock.Start());
        var destroyedButUnlogged = ActiveTarget("lifecycle-target", lifecycleClock.Now, 5);
        destroyedButUnlogged.Destroy();
        lifecycleClock.Advance(TimeSpan.FromSeconds(300));
        lifecycleEvents.AddRange(lifecycleBlock.Update());
        True(scorer.ScoreCompletedPrimaryBlock("lifecycle-only", lifecycleEvents) == 0, "TargetState.Destroyed alone is not a primary-score event");

        var plan = new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" });
        True(plan.ConditionDurationSeconds == 300 && !plan.OverrideDesignDuration, "session preserves the fixed design duration");
        Throws<ArgumentException>(() => new SessionPlan("p-01", new[] { "WE_MT", "WE_MT", "NE_FT", "NE_MT" }), "duplicate condition order is rejected");
        Throws<ArgumentException>(() => new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 299), "duration override must be explicit");
        True(new SessionPlan("p-01", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 10, true).OverrideDesignDuration, "development duration override is explicit and auditable");
        Throws<ArgumentException>(() => new ScenarioBlockController(clock, "test", 1, 10), "block duration override must be explicit");
        Console.WriteLine("PASS: " + passed + " scenario checks");
    }

    static TargetEntity ActiveTarget(string id, MonotonicTimestamp timestamp, double z = 0)
    { var target = new TargetEntity(id, new Vector3d(0, 0, z), Vector3d.Zero, 1, timestamp); target.Activate(); return target; }
    static Ray3d ForwardRay() => new Ray3d(Vector3d.Zero, new Vector3d(0, 0, 1));
    static string? TextField(SessionEvent item, string name)
    { foreach (var field in item.Fields) if (field.Name == name) return field.Text; return null; }
}
