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
            context = value ?? throw new ArgumentNullException(nameof(value));
            weBlock = value.Condition.StartsWith("WE_", StringComparison.Ordinal);
            Record("EltsState", LogField.String("operation", "prepare"), LogField.String("blockId", value.BlockId), LogField.String("condition", value.Condition), LogField.String("neMode", options.NeMode.ToString()), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission));
            if (weBlock) return EnsureHelloAndArm();
            if (options.NeMode == DevelopmentNeLinkMode.Idle) return Send(EltsMessageType.Disarm, null, "operator");
            return EnsureHelloAndArm();
        }

        public bool TrySetEnabled(bool enabled)
        {
            if (enabled)
            {
                if (!weBlock || context == null || !EnsureHelloAndArm()) return false;
                return Send(EltsMessageType.Start, context, null);
            }
            return Send(EltsMessageType.Stop, null, "block_end");
        }

        public bool Tick()
        {
            try { link.Advance(); }
            catch (Exception e) { return Loss("advance", e); }
            if (!weBlock) return Observe("ne_tick");
            if (link.State != EltsLinkState.Active || !link.LedPermission) return Loss("active_permission_lost", null);
            return Send(EltsMessageType.Ping, null, null) && Observe("heartbeat");
        }

        private bool EnsureHelloAndArm()
        {
            if (link.State == EltsLinkState.Active && link.LedPermission) return true;
            if (link.State == EltsLinkState.Armed) return true;
            return Send(EltsMessageType.Hello, null, null) && Send(EltsMessageType.Arm, null, null);
        }

        private bool Send(EltsMessageType type, SessionBlockLinkContext? start, string? reason)
        {
            long wireSequence = nextCommandSequence++;
            EltsMessage command = EltsMessage.Command(type, wireSequence, clock.Now.Ticks,
                type == EltsMessageType.Hello ? options.ApplicationVersion : null,
                type == EltsMessageType.Start ? new EltsBlockContext(start!.ParticipantPseudonym, start.BlockId, start.Condition, start.DurationTicks) : null,
                type == EltsMessageType.Stop ? reason : null);
            for (int attempt = 1; attempt <= options.MaximumCommandAttempts; attempt++)
            {
                Record("EltsCommand", LogField.String("command", EltsMessage.WireName(type)), LogField.NumberValue("commandSequence", wireSequence), LogField.NumberValue("attempt", attempt), LogField.NumberValue("unityTicks", command.UnityMonotonicTicks!.Value));
                try
                {
                    EltsMessage reply = link.Send(command);
                    RecordReply(reply, attempt);
                    if (reply.Type == EltsMessageType.Ack && reply.AcknowledgedCommand == EltsMessage.WireName(type))
                    {
                        Record("EltsState", LogField.String("operation", "ack"), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission));
                        return true;
                    }
                    return Loss("nack_or_invalid_reply", null);
                }
                catch (Exception e) { if (attempt == options.MaximumCommandAttempts) return Loss("send", e); }
            }
            return false;
        }

        private void RecordReply(EltsMessage reply, int attempt)
        {
            string type = reply.Type == EltsMessageType.Ack ? "EltsAck" : reply.Type == EltsMessageType.Nack ? "EltsNack" : "EltsState";
            var fields = new List<LogField> { LogField.String("reply", EltsMessage.WireName(reply.Type)), LogField.NumberValue("commandSequence", reply.Sequence), LogField.NumberValue("attempt", attempt), LogField.String("state", reply.State?.ToString() ?? "unknown") };
            if (reply.LedPermission.HasValue) fields.Add(LogField.Boolean("ledPermission", reply.LedPermission.Value));
            if (reply.AcknowledgedCommand != null) fields.Add(LogField.String("command", reply.AcknowledgedCommand));
            if (reply.Error != null) fields.Add(LogField.String("error", reply.Error));
            if (reply.JetsonMonotonicTicks.HasValue)
            {
                fields.Add(LogField.NumberValue("jetsonTicks", reply.JetsonMonotonicTicks.Value));
                Record("EltsOffset", LogField.NumberValue("commandSequence", reply.Sequence), LogField.NumberValue("unityTicks", clock.Now.Ticks), LogField.NumberValue("jetsonTicks", reply.JetsonMonotonicTicks.Value), LogField.NumberValue("endpointMinusUnityTicks", reply.JetsonMonotonicTicks.Value - clock.Now.Ticks));
            }
            Record(type, fields.ToArray());
        }

        private bool Observe(string operation)
        {
            Record("EltsState", LogField.String("operation", operation), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission));
            return !weBlock || (link.State == EltsLinkState.Active && link.LedPermission);
        }
        private bool Loss(string operation, Exception? error)
        {
            Record("EltsLinkLoss", LogField.String("operation", operation), LogField.String("state", link.State.ToString()), LogField.Boolean("ledPermission", link.LedPermission), LogField.String("detail", error == null ? "protocol_or_permission_failure" : error.GetType().Name));
            return false;
        }
        private void Record(string type, params LogField[] fields) { sink.TryRecord(new SessionEvent(sequence.Next(), clock.Now, type, fields)); }
    }
}
