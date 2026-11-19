#nullable enable
using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Elts.Clock;
using Elts.EltsLink;

namespace Elts.Session
{
    /// <summary>Isolated tracker protocol client. One worker owns Pump and the injected transport.</summary>
    public sealed class TrackerSender
    {
        // Wire-contract values in monotonic TimeSpan ticks (100 ns).
        private const long AckWindow = 100 * TimeSpan.TicksPerMillisecond;
        private const long HeartbeatInterval = 250 * TimeSpan.TicksPerMillisecond;
        private const long SnapshotLifetime = 250 * TimeSpan.TicksPerMillisecond;
        private const long ReopenInterval = 1000 * TimeSpan.TicksPerMillisecond;
        private static readonly byte[] RejectPartialFrame = { (byte)'!', (byte)'\n' };

        private readonly ISharedClock clock;
        private readonly ITrackerByteTransport transport;
        private readonly TrackerProtocol protocol = new TrackerProtocol();
        private readonly byte[] incoming = new byte[1024];
        private SessionEngine? engine;
        private SessionLiveSegment? lastConsidered;
        private Run? run;
        private Request? pending;
        private bool connected;
        private long openedAt, reopenAt;
        private bool partialLine;
        private int repairOffset;
        private int shutdown;
        private string status = "Simulator disconnected";

        public TrackerSender(ISharedClock clock, ITrackerByteTransport transport)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public string Status => Volatile.Read(ref status);
        public void ObserveSession(SessionEngine? session) => Volatile.Write(ref engine, session);
        public void RequestShutdown() => Interlocked.Exchange(ref shutdown, 1);
        private bool ShuttingDown => Volatile.Read(ref shutdown) != 0;
        private SessionLiveSegment? Live => Volatile.Read(ref engine)?.LiveSegment;
        private long Now => clock.Now.Ticks;

        /// <summary>Advance by at most one read and one write. Return false once shutdown is done.</summary>
        public bool Pump()
        {
            try
            {
                if (!connected)
                {
                    if (ShuttingDown) return false;
                    // A live edge observed while disconnected is retired even if this open succeeds.
                    lastConsidered = Live ?? lastConsidered;
                    if (Now >= reopenAt) Open();
                    return true;
                }

                RevokeIfNeeded();
                if (run == null && ShuttingDown) return FinishShutdown();
                if (run == null) ConsiderLiveEdge();
                if (run != null && pending == null && Now >= run.HeartbeatDue)
                {
                    if (run.LastSequence == int.MaxValue - 1) Stop("Sequence exhausted");
                    else Queue("HEARTBEAT", run.LastSequence + 1);
                }

                ServiceWrite();
                if (connected)
                {
                    int count = transport.Read(incoming);
                    if (count < 0 || count > incoming.Length) throw new IOException("Invalid tracker read count.");
                    protocol.Read(incoming, count, AcceptReply);
                }
                if (run == null && ShuttingDown) return FinishShutdown();
                return true;
            }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is UnauthorizedAccessException)
            {
                Disconnect("Simulator transport fault");
                return !ShuttingDown;
            }
        }

        private void Open()
        {
            protocol.Reset();
            connected = transport.TryOpen();
            openedAt = Now;
            reopenAt = openedAt + ReopenInterval;
            if (connected) SetStatus("Simulator idle");
        }

        private bool FinishShutdown()
        {
            if (connected) Disconnect("Simulator stopped");
            return false;
        }

        private void Disconnect(string reason)
        {
            try { transport.Close(); }
            catch (Exception error) when (error is IOException || error is ObjectDisposedException || error is UnauthorizedAccessException) { }
            connected = false;
            reopenAt = Now + ReopenInterval;
            lastConsidered = Live ?? lastConsidered;
            run = null;
            pending = null;
            partialLine = false;
            repairOffset = 0;
            protocol.Reset();
            SetStatus(reason);
        }

        private void ConsiderLiveEdge()
        {
            var segment = Live;
            if (segment == null || ReferenceEquals(segment, lastConsidered)) return;
            lastConsidered = segment;
            if (!TrackerProtocol.AllowsTracking(segment.Condition)) return;
            if (segment.EnteredAt.Ticks < openedAt || !Eligible(segment))
            {
                SetStatus("Simulator unavailable at live entry");
                return;
            }

            var id = new byte[16];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(id);
            run = new Run(segment, BitConverter.ToString(id).Replace("-", "").ToLowerInvariant());
            Queue("HELLO", 0);
            SetStatus("Simulator starting");
        }

