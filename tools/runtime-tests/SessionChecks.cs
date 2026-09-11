#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

static class SessionChecks
{
    private static int passed;

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        passed++;
    }

    static void Throws(Action action, string name)
    {
        try { action(); }
        catch (InvalidOperationException) { passed++; return; }
        throw new Exception("FAIL: " + name);
    }

    static void Main()
    {
        ExerciseNominalSessionAndAuditMetadata();
        ExerciseRerunIdentityAndClosedSessionRules();
        ExerciseSinkFailureFailsClosed();
        ExerciseLinkFailuresFailClosedWithoutInvalidTransition();
        ExerciseOperatorAbortAcrossOpenStates();
        ExerciseDeadlineFirstOperatorActions();
        ExerciseFlexibleDevelopmentTiming();
        ExercisePauseAndResumeFailures();
        ExerciseFlexibleControlsRequireDevelopment();
        ExercisePausedTriggerWindows();
        ExerciseAdministratorQueueAndBreaks();
        Console.WriteLine("PASS: " + passed + " session checks");
    }

    private static void ExerciseAdministratorQueueAndBreaks()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>();
        var engine = NewEngine(clock, new Sink(events), new Link());
        BeginFirstBlock(engine, clock);
        engine.RestartDevelopmentPractice();
        True(engine.State == SessionState.Practice, "practice can restart before the first test");
        engine.FinishDevelopmentPractice();
        engine.SetDevelopmentPhaseDurations(10, 30);
        engine.SkipDevelopmentTest("Participant needs a different condition first");
        True(engine.CurrentBlockSkipped && engine.State == SessionState.BlockEnded, "skip is a recorded disposition, not an aborted participant");
        True(!events.Exists(e => e.EventType == "BlockStarted"), "skip creates no zero-score block");
        engine.Advance();
        True(engine.RemainingSeconds == 30, "shared break duration applies immediately");
        clock.Advance(TimeSpan.FromSeconds(7));
        engine.SetDevelopmentPhaseDurations(10, 40);
        True(engine.RemainingSeconds == 33, "editing break preserves elapsed rest time");
        engine.QueueDevelopmentRepeat("WE_MT", "Retry skipped condition");
        Throws(() => engine.QueueDevelopmentRepeat("WE_MT", "Duplicate"), "duplicate queued repeat rejected");
        engine.SkipDevelopmentBreak();
        True(engine.State == SessionState.BlockReady && engine.CurrentBlockId == "WE_MT-attempt-02", "repeat has a distinct attempt identity");
        engine.StartBlock();
        Throws(() => engine.SkipDevelopmentTest("unsafe"), "running test cannot be skipped");
        Throws(() => engine.QueueDevelopmentRepeat("WE_MT", "unsafe"), "running queue cannot be replaced");
        clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick();
        engine.Advance();
        True(engine.RemainingSeconds == 40, "same duration applies to every subsequent break");
        clock.Advance(TimeSpan.FromSeconds(40)); engine.Advance();
        True(engine.CurrentCondition == "NE_MT" && engine.CurrentBlockId == "NE_MT-attempt-01", "repeat preserves remaining original test order");
        engine.QueueDevelopmentRepeat("WE_MT", "Repeat again before next test");
        True(engine.CurrentBlockId == "WE_MT-attempt-03", "repeated repeat cannot reuse an identity");
        True(Strict(events) && HasField(events, "BlockSkipped", "reason", "Participant needs a different condition first"), "exception reason and audit ordering preserved");
        True(events.Exists(e => e.EventType == "BreakSkipped"), "skip break is auditable");

        var standard = new SessionEngine(clock, new SessionEventSequence(), new SessionPlan("standard", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }),
            new SessionTiming(10, 30), new Sink(new List<SessionEvent>()), new Link());
        Throws(() => standard.SetDevelopmentPhaseDurations(1, 1), "standard study timing cannot be changed through administrator override");
        Throws(() => standard.SkipDevelopmentTest("test"), "standard sessions reject skip override");
    }

    private static void ExerciseFlexibleDevelopmentTiming()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>(); var link = new Link();
        var engine = NewEngine(clock, new Sink(events), link);
        engine.SetDevelopmentDuration("WE_MT", 30);
        engine.SetDevelopmentDuration("NE_MT", 60);
        BeginFirstBlock(engine, clock); engine.StartBlock();
        True(engine.RemainingSeconds == 30, "individual first-test duration applied");
        Throws(() => engine.SetDevelopmentDuration("WE_MT", 45), "running duration cannot be edited");
        engine.SetDevelopmentDuration("NE_MT", 50);
        clock.Advance(TimeSpan.FromSeconds(10)); engine.PauseDevelopmentBlock();
        True(engine.State == SessionState.BlockPaused && !link.Enabled && !engine.TargetsActive, "pause disables link and firing");
        clock.Advance(TimeSpan.FromSeconds(1200)); engine.Tick();
        True(engine.RemainingSeconds == 20 && engine.CurrentActiveSeconds == 10, "paused wall time does not consume test time");
        engine.SetDevelopmentDuration("WE_MT", 45);
        True(engine.RemainingSeconds == 35, "paused duration edit preserves active time");
        bool rejected = false;
        try { engine.SetDevelopmentDuration("WE_MT", 9); } catch(ArgumentException) { rejected = true; }
        True(rejected && engine.RemainingSeconds == 35, "cannot shorten below elapsed active time");
        engine.ResumeDevelopmentBlock();
        True(link.Enabled && engine.TargetsActive && engine.RemainingSeconds == 35, "resume continues same block");
        clock.Advance(TimeSpan.FromSeconds(34)); engine.Tick();
        True(engine.State == SessionState.BlockRunning, "resumed deadline not reached early");
        clock.Advance(TimeSpan.FromSeconds(1)); engine.Tick();
        True(engine.State == SessionState.BlockEnded && engine.CurrentActiveSeconds == 45 && !link.Enabled, "resumed block ends at active deadline");
        True(events.FindAll(e=>e.EventType=="BlockStarted").Count == 1 &&
            events.FindAll(e=>e.EventType=="BlockPaused").Count == 1 && events.FindAll(e=>e.EventType=="BlockResumed").Count == 1,
            "pause and resume do not manufacture a new block start");
        True(Strict(events), "flexible timing retains the shared event sequence");
        engine.Advance(); clock.Advance(TimeSpan.FromSeconds(1)); engine.Advance(); engine.StartBlock();
        True(engine.CurrentCondition == "NE_MT" && engine.RemainingSeconds == 50, "upcoming test keeps independently edited duration");
        clock.Advance(TimeSpan.FromSeconds(7)); engine.PauseDevelopmentBlock(); engine.StopDevelopmentBlock();
        True(engine.State == SessionState.BlockEnded && engine.CurrentBlockStoppedEarly && engine.CurrentActiveSeconds == 7,
            "stop finishes paused test without aborting participant");
        Throws(()=>engine.ResumeDevelopmentBlock(), "stopped test cannot accidentally resume");
        engine.AddNote("Participant session stays open after stop");
        engine.Advance(); clock.Advance(TimeSpan.FromSeconds(1)); engine.Advance();
        True(engine.CurrentCondition == "WE_FT" && engine.State == SessionState.BlockReady, "remaining tests survive early stop");
        engine.StartBlock(); clock.Advance(TimeSpan.FromSeconds(300)); engine.PauseDevelopmentBlock();
        True(engine.State == SessionState.BlockEnded && !engine.CurrentBlockStoppedEarly, "deadline wins over late pause");
    }

    private static void ExercisePauseAndResumeFailures()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var link = new Link(); var engine = NewEngine(clock,new Sink(new List<SessionEvent>()),link);
        BeginFirstBlock(engine,clock); engine.StartBlock(); link.ThrowOnDisable = true;
        engine.PauseDevelopmentBlock();
        True(engine.State == SessionState.Failed && !engine.TargetsActive, "pause cannot proceed after link stop failure");
        link = new Link(); engine = NewEngine(clock,new Sink(new List<SessionEvent>()),link);
        BeginFirstBlock(engine,clock); engine.StartBlock(); engine.PauseDevelopmentBlock(); link.ThrowOnEnable = true;
        engine.ResumeDevelopmentBlock();
        True(engine.State == SessionState.Failed && !link.Enabled, "failed resume de-permits the link");
        link = new Link(); var sink = new SwitchableSink(); engine = NewEngine(clock,sink,link);
        BeginFirstBlock(engine,clock); engine.StartBlock(); sink.Accept = false; engine.PauseDevelopmentBlock();
        True(engine.State == SessionState.Failed && !link.Enabled, "pause logging failure cannot leave test running");
        link = new Link(); engine = NewEngine(clock,new Sink(new List<SessionEvent>()),link);
        BeginFirstBlock(engine,clock); engine.StartBlock(); engine.PauseDevelopmentBlock(); engine.Abort("paused abort");
        True(engine.State == SessionState.Aborted && !link.Enabled, "whole-session abort remains available while paused");
    }

    private static void ExerciseFlexibleControlsRequireDevelopment()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var plan = new SessionPlan("p1",new[]{"WE_MT","NE_MT","WE_FT","NE_FT"});
        var engine = new SessionEngine(clock,new SessionEventSequence(),plan,new SessionTiming(1,1),new Sink(new List<SessionEvent>()),new Link());
        BeginFirstBlock(engine,clock); engine.StartBlock();
        Throws(()=>engine.PauseDevelopmentBlock(), "standard plan rejects pause override");
        Throws(()=>engine.StopDevelopmentBlock(), "standard plan rejects early-stop override");
        Throws(()=>engine.SetDevelopmentDuration("WE_MT",60), "standard plan rejects duration override");
        foreach(double invalid in new[]{0d,-1,double.NaN,double.PositiveInfinity,3601})
        {
            bool rejected = false;
            try { SessionEngine.ValidateDevelopmentDuration(invalid); } catch(ArgumentException) { rejected = true; }
            True(rejected, "invalid duration rejected: "+invalid);
        }
    }

    private sealed class SwitchableSink : ISessionEventSink
    {
        public bool Accept = true;
        public bool TryRecord(SessionEvent item) => Accept;
    }

    private static void ExercisePausedTriggerWindows()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var block = new ScenarioBlockController(clock,new SessionEventSequence(),"test",1,30,true);
        var model = new CountingShot(); var ray = new Elts.Geometry.Ray3d(Elts.Geometry.Vector3d.Zero,new Elts.Geometry.Vector3d(0,0,1));
        block.Start(); clock.Advance(TimeSpan.FromSeconds(1)); block.Pause();
        var pausedAt = clock.Now;
        block.Fire(model,new ShotContext(pausedAt,ray,true),Array.Empty<TargetEntity>(),_=>true);
        True(model.Calls == 0, "paused block does not admit a shot");
        clock.Advance(TimeSpan.FromSeconds(20)); block.Resume();
        block.Fire(model,new ShotContext(pausedAt,ray,true),Array.Empty<TargetEntity>(),_=>true);
        True(model.Calls == 0, "resumed block rejects observations from the paused interval");
        block.Fire(model,new ShotContext(clock.Now,ray,true),Array.Empty<TargetEntity>(),_=>true);
        True(model.Calls == 1, "fresh resumed observation reaches shot model");
        block.Stop();
        block.Fire(model,new ShotContext(clock.Now,ray,true),Array.Empty<TargetEntity>(),_=>true);
        True(model.Calls == 1, "stopped block rejects further shots");
    }

    private sealed class CountingShot : IShotModel
    {
        public int Calls;
        public IReadOnlyList<SessionEvent> Fire(ShotContext shot,string blockId,IEnumerable<TargetEntity> targets,Func<TargetEntity,bool> visible)
        {Calls++;return Array.Empty<SessionEvent>();}
    }

    private static void ExerciseNominalSessionAndAuditMetadata()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>();
        var link = new Link();
        var engine = NewEngine(clock, new Sink(events), link);

        var bad = engine.StartSetup(new SessionReadinessInput());
        True(!bad.IsReady && !bad.SetupAccepted && !bad.StudyReady && bad.Failures.Count == 7,
            "specific physical readiness failures never enable study");

        var accepted = engine.StartSetup(Ready());
        True(accepted.IsReady && accepted.SetupAccepted && engine.State == SessionState.SessionSetup,
            "setup reports both readiness and accepted session start");
        True(HasField(events, "SessionMetadata", "participantId", "p1")
            && HasField(events, "SessionMetadata", "conditionOrder", "WE_MT|NE_MT|WE_FT|NE_FT"),
            "session metadata records participant and supplied condition order");

        engine.Advance();
        engine.Advance();
        clock.Advance(TimeSpan.FromSeconds(1));
        engine.Tick();
        True(engine.State == SessionState.BlockReady, "practice timer advances to first block");

        engine.StartBlock();
        True(engine.State == SessionState.BlockRunning && engine.TargetsActive && link.Enabled,
            "WE block starts link");
        True(engine.CurrentBlockId == "WE_MT-attempt-01", "first block has an explicit attempt identity");

        clock.Advance(TimeSpan.FromSeconds(300));
        engine.Tick();
        True(engine.State == SessionState.BlockEnded && !link.Enabled, "exact block deadline ends link");

        engine.Advance();
        clock.Advance(TimeSpan.FromSeconds(1));
        engine.Advance();
        True(engine.CurrentBlockIndex == 1 && engine.State == SessionState.BlockReady, "break advances supplied order");
        True(Strict(events), "session transitions use one strict sequence");
    }

    private static void ExerciseRerunIdentityAndClosedSessionRules()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>();
        var engine = NewEngine(clock, new Sink(events), new Link());
        BeginFirstBlock(engine, clock);

        string firstAttempt = engine.CurrentBlockId!;
        engine.StartBlock();
        engine.Abort("operator-stop");
        True(engine.State == SessionState.Aborted && engine.CanRerun, "abort works from a running block and permits its rerun");
        engine.Abort("repeat");
        True(engine.State == SessionState.Aborted, "abort is idempotent only after an abort");
        Throws(() => engine.AddNote("after abort"), "notes cannot be appended after a terminal abort");

        engine.RerunCurrentBlock();
        True(engine.RerunIndex == 1 && engine.CurrentBlockId == "WE_MT-attempt-02" && engine.CurrentBlockId != firstAttempt,
            "rerun creates a distinct block identity before another block starts");

        engine.StartBlock();
        clock.Advance(TimeSpan.FromSeconds(300));
        engine.Tick();
        while (engine.State != SessionState.SessionComplete)
        {
            if (engine.State == SessionState.BlockReady)
            {
                engine.StartBlock();
                clock.Advance(TimeSpan.FromSeconds(300));
                engine.Tick();
            }
            else if (engine.State == SessionState.BlockEnded) engine.Advance();
            else if (engine.State == SessionState.Break)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                engine.Advance();
            }
            else throw new Exception("unexpected state");
        }

        Throws(() => engine.AddNote("after complete"), "notes cannot be appended after session completion");
        Throws(() => engine.EmitProvisionalSyncMarker("after complete"), "markers cannot be appended after session completion");
        True(events.Exists(e => e.EventType == "BlockStarted" && Field(e, "blockId") == firstAttempt)
            && events.Exists(e => e.EventType == "BlockStarted" && Field(e, "blockId") == "WE_MT-attempt-02"),
            "each recorded attempt retains a separate block identifier");
    }

    private static void ExerciseSinkFailureFailsClosed()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var setupFailure = NewEngine(clock, new Sink(new List<SessionEvent>(), false), new Link());
        var report = setupFailure.StartSetup(Ready());
        True(report.IsReady && !report.SetupAccepted && setupFailure.State == SessionState.Failed,
            "readiness remains distinct from unsuccessful setup event acceptance");

        var events = new List<SessionEvent>();
        var link = new Link();
        var startTransitionFailure = NewEngine(clock, new Sink(events, failAtAttempt: 6), link);
        BeginFirstBlock(startTransitionFailure, clock);
        startTransitionFailure.StartBlock();
        True(startTransitionFailure.State == SessionState.Failed && !link.Enabled,
            "a failed start transition fails closed and de-permits the WE link");
        True(!events.Exists(e => e.EventType == "BlockStarted"),
            "block start is not emitted after its transition was rejected");
    }

    private static void ExerciseLinkFailuresFailClosedWithoutInvalidTransition()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>();
        var stopThrowingLink = new Link { ThrowOnDisable = true };
        var engine = NewEngine(clock, new Sink(events), stopThrowingLink);
        BeginFirstBlock(engine, clock);
        engine.StartBlock();
        clock.Advance(TimeSpan.FromSeconds(300));
        engine.Tick();

        True(engine.State == SessionState.Failed && !stopThrowingLink.Enabled,
            "a throwing stop link is caught and leaves the engine failed closed");
        True(!events.Exists(e => e.EventType == "SessionTransition" && Field(e, "to") == "BlockEnded"),
            "block-ended transition is not recorded after link stop failure");

        var startThrowingLink = new Link { ThrowOnEnable = true };
        var startFailure = NewEngine(clock, new Sink(new List<SessionEvent>()), startThrowingLink);
        BeginFirstBlock(startFailure, clock);
        startFailure.StartBlock();
        True(startFailure.State == SessionState.Failed && !startThrowingLink.Enabled,
            "a throwing start link is contained and fails closed");
    }

    private static void ExerciseDeadlineFirstOperatorActions()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var events = new List<SessionEvent>();
        var engine = NewEngine(clock, new Sink(events), new Link());
        BeginFirstBlock(engine, clock);
        engine.StartBlock();
        clock.Advance(TimeSpan.FromSeconds(300));
        engine.AddNote("deadline note");
        engine.Abort("late abort");

        int ended = events.FindIndex(e => e.EventType == "BlockEnded");
        int note = events.FindIndex(e => e.EventType == "OperatorNote");
        True(ended >= 0 && note > ended, "deadline expiry is recorded before a later operator note");
        True(!events.Exists(e => e.EventType == "BlockAborted"), "late abort preserves normal block completion");
    }

    private static void ExerciseOperatorAbortAcrossOpenStates()
    {
        VerifyPhaseAbort(SessionState.Idle);
        VerifyPhaseAbort(SessionState.SessionSetup);
        VerifyPhaseAbort(SessionState.Calibration);
        VerifyPhaseAbort(SessionState.Practice);
        VerifyPhaseAbort(SessionState.BlockReady);
        VerifyPhaseAbort(SessionState.Break);
    }

    private static void VerifyPhaseAbort(SessionState phase)
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var engine = NewEngine(clock, new Sink(new List<SessionEvent>()), new Link());
        if (phase != SessionState.Idle)
        {
            True(engine.StartSetup(Ready()).SetupAccepted, "phase abort setup accepted");
            if (phase == SessionState.Calibration || phase == SessionState.Practice || phase == SessionState.BlockReady || phase == SessionState.Break) engine.Advance();
            if (phase == SessionState.Practice || phase == SessionState.BlockReady || phase == SessionState.Break) engine.Advance();
            if (phase == SessionState.BlockReady || phase == SessionState.Break)
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                engine.Tick();
            }
            if (phase == SessionState.Break)
            {
                engine.StartBlock();
                clock.Advance(TimeSpan.FromSeconds(300));
                engine.Tick();
                engine.Advance();
            }
        }
        True(engine.State == phase, "phase reached before abort: " + phase);
        engine.Abort("operator abort");
        True(engine.State == SessionState.Aborted && (phase == SessionState.Break || !engine.CanRerun), "operator abort works without a phantom rerun from " + phase);
        if (phase != SessionState.Break) Throws(() => engine.RerunCurrentBlock(), "phase abort cannot rerun a phantom block: " + phase);
    }


    private static SessionEngine NewEngine(ManualSharedClock clock, ISessionEventSink sink, ISessionLink link)
    {
        var plan = new SessionPlan("p1", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 300, true);
        return new SessionEngine(clock, new SessionEventSequence(), plan, new SessionTiming(1, 1, true), sink, link);
    }

    private static void BeginFirstBlock(SessionEngine engine, ManualSharedClock clock)
    {
        True(engine.StartSetup(Ready()).SetupAccepted, "test session setup accepted");
        engine.Advance();
        engine.Advance();
        clock.Advance(TimeSpan.FromSeconds(1));
        engine.Tick();
    }

    private static SessionReadinessInput Ready() => new SessionReadinessInput
    {
        TwoDisplays = true,
        SteamVrRunning = true,
        TrackersBoundAndValidForTwoSeconds = true,
        ConfigurationHashesComputed = true,
        DiskFreeBytes = 2L * 1024 * 1024 * 1024,
        LogWriterAlive = true,
        WeLinkReachable = true,
    };

    private static bool Strict(List<SessionEvent> events)
    {
        for (int i = 1; i < events.Count; i++)
            if (events[i].Sequence != events[i - 1].Sequence + 1) return false;
        return true;
    }

    private static bool HasField(List<SessionEvent> events, string eventType, string name, string value)
    {
        return events.Exists(e => e.EventType == eventType && Field(e, name) == value);
    }

    private static string? Field(SessionEvent item, string name)
    {
        foreach (var field in item.Fields)
            if (field.Name == name) return field.Text;
        return null;
    }

    private sealed class Sink : ISessionEventSink
    {
        private readonly List<SessionEvent> events;
        private readonly int? failAtAttempt;
        private int attempts;

        public Sink(List<SessionEvent> events, bool ok = true, int? failAtAttempt = null)
        {
            this.events = events;
            this.failAtAttempt = ok ? failAtAttempt : 1;
        }

        public bool TryRecord(SessionEvent item)
        {
            attempts++;
            if (failAtAttempt.HasValue && attempts >= failAtAttempt.Value) return false;
            events.Add(item);
            return true;
        }
    }

    private sealed class Link : ISessionLink
    {
        public bool Enabled;
        public bool ThrowOnEnable;
        public bool ThrowOnDisable;

        public bool TrySetEnabled(bool enabled)
        {
            if (enabled && ThrowOnEnable) { Enabled = true; throw new InvalidOperationException("enable failed"); }
            if (!enabled && ThrowOnDisable)
            {
                Enabled = false;
                throw new InvalidOperationException("disable failed");
            }
            Enabled = enabled;
            return true;
        }
    }
}
