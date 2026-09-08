#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.EltsLink;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

static class EltsSessionChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Main()
    {
        ExerciseWeAndNeLifecycle();
        ExerciseBoundedIdempotentRetry();
        ExerciseHeartbeatLossFailsClosed();
        ExerciseReconnectNeverResumes();
        Console.WriteLine("PASS: " + passed + " ELTS session adapter checks");
    }

    static void ExerciseWeAndNeLifecycle()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var records = new List<SessionEvent>();
        var link = new MockEltsLink(new SimulatedEltsController(clock));
        var engine = NewEngine(clock, records, link);
        Begin(engine, clock); engine.StartBlock();
        True(engine.State == SessionState.BlockRunning && link.State == EltsLinkState.Active && link.LedPermission, "WE only starts after HELLO and ARM ACKs");
        True(Has(records, "EltsCommand", "command", "HELLO") && Has(records, "EltsAck", "command", "ARM") && Has(records, "EltsCommand", "command", "START"), "WE commands and ACKs are typed events");
        True(Has(records, "EltsOffset", "commandSequence", "1"), "endpoint offset evidence is recorded with command sequence");
        clock.Advance(TimeSpan.FromSeconds(1)); engine.Tick();
        True(engine.State == SessionState.BlockEnded && !link.LedPermission, "normal WE deadline stops synthetic permission");
        engine.Advance(); engine.Advance(); engine.StartBlock();
        True(engine.State == SessionState.BlockRunning && link.State == EltsLinkState.Idle && !link.LedPermission, "default development NE policy stays Idle");
        True(Has(records, "EltsState", "condition", "NE_MT") && Has(records, "EltsCommand", "command", "DISARM"), "NE block is prepared and logged without deciding D-10");
        True(Strict(records), "engine and adapter share one strictly increasing event sequence");
    }

    static void ExerciseBoundedIdempotentRetry()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch); var records = new List<SessionEvent>(); var sequence = new SessionEventSequence();
        var mock = new MockEltsLink(new SimulatedEltsController(clock)); var flaky = new ThrowOnceLink(mock); var adapter = new SessionEltsLinkAdapter(clock, sequence, new Sink(records), flaky);
        True(adapter.PrepareBlock(new SessionBlockLinkContext("synthetic-pseudonym", "WE_MT-attempt-01", "WE_MT", TimeSpan.TicksPerSecond)), "bounded retry recovers a transient synthetic delivery loss");
        double? first = null; int helloAttempts = 0;
        foreach (var record in records) if (record.EventType == "EltsCommand" && Field(record, "command") == "HELLO")
        {
            helloAttempts++; double? commandSequence = Number(record, "commandSequence");
            True(commandSequence.HasValue, "command event records a sequence");
            if (!first.HasValue) first = commandSequence;
            True(commandSequence.GetValueOrDefault() == first.GetValueOrDefault(), "retry reuses the same protocol sequence");
        }
        True(helloAttempts == 2, "retry count is bounded and observable");
    }

    static void ExerciseHeartbeatLossFailsClosed()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var records = new List<SessionEvent>();
        var controller = new SimulatedEltsController(clock, new EltsLinkRuntimeOptions(TimeSpan.TicksPerSecond));
        var engine = NewEngine(clock, records, new MockEltsLink(controller));
        Begin(engine, clock); engine.StartBlock(); clock.Advance(TimeSpan.FromSeconds(2)); engine.Tick();
        True(engine.State == SessionState.Failed && !controller.LedPermission, "heartbeat timeout fails session and de-permits controller");
        True(Has(records, "EltsLinkLoss", "operation", "active_permission_lost") && Has(records, "SessionTransition", "to", "Failed"), "loss and terminal session state are logged");
    }

    static void ExerciseReconnectNeverResumes()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var records = new List<SessionEvent>();
        var link = new MockEltsLink(new SimulatedEltsController(clock));
        var engine = NewEngine(clock, records, link);
        Begin(engine, clock); engine.StartBlock(); link.SimulateReconnect(); engine.Tick();
        True(engine.State == SessionState.Failed && link.State == EltsLinkState.Idle && !link.LedPermission, "reconnect cannot silently resume an active block");
        True(Has(records, "EltsLinkLoss", "operation", "active_permission_lost"), "reconnect loss is recorded");
    }

    static SessionEngine NewEngine(ManualSharedClock clock, List<SessionEvent> records, IEltsLink link)
    {
        var sequence = new SessionEventSequence(); var sink = new Sink(records);
        return new SessionEngine(clock, sequence, new SessionPlan("synthetic-pseudonym", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }, 1, true), new SessionTiming(0, 0, true), sink,
            new SessionEltsLinkAdapter(clock, sequence, sink, link));
    }
    static void Begin(SessionEngine engine, ManualSharedClock clock) { True(engine.StartSetup(Ready()).SetupAccepted, "synthetic setup accepted"); engine.Advance(); engine.Advance(); engine.Tick(); True(engine.State == SessionState.BlockReady, "first block ready"); }
    static SessionReadinessInput Ready() => new SessionReadinessInput { TwoDisplays = true, SteamVrRunning = true, TrackersBoundAndValidForTwoSeconds = true, ConfigurationHashesComputed = true, DiskFreeBytes = 2L * 1024 * 1024 * 1024, LogWriterAlive = true, WeLinkReachable = true };
    static bool Has(IEnumerable<SessionEvent> records, string type, string field, string value) { foreach (var record in records) if (record.EventType == type) foreach (var item in record.Fields) if (item.Name == field && (item.Text == value || item.Number?.ToString("0") == value)) return true; return false; }
    static string? Field(SessionEvent item, string name) { foreach (var field in item.Fields) if (field.Name == name) return field.Text; return null; }
    static double? Number(SessionEvent item, string name) { foreach (var field in item.Fields) if (field.Name == name) return field.Number; return null; }
    static bool Strict(IReadOnlyList<SessionEvent> records) { long previous = 0; foreach (var item in records) { if (item.Sequence <= previous) return false; previous = item.Sequence; } return true; }
    sealed class Sink : ISessionEventSink { readonly List<SessionEvent> records; public Sink(List<SessionEvent> records) { this.records = records; } public bool TryRecord(SessionEvent item) { records.Add(item); return true; } }
    sealed class ThrowOnceLink : IEltsLink
    {
        readonly IEltsLink inner; bool fail = true;
        public ThrowOnceLink(IEltsLink inner) { this.inner = inner; }
        public EltsLinkState State => inner.State; public bool LedPermission => inner.LedPermission;
        public EltsMessage Send(EltsMessage command) { if (fail) { fail = false; throw new InvalidOperationException("synthetic transient loss"); } return inner.Send(command); }
        public void Advance() => inner.Advance(); public void SimulateReconnect() => inner.SimulateReconnect();
    }
}