        private bool Eligible(SessionLiveSegment segment)
        {
            long observed = segment.ObservedTicks;
            long now = Now;
            return !ShuttingDown && ReferenceEquals(segment, Live) && now < segment.Deadline.Ticks
                && now - observed < SnapshotLifetime;
        }

        private void RevokeIfNeeded()
        {
            if (run != null && !run.Stopping && !Eligible(run.Segment)) Stop();
        }

        private void Stop(string? fault = null)
        {
            if (run == null || run.Stopping) return;
            pending = null;
            SetStatus(fault ?? "Simulator stopping");
            if (run.Epoch == null)
            {
                run = null;
                SetStatus(fault ?? "Simulator idle");
                return;
            }
            run.Stopping = true;
            run.StopStatus = fault ?? "Simulator idle";
            Queue("STOP", run.LastSequence + 1);
        }

        private void Queue(string command, int sequence)
        {
            var current = run!;
            current.LastSequence = sequence;
            pending = new Request(command, sequence, TrackerProtocol.Encode(command, current.Id, current.Epoch,
                sequence, command == "START" ? current.Segment.Condition : null));
        }

        private void ServiceWrite()
        {
            RevokeIfNeeded();
            var request = pending;
            if (request == null) return;
            long now = Now;
            request.FirstAttempt ??= now;
            long elapsed = now - request.FirstAttempt.Value;
            if (elapsed >= 2 * AckWindow)
            {
                RejectRequest(request, request.Command + " acknowledgment timed out");
                return;
            }
            if (elapsed >= AckWindow && !request.Retried)
            {
                request.Retried = true;
                request.Offset = 0;
            }
            if (request.Offset == request.Frame.Length) return;

            if (request.Offset == 0 && partialLine)
            {
                // Even if only LF was missing, the old bytes must never become a valid command.
                repairOffset += Write(RejectPartialFrame, repairOffset);
                if (repairOffset == RejectPartialFrame.Length)
                {
                    repairOffset = 0;
                    partialLine = false;
                }
                return;
            }

            int count = Write(request.Frame, request.Offset);
            if (count == 0) return;
            request.Offset += count;
            partialLine = request.Offset != request.Frame.Length;
            if (!partialLine) request.SentFullFrame = true;
            if (!request.Transmitted && (request.Command == "START" || request.Command == "HEARTBEAT"))
                run!.HeartbeatDue = now + HeartbeatInterval;
            request.Transmitted = true;
        }

        private int Write(byte[] frame, int offset)
        {
            int count = transport.Write(frame, offset);
            if (count < 0 || count > frame.Length - offset) throw new IOException("Invalid tracker write count.");
            return count;
        }

        private void AcceptReply(TrackerReply reply)
        {
            RevokeIfNeeded();
            var current = run;
            var request = pending;
            if (current == null || request == null || !request.FirstAttempt.HasValue ||
                !request.SentFullFrame || reply.Run != current.Id || reply.Sequence != request.Sequence)
                return;
            long elapsed = Now - request.FirstAttempt.Value;
            if (elapsed >= (request.Retried ? 2 : 1) * AckWindow) return;

            if (reply.Error != null)
            {
                if (reply.Epoch == current.Epoch) RejectRequest(request, request.Command + " rejected: " + reply.Error);
                return;
            }
            if (reply.Command != request.Command || (current.Epoch != null && reply.Epoch != current.Epoch)) return;

            pending = null;
            switch (request.Command)
            {
                case "HELLO":
                    current.Epoch = reply.Epoch;
                    Queue("START", 1);
                    break;
                case "START":
                    SetStatus("Simulator active");
                    break;
                case "STOP":
                    run = null;
                    SetStatus(current.StopStatus);
                    break;
            }
        }

        private void RejectRequest(Request request, string reason)
        {
            if (request.Command == "STOP") Disconnect(reason);
            else Stop(reason);
        }

        private void SetStatus(string value) => Volatile.Write(ref status, value);

        private sealed class Run
        {
            public readonly SessionLiveSegment Segment;
            public readonly string Id;
            public string? Epoch;
            public int LastSequence;
            public long HeartbeatDue;
            public bool Stopping;
            public string StopStatus = "Simulator idle";

            public Run(SessionLiveSegment segment, string id) { Segment = segment; Id = id; }
        }

        private sealed class Request
        {
            public readonly string Command;
            public readonly int Sequence;
            public readonly byte[] Frame;
            public long? FirstAttempt;
            public int Offset;
            public bool Retried, Transmitted, SentFullFrame;

            public Request(string command, int sequence, byte[] frame)
            { Command = command; Sequence = sequence; Frame = frame; }
        }
    }
}
