#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;

namespace Elts.EltsLink
{
public interface IEltsLink
{
    EltsLinkState State { get; }
    bool LedPermission { get; }
    EltsMessage Send(EltsMessage command);
    void Advance();
    void SimulateReconnect();
}

/// <summary>Raw timing evidence retained by a client. Its midpoint estimate is metadata, not a D-12 synchronization decision.</summary>
public readonly struct EltsClockOffsetEstimate
{
    public readonly long UnitySentTicks, UnityAckTicks, JetsonTicks;
    public EltsClockOffsetEstimate(long unitySentTicks, long unityAckTicks, long jetsonTicks)
    {
        if (unitySentTicks < 0 || unityAckTicks < unitySentTicks || jetsonTicks < 0) throw new ArgumentOutOfRangeException();
        UnitySentTicks = unitySentTicks; UnityAckTicks = unityAckTicks; JetsonTicks = jetsonTicks;
    }
    public long MidpointOffsetTicks => UnitySentTicks + (UnityAckTicks - UnitySentTicks) / 2 - JetsonTicks;
}

/// <summary>Pure fixture model. LedPermission represents no IO, pin, process, or physical light state.</summary>
public sealed class SimulatedEltsController
{
    private readonly ISharedClock clock;
    private readonly EltsLinkRuntimeOptions options;
    private readonly Dictionary<long, Replay> replays = new Dictionary<long, Replay>();
    private readonly Queue<long> replayOrder = new Queue<long>();
    private bool helloComplete, eStopLatched;
    private long heartbeatDeadlineTicks, activeDeadlineTicks;
    // Highest command sequence accepted in this connection epoch. The bounded
    // reply cache can forget payloads, but it must never permit an old message
    // to execute again after eviction.
    private long sequenceWatermark = -1;
    public EltsLinkState State { get; private set; } = EltsLinkState.Idle;
    public bool LedPermission { get; private set; }
    public SimulatedEltsController(ISharedClock clock, EltsLinkRuntimeOptions? options = null)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); this.options = options ?? new EltsLinkRuntimeOptions();
    }
    public EltsMessage Receive(EltsMessage command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command)); Advance();
        var fingerprint = command.Fingerprint;
        if (replays.TryGetValue(command.Sequence, out var replay))
        {
            if (replay.Fingerprint != fingerprint) return Nack(command, "sequence_conflict");
            // Cached ACK data records the old command outcome. It must not be
            // presented as a current permission/state after a timeout or guard.
            if (replay.Response.State != State || replay.Response.LedPermission != LedPermission)
                return Nack(command, "replay_state_changed");
            return replay.Response;
        }
        if (command.Sequence <= sequenceWatermark) return Nack(command, "sequence_expired");
        var response = Process(command);
        sequenceWatermark = command.Sequence;
        replays.Add(command.Sequence, new Replay(fingerprint, response)); replayOrder.Enqueue(command.Sequence);
        if (replayOrder.Count > options.ReplayCacheCapacity) replays.Remove(replayOrder.Dequeue());
        return response;
    }
    public void Advance()
    {
        if (!LedPermission) return;
        var now = clock.Now.Ticks;
        if (now >= heartbeatDeadlineTicks) Depermit();
        else if (now >= activeDeadlineTicks) Depermit();
    }
    public void SimulateReconnect() { Depermit(); State = EltsLinkState.Idle; helloComplete = false; replays.Clear(); replayOrder.Clear(); sequenceWatermark = -1; }
    public void SimulateEStop() { eStopLatched = true; Depermit(); }
    /// <summary>Fixture-only external intervention. It models neither wiring nor a real E-stop reset procedure.</summary>
    public void ReleaseSimulatedEStopForFixture() { eStopLatched = false; }
    private EltsMessage Process(EltsMessage command)
    {
        if (command.ProtocolVersion != EltsProtocolV1.Version) return Nack(command, "unsupported_version");
        switch (command.Type)
        {
            case EltsMessageType.Hello:
                helloComplete = true; return Ack(command);
            case EltsMessageType.Arm:
                if (!helloComplete || State != EltsLinkState.Idle) return Nack(command, "invalid_state"); State = EltsLinkState.Armed; return Ack(command);
            case EltsMessageType.Disarm:
                Depermit(); State = EltsLinkState.Idle; return Ack(command);
            case EltsMessageType.Start:
                if (!helloComplete || State != EltsLinkState.Armed || eStopLatched || command.BlockContext == null) return Nack(command, eStopLatched ? "estop_latched" : "invalid_state");
                State = EltsLinkState.Active; LedPermission = true; var now = clock.Now.Ticks; heartbeatDeadlineTicks = AddChecked(now, options.HeartbeatTimeoutTicks); activeDeadlineTicks = AddChecked(now, AddChecked(command.BlockContext.DurationTicks, options.MaximumOnTimeExtraTicks)); return Ack(command);
            case EltsMessageType.Stop:
                if (State == EltsLinkState.Active) Depermit(); return Ack(command);
            case EltsMessageType.Ping:
                if (State == EltsLinkState.Active) heartbeatDeadlineTicks = AddChecked(clock.Now.Ticks, options.HeartbeatTimeoutTicks); return Ack(command);
            case EltsMessageType.Status: return Ack(command);
            default: return Nack(command, "unknown_command");
        }
    }
    private EltsMessage Ack(EltsMessage command) => EltsMessage.Ack(command.Sequence, command.Type, clock.Now.Ticks, State, LedPermission);
    private EltsMessage Nack(EltsMessage command, string error) => EltsMessage.Nack(command.Sequence, command.Type, clock.Now.Ticks, State, error);
    private void Depermit() { LedPermission = false; if (State == EltsLinkState.Active) State = EltsLinkState.Armed; }
    private static long AddChecked(long left, long right) { if (right > long.MaxValue - left) return long.MaxValue; return left + right; }
    private readonly struct Replay { public readonly string Fingerprint; public readonly EltsMessage Response; public Replay(string fingerprint, EltsMessage response) { Fingerprint = fingerprint; Response = response; } }
}

public sealed class MockEltsLink : IEltsLink
{
    private readonly SimulatedEltsController controller;
    public MockEltsLink(SimulatedEltsController controller) { this.controller = controller ?? throw new ArgumentNullException(nameof(controller)); }
    public EltsLinkState State => controller.State; public bool LedPermission => controller.LedPermission;
    public EltsMessage Send(EltsMessage command) => controller.Receive(command); public void Advance() => controller.Advance(); public void SimulateReconnect() => controller.SimulateReconnect();
}

/// <summary>Safe default adapter: it never connects to anything and can never grant simulated permission.</summary>
public sealed class NullEltsLink : IEltsLink
{
    private readonly ISharedClock clock;
    public NullEltsLink(ISharedClock clock) { this.clock = clock ?? throw new ArgumentNullException(nameof(clock)); }
    public EltsLinkState State => EltsLinkState.Idle; public bool LedPermission => false;
    public EltsMessage Send(EltsMessage command) { if (command == null) throw new ArgumentNullException(nameof(command)); return EltsMessage.Nack(command.Sequence, command.Type, clock.Now.Ticks, State, "null_link"); }
    public void Advance() { } public void SimulateReconnect() { }
}
}
