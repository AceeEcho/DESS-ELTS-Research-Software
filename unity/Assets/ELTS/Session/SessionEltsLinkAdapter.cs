#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.EltsLink;
using Elts.Logging;
using Elts.Scenario;

namespace Elts.Session
{
    /// <summary>Development-only NE handling. This is a fixture policy, not a D-10 study decision.</summary>
    public enum DevelopmentNeLinkMode { Idle, Armed }

    /// <summary>Bounded retry settings for the in-process synthetic protocol adapter.</summary>
    public sealed class SessionEltsLinkOptions
    {
        public const int DefaultMaximumCommandAttempts = 2;
        public SessionEltsLinkOptions(DevelopmentNeLinkMode neMode = DevelopmentNeLinkMode.Idle, int maximumCommandAttempts = DefaultMaximumCommandAttempts, string applicationVersion = "elts-session-development")
        {
            if (!Enum.IsDefined(typeof(DevelopmentNeLinkMode), neMode)) throw new ArgumentOutOfRangeException(nameof(neMode));
            if (maximumCommandAttempts < 1 || maximumCommandAttempts > 8) throw new ArgumentOutOfRangeException(nameof(maximumCommandAttempts));
            if (String.IsNullOrWhiteSpace(applicationVersion)) throw new ArgumentException("An application version is required.", nameof(applicationVersion));
            NeMode = neMode; MaximumCommandAttempts = maximumCommandAttempts; ApplicationVersion = applicationVersion;
        }
        public DevelopmentNeLinkMode NeMode { get; }
        public int MaximumCommandAttempts { get; }
        public string ApplicationVersion { get; }
    }

    /// <summary>
    /// Pure adapter from the existing synthetic IEltsLink protocol to ISessionLink.
    /// It records typed command, reply, state, offset, and loss events through the
    /// supplied shared clock and sequence. It never opens a device connection.
    /// </summary>
    public sealed class SessionEltsLinkAdapter : ISessionConditionLink
    {
        private readonly ISharedClock clock;
        private readonly ISessionEventSequence sequence;
        private readonly ISessionEventSink sink;
        private readonly IEltsLink link;
        private readonly SessionEltsLinkOptions options;
        private long nextCommandSequence = 1;
        private SessionBlockLinkContext? context;
        private bool weBlock;
        private bool failed;
        private string? failedBlockId;

        public SessionEltsLinkAdapter(ISharedClock clock, ISessionEventSequence sequence, ISessionEventSink sink, IEltsLink link, SessionEltsLinkOptions? options = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.link = link ?? throw new ArgumentNullException(nameof(link));
            this.options = options ?? new SessionEltsLinkOptions();
        }

        public bool PrepareBlock(SessionBlockLinkContext value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (failed && String.Equals(value.BlockId, failedBlockId, StringComparison.Ordinal)) return false;
            if (failed) { failed = false; failedBlockId = null; }
            context = value ?? throw new ArgumentNullException(nameof(value));
            weBlock = value.Condition.StartsWith("WE_", StringComparison.Ordinal);
            if (!Record("EltsState", LogField.String("operation", "prepare"), LogField.String("blockId", value.BlockId), LogField.String("condition", value.Condition), LogField.String("neMode", options.NeMode.ToString()), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission))) return false;
            if (weBlock) return EnsureHelloAndArm();
            if (options.NeMode == DevelopmentNeLinkMode.Idle) return Send(EltsMessageType.Disarm, null, "operator");
            return EnsureHelloAndArm();
        }

