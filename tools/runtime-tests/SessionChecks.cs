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
        Console.WriteLine("PASS: " + passed + " session checks");
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
