#nullable enable
using System;
using System.IO;
using Elts.Clock;
using Elts.EltsLink;

static class EltsLinkChecks
{
    private static int passed;
    private static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }
    private static void Throws<T>(Action action, string name) where T : Exception { try { action(); throw new Exception("FAIL: " + name + " did not throw"); } catch (T) { passed++; } }
    private static EltsMessage Command(EltsMessageType type, long sequence, long ticks, EltsBlockContext? context = null, string? reason = null) =>
        EltsMessage.Command(type, sequence, ticks, applicationVersion: type == EltsMessageType.Hello ? "checks-v1" : null, blockContext: context, reason: reason);

    private static void Main()
    {
        CodecChecks();
        LifecycleAndSafetyFixtureChecks();
        BoundaryAndOffsetChecks();
        Console.WriteLine("PASS: " + passed + " DEV-10 synthetic ELTS-link checks");
    }

    private static void CodecChecks()
    {
        var start = Command(EltsMessageType.Start, 7, 123, new EltsBlockContext("synthetic-p", "1", "WE_MT", TimeSpan.TicksPerSecond));
        var encoded = EltsCodec.Encode(start);
        var decoded = EltsCodec.Decode(encoded);
        True(decoded.Type == EltsMessageType.Start && decoded.Sequence == 7 && decoded.BlockContext!.DurationTicks == TimeSpan.TicksPerSecond, "START round trips as exact integer ticks");
        Throws<InvalidDataException>(() => EltsCodec.Decode("{bad}\n"), "malformed JSON is rejected");
        Throws<InvalidDataException>(() => EltsCodec.Decode("{\"protocolVersion\":\"development-synthetic-v1\",\"messageType\":\"ARM\",\"sequence\":1,\"unityMonotonicTicks\":0,\"target\":{}}\n"), "prohibited target payload is rejected by strict whitelist");
        Throws<InvalidDataException>(() => EltsCodec.Decode("{\"protocolVersion\":\"development-synthetic-v1\",\"messageType\":\"ARM\",\"sequence\":1,\"sequence\":1,\"unityMonotonicTicks\":0}\n"), "duplicate JSON field is rejected");
        Throws<InvalidDataException>(() => EltsCodec.Decode("{protocolVersion:\"development-synthetic-v1\",\"messageType\":\"ARM\",\"sequence\":1,\"unityMonotonicTicks\":0}\n"), "unquoted JSON property is rejected");
        Throws<InvalidDataException>(() => EltsCodec.Decode("/* no comments */ {\"protocolVersion\":\"development-synthetic-v1\",\"messageType\":\"ARM\",\"sequence\":1,\"unityMonotonicTicks\":0}\n"), "JSON comments are rejected");
        Throws<InvalidDataException>(() => EltsCodec.Decode("{\"protocolVersion\":\"controller-unknown\",\"messageType\":\"PING\",\"sequence\":1,\"unityMonotonicTicks\":0}\n"), "incompatible version is rejected");
    }

    private static void LifecycleAndSafetyFixtureChecks()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var controller = new SimulatedEltsController(clock, new EltsLinkRuntimeOptions(3 * TimeSpan.TicksPerSecond, 30 * TimeSpan.TicksPerSecond, 3));
        var link = new MockEltsLink(controller);
        var rejectedStart = Command(EltsMessageType.Start, 3, 0, new EltsBlockContext("synthetic-p", "1", "WE_MT", TimeSpan.TicksPerSecond));
        True(link.Send(rejectedStart).Type == EltsMessageType.Nack && !link.LedPermission, "START before HELLO/ARM cannot permit output");
        True(link.Send(Command(EltsMessageType.Hello, 1, 0)).Type == EltsMessageType.Ack, "HELLO is acknowledged");
        True(link.Send(Command(EltsMessageType.Arm, 2, 0)).State == EltsLinkState.Armed, "ARM enters Armed with permission inhibited");
        var start = Command(EltsMessageType.Start, 4, 0, new EltsBlockContext("synthetic-p", "1", "WE_MT", TimeSpan.TicksPerSecond));
        var startAck = link.Send(start);
        True(startAck.Type == EltsMessageType.Ack && startAck.State == EltsLinkState.Active && startAck.LedPermission == true && link.LedPermission, "START ACK is emitted after simulated permission becomes true");
        var replay = link.Send(start);
        True(replay.Fingerprint == startAck.Fingerprint && link.State == EltsLinkState.Active, "same sequence replays exactly one result without second transition");
        var conflict = link.Send(Command(EltsMessageType.Ping, 4, 0));
        True(conflict.Type == EltsMessageType.Nack && conflict.Error == "sequence_conflict", "conflicting duplicate sequence is NACKed");
        clock.Advance(TimeSpan.FromSeconds(3)); link.Advance();
        True(link.State == EltsLinkState.Armed && !link.LedPermission, "three-second synthetic heartbeat lapse de-permits and returns Armed");
        link.SimulateReconnect();
        True(link.State == EltsLinkState.Idle && !link.LedPermission && link.Send(start).Type == EltsMessageType.Nack, "reconnect never resumes Active and requires HELLO");

        var guardClock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var guarded = new SimulatedEltsController(guardClock); var guardedLink = new MockEltsLink(guarded);
        guardedLink.Send(Command(EltsMessageType.Hello, 1, 0)); guardedLink.Send(Command(EltsMessageType.Arm, 2, 0));
        guardedLink.Send(Command(EltsMessageType.Start, 3, 0, new EltsBlockContext("synthetic-p", "1", "WE_MT", TimeSpan.TicksPerSecond)));
        for (long sequence = 4; sequence <= 18; sequence++)
        {
            guardClock.Advance(TimeSpan.FromSeconds(2));
            guardedLink.Send(Command(EltsMessageType.Ping, sequence, guardClock.Now.Ticks));
        }
        guardClock.Advance(TimeSpan.FromSeconds(1)); guardedLink.Advance();
        True(!guardedLink.LedPermission && guardedLink.State == EltsLinkState.Armed, "duration plus 30-second fixture guard de-permits despite fresh heartbeats");

        guarded.ReleaseSimulatedEStopForFixture(); guardedLink.Send(Command(EltsMessageType.Start, 19, 0, new EltsBlockContext("synthetic-p", "2", "WE_MT", TimeSpan.TicksPerSecond)));
        guarded.SimulateEStop();
        True(!guardedLink.LedPermission && guardedLink.State == EltsLinkState.Armed, "simulated E-stop de-permits");
        var afterStop = guardedLink.Send(Command(EltsMessageType.Start, 20, 0, new EltsBlockContext("synthetic-p", "3", "WE_MT", TimeSpan.TicksPerSecond)));
        True(afterStop.Type == EltsMessageType.Nack && afterStop.Error == "estop_latched", "no wire command can reset a simulated E-stop latch");
    }

    private static void BoundaryAndOffsetChecks()
    {
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        var none = new NullEltsLink(clock);
        var result = none.Send(Command(EltsMessageType.Start, 1, 0, new EltsBlockContext("synthetic-p", "1", "WE_MT", 1)));
        True(result.Type == EltsMessageType.Nack && !none.LedPermission && none.State == EltsLinkState.Idle, "null link can never permit output");
        var offset = new EltsClockOffsetEstimate(100, 160, 70);
        True(offset.UnitySentTicks == 100 && offset.UnityAckTicks == 160 && offset.JetsonTicks == 70 && offset.MidpointOffsetTicks == 60, "clock offset retains raw triplet and uses integer midpoint");
        Throws<ArgumentOutOfRangeException>(() => new EltsClockOffsetEstimate(20, 19, 0), "offset rejects an ACK earlier than send");
    }
}