        public bool TrySetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (failed || !weBlock || context == null || !EnsureHelloAndArm()) return false;
                return Send(EltsMessageType.Start, context, null);
            }
            if (failed) { BestEffortStop(); return false; }
            bool stopped = Send(EltsMessageType.Stop, null, "block_end");
            // Logging can fail after START. Do not let that failure prevent the
            // engine's terminal cleanup from sending a de-permission command.
            if (!stopped && failed) BestEffortStop();
            return stopped;
        }

        public bool Tick()
        {
            if (failed) return false;
            try { link.Advance(); }
            catch (Exception e) { return Loss("advance", e); }
            if (!weBlock) return ObserveNe();
            if (link.State != EltsLinkState.Active || !link.LedPermission) return Loss("active_permission_lost", null);
            return Send(EltsMessageType.Ping, null, null) && Observe("heartbeat");
        }

        private bool EnsureHelloAndArm()
        {
            if (link.State == EltsLinkState.Active && link.LedPermission) return true;
            if (link.State == EltsLinkState.Armed) return true;
            return Send(EltsMessageType.Hello, null, null) && Send(EltsMessageType.Arm, null, null);
        }

        private void BestEffortStop()
        {
            try
            {
                var command = EltsMessage.Command(EltsMessageType.Stop, nextCommandSequence++, clock.Now.Ticks, reason: "block_end");
                link.Send(command);
            }
            catch { }
        }

        private bool Send(EltsMessageType type, SessionBlockLinkContext? start, string? reason)
        {
            long wireSequence = nextCommandSequence++;
            long commandUnityTicks = clock.Now.Ticks;
            EltsMessage command = EltsMessage.Command(type, wireSequence, commandUnityTicks,
                type == EltsMessageType.Hello ? options.ApplicationVersion : null,
                type == EltsMessageType.Start ? new EltsBlockContext(start!.ParticipantPseudonym, start.BlockId, start.Condition, start.DurationTicks) : null,
                type == EltsMessageType.Stop ? reason : null);
            for (int attempt = 1; attempt <= options.MaximumCommandAttempts; attempt++)
            {
                if (!Record("EltsCommand", LogField.String("command", EltsMessage.WireName(type)), LogField.NumberValue("commandSequence", wireSequence), LogField.NumberValue("attempt", attempt), LogField.NumberValue("unityTicks", commandUnityTicks))) return false;
                try
                {
                    EltsMessage reply = link.Send(command);
                    if (!RecordReply(reply, attempt)) return false;
                    if (IsExpectedAck(type, wireSequence, reply))
                    {
                        return Record("EltsState", LogField.String("operation", "ack"), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission));
                    }
                    return Loss("nack_or_invalid_reply", null);
                }
                catch (Exception e) { if (attempt == options.MaximumCommandAttempts) return Loss("send", e); }
            }
            return false;
        }

        private bool RecordReply(EltsMessage reply, int attempt)
        {
            string type = reply.Type == EltsMessageType.Ack ? "EltsAck" : reply.Type == EltsMessageType.Nack ? "EltsNack" : "EltsState";
            var fields = new List<LogField> { LogField.String("reply", EltsMessage.WireName(reply.Type)), LogField.NumberValue("commandSequence", reply.Sequence), LogField.NumberValue("attempt", attempt), LogField.String("state", reply.State?.ToString() ?? "unknown") };
            if (reply.LedPermission.HasValue) fields.Add(LogField.Boolean("ledPermission", reply.LedPermission.Value));
            if (reply.AcknowledgedCommand != null) fields.Add(LogField.String("command", reply.AcknowledgedCommand));
            if (reply.Error != null) fields.Add(LogField.String("error", reply.Error));
            if (reply.JetsonMonotonicTicks.HasValue)
            {
                long observedUnityTicks = clock.Now.Ticks;
                fields.Add(LogField.NumberValue("jetsonTicks", reply.JetsonMonotonicTicks.Value));
                if (!Record("EltsOffset", LogField.NumberValue("commandSequence", reply.Sequence), LogField.NumberValue("observedUnityTicks", observedUnityTicks), LogField.NumberValue("jetsonTicks", reply.JetsonMonotonicTicks.Value), LogField.NumberValue("endpointMinusObservedUnityTicks", reply.JetsonMonotonicTicks.Value - observedUnityTicks))) return false;
            }
            return Record(type, fields.ToArray());
        }

        private bool IsExpectedAck(EltsMessageType command, long wireSequence, EltsMessage reply)
        {
            if (reply.Type != EltsMessageType.Ack || reply.Sequence != wireSequence || reply.AcknowledgedCommand != EltsMessage.WireName(command) ||
                !reply.State.HasValue || !reply.LedPermission.HasValue || reply.State.Value != link.State || reply.LedPermission.Value != link.LedPermission) return false;
            if (command == EltsMessageType.Hello) return reply.State == EltsLinkState.Idle && !reply.LedPermission.Value;
            if (command == EltsMessageType.Arm) return reply.State == EltsLinkState.Armed && !reply.LedPermission.Value;
            if (command == EltsMessageType.Start || command == EltsMessageType.Ping) return reply.State == EltsLinkState.Active && reply.LedPermission.Value;
            if (command == EltsMessageType.Stop) return reply.State != EltsLinkState.Active && !reply.LedPermission.Value;
            if (command == EltsMessageType.Disarm) return reply.State == EltsLinkState.Idle && !reply.LedPermission.Value;
            return false;
        }

        private bool Observe(string operation)
        {
            return Record("EltsState", LogField.String("operation", operation), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission)) &&
                (link.State == EltsLinkState.Active && link.LedPermission);
        }
        private bool ObserveNe()
        {
            bool expected = !link.LedPermission && (options.NeMode == DevelopmentNeLinkMode.Idle ? link.State == EltsLinkState.Idle : link.State == EltsLinkState.Armed);
            if (!Record("EltsState", LogField.String("operation", "ne_tick"), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission))) return false;
            return expected || Loss("ne_state_or_permission_changed", null);
        }
        private bool Loss(string operation, Exception? error)
        {
            failed = true; failedBlockId = context?.BlockId;
            Record("EltsLinkLoss", LogField.String("operation", operation), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission), LogField.String("detail", error == null ? "protocol_or_permission_failure" : error.GetType().Name));
            return false;
        }
        private bool Record(string type, params LogField[] fields)
        {
            try
            {
                if (sink.TryRecord(new SessionEvent(sequence.Next(), clock.Now, type, fields))) return true;
            }
            catch { }
            failed = true; failedBlockId = context?.BlockId;
            return false;
        }
    }
}
