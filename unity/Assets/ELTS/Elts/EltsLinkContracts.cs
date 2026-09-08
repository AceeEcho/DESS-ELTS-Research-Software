#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Elts.Clock;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.EltsLink
{
/// <summary>
/// Development-only JSON protocol for fixtures. It is derived from specification
/// section 11's message table, but is deliberately not claimed compatible with
/// the built ELTS controller. A real adapter requires D-03 and controller-owner
/// review before it may exist.
/// </summary>
public static class EltsProtocolV1
{
    public const string Version = "development-synthetic-v1";
    public const int MaximumLineCharacters = 4096;
}

public enum EltsLinkState { Idle, Armed, Active }
public enum EltsMessageType { Hello, Arm, Disarm, Start, Stop, Ping, Status, Ack, Nack, State }

/// <summary>Immutable, synthetic-only timing settings. Values use TimeSpan ticks (100 ns), never UTC or floating seconds.</summary>
public sealed class EltsLinkRuntimeOptions
{
    public long HeartbeatTimeoutTicks { get; }
    public long MaximumOnTimeExtraTicks { get; }
    public int ReplayCacheCapacity { get; }
    public EltsLinkRuntimeOptions(long heartbeatTimeoutTicks = 3 * TimeSpan.TicksPerSecond,
        long maximumOnTimeExtraTicks = 30 * TimeSpan.TicksPerSecond, int replayCacheCapacity = 128)
    {
        if (heartbeatTimeoutTicks <= 0) throw new ArgumentOutOfRangeException(nameof(heartbeatTimeoutTicks));
        if (maximumOnTimeExtraTicks < 0) throw new ArgumentOutOfRangeException(nameof(maximumOnTimeExtraTicks));
        if (replayCacheCapacity < 1 || replayCacheCapacity > 4096) throw new ArgumentOutOfRangeException(nameof(replayCacheCapacity));
        HeartbeatTimeoutTicks = heartbeatTimeoutTicks;
        MaximumOnTimeExtraTicks = maximumOnTimeExtraTicks;
        ReplayCacheCapacity = replayCacheCapacity;
    }
}

public sealed class EltsBlockContext
{
    public string ParticipantPseudonym { get; }
    public string Block { get; }
    public string Condition { get; }
    public long DurationTicks { get; }
    public EltsBlockContext(string participantPseudonym, string block, string condition, long durationTicks)
    {
        ParticipantPseudonym = Required(participantPseudonym, nameof(participantPseudonym));
        Block = Required(block, nameof(block));
        Condition = Required(condition, nameof(condition));
        if (durationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
        DurationTicks = durationTicks;
    }
    internal static string Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("A bounded nonempty value is required.", name);
        return value;
    }
}

/// <summary>Typed wire record. All timestamp fields are monotonic TimeSpan ticks measured at their local endpoint.</summary>
public sealed class EltsMessage
{
    public string ProtocolVersion { get; }
    public EltsMessageType Type { get; }
    public long Sequence { get; }
    public long? UnityMonotonicTicks { get; }
    public long? JetsonMonotonicTicks { get; }
    public string? ApplicationVersion { get; }
    public EltsBlockContext? BlockContext { get; }
    public string? Reason { get; }
    public string? AcknowledgedCommand { get; }
    public EltsLinkState? State { get; }
    public bool? LedPermission { get; }
    public bool? FaceDetected { get; }
    public string? Error { get; }

    internal EltsMessage(string protocolVersion, EltsMessageType type, long sequence, long? unityTicks = null,
        long? jetsonTicks = null, string? applicationVersion = null, EltsBlockContext? blockContext = null,
        string? reason = null, string? command = null, EltsLinkState? state = null, bool? ledPermission = null,
        bool? faceDetected = null, string? error = null)
    {
        ProtocolVersion = EltsBlockContext.Required(protocolVersion, nameof(protocolVersion));
        if (!Enum.IsDefined(typeof(EltsMessageType), type)) throw new ArgumentOutOfRangeException(nameof(type));
        if (sequence < 0) throw new ArgumentOutOfRangeException(nameof(sequence));
        Type = type; Sequence = sequence; UnityMonotonicTicks = unityTicks; JetsonMonotonicTicks = jetsonTicks;
        ApplicationVersion = applicationVersion; BlockContext = blockContext; Reason = reason; AcknowledgedCommand = command;
        State = state; LedPermission = ledPermission; FaceDetected = faceDetected; Error = error;
    }
    public static EltsMessage Command(EltsMessageType type, long sequence, long unityTicks, string? applicationVersion = null,
        EltsBlockContext? blockContext = null, string? reason = null)
    {
        if (type != EltsMessageType.Hello && type != EltsMessageType.Arm && type != EltsMessageType.Disarm &&
            type != EltsMessageType.Start && type != EltsMessageType.Stop && type != EltsMessageType.Ping && type != EltsMessageType.Status)
            throw new ArgumentException("Only a command can be created here.", nameof(type));
        if (unityTicks < 0) throw new ArgumentOutOfRangeException(nameof(unityTicks));
        return new EltsMessage(EltsProtocolV1.Version, type, sequence, unityTicks: unityTicks, applicationVersion: applicationVersion,
            blockContext: blockContext, reason: reason);
    }
    public static EltsMessage Ack(long sequence, EltsMessageType command, long jetsonTicks, EltsLinkState state, bool ledPermission) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.Ack, sequence, jetsonTicks: jetsonTicks, command: WireName(command), state: state, ledPermission: ledPermission);
    public static EltsMessage Nack(long sequence, EltsMessageType command, long jetsonTicks, EltsLinkState state, string error) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.Nack, sequence, jetsonTicks: jetsonTicks, command: WireName(command), state: state, error: EltsBlockContext.Required(error, nameof(error)));
    public static EltsMessage StateChanged(long sequence, long jetsonTicks, EltsLinkState state, bool ledPermission, bool faceDetected, string reason) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.State, sequence, jetsonTicks: jetsonTicks, state: state, ledPermission: ledPermission, faceDetected: faceDetected, reason: EltsBlockContext.Required(reason, nameof(reason)));
    public static string WireName(EltsMessageType type) => type.ToString().ToUpperInvariant();
    public string Fingerprint => EltsCodec.Encode(this);
}

/// <summary>Strict newline-delimited JSON codec. Unknown, duplicate, missing, mistyped, and extra fields are rejected.</summary>
public static class EltsCodec
{
    public static string Encode(EltsMessage message)
    {
        if (message == null) throw new ArgumentNullException(nameof(message));
        var value = new JObject { ["protocolVersion"] = message.ProtocolVersion, ["messageType"] = EltsMessage.WireName(message.Type), ["sequence"] = message.Sequence };
        if (message.UnityMonotonicTicks.HasValue) value["unityMonotonicTicks"] = message.UnityMonotonicTicks.Value;
        if (message.JetsonMonotonicTicks.HasValue) value["jetsonMonotonicTicks"] = message.JetsonMonotonicTicks.Value;
        if (message.ApplicationVersion != null) value["applicationVersion"] = message.ApplicationVersion;
        if (message.BlockContext != null) { value["participantPseudonym"] = message.BlockContext.ParticipantPseudonym; value["block"] = message.BlockContext.Block; value["condition"] = message.BlockContext.Condition; value["durationTicks"] = message.BlockContext.DurationTicks; }
        if (message.Reason != null) value["reason"] = message.Reason;
        if (message.AcknowledgedCommand != null) value["command"] = message.AcknowledgedCommand;
        if (message.State.HasValue) value["state"] = message.State.Value.ToString();
        if (message.LedPermission.HasValue) value["ledPermission"] = message.LedPermission.Value;
        if (message.FaceDetected.HasValue) value["faceDetected"] = message.FaceDetected.Value;
        if (message.Error != null) value["error"] = message.Error;
        return value.ToString(Formatting.None) + "\n";
    }

    public static EltsMessage Decode(string newlineDelimitedJson)
    {
        if (string.IsNullOrEmpty(newlineDelimitedJson) || newlineDelimitedJson.Length > EltsProtocolV1.MaximumLineCharacters ||
            !newlineDelimitedJson.EndsWith("\n", StringComparison.Ordinal) || newlineDelimitedJson.IndexOf('\n') != newlineDelimitedJson.Length - 1 || newlineDelimitedJson.IndexOf('\r') >= 0)
            throw new InvalidDataException("Protocol input must be one bounded LF-terminated JSON object.");
        EnsureStrictJsonSyntax(newlineDelimitedJson);
        JObject value;
        try
        {
            using var reader = new JsonTextReader(new StringReader(newlineDelimitedJson)) { DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Decimal };
            value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error, CommentHandling = CommentHandling.Load });
            if (reader.Read()) throw new InvalidDataException("Trailing JSON tokens are not allowed.");
        }
        catch (JsonException ex) { throw new InvalidDataException("Malformed protocol JSON.", ex); }
        var protocolVersion = Text(value, "protocolVersion");
        if (protocolVersion != EltsProtocolV1.Version) throw new InvalidDataException("Unsupported protocolVersion.");
        var type = ParseType(Text(value, "messageType"));
        var sequence = Integer(value, "sequence");
        return DecodeByType(value, protocolVersion, type, sequence);
    }
    // Newtonsoft permits JavaScript-style property names and comments. Masking
    // strings first lets this small preflight reject those extensions without
    // mistaking text inside a valid JSON string for syntax.
    private static void EnsureStrictJsonSyntax(string value)
    {
        var outside = new char[value.Length];
        bool inString = false, escaped = false;
        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            outside[i] = inString ? ' ' : current;
            if (inString)
            {
                if (escaped) escaped = false;
                else if (current == '\\') escaped = true;
                else if (current == '"') inString = false;
            }
            else if (current == '"') { inString = true; outside[i] = ' '; }
        }
        if (inString || escaped) throw new InvalidDataException("Unterminated JSON string.");
        var syntax = new string(outside);
        if (syntax.Contains("//", StringComparison.Ordinal) || syntax.Contains("/*", StringComparison.Ordinal) ||
            Regex.IsMatch(syntax, @"[\{,]\s*[A-Za-z_$]"))
            throw new InvalidDataException("Only canonical JSON with quoted fields and no comments is allowed.");
    }

    private static EltsMessage DecodeByType(JObject value, string version, EltsMessageType type, long sequence)
    {
        switch (type)
        {
            case EltsMessageType.Hello: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "applicationVersion"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), applicationVersion: Text(value, "applicationVersion"));
            case EltsMessageType.Arm: case EltsMessageType.Disarm: case EltsMessageType.Ping: case EltsMessageType.Status: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"));
            case EltsMessageType.Start: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "participantPseudonym", "block", "condition", "durationTicks"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), blockContext: new EltsBlockContext(Text(value, "participantPseudonym"), Text(value, "block"), Text(value, "condition"), PositiveInteger(value, "durationTicks")));
            case EltsMessageType.Stop: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "reason"); var reason = Text(value, "reason"); if (reason != "block_end" && reason != "abort" && reason != "operator") throw new InvalidDataException("Unsupported STOP reason."); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), reason: reason);
            case EltsMessageType.Ack: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "command", "state", "ledPermission"); return EltsMessage.Ack(sequence, ParseType(Text(value, "command")), Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Bool(value, "ledPermission"));
            case EltsMessageType.Nack: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "command", "state", "error"); return EltsMessage.Nack(sequence, ParseType(Text(value, "command")), Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Text(value, "error"));
            case EltsMessageType.State: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "state", "ledPermission", "faceDetected", "reason"); return EltsMessage.StateChanged(sequence, Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Bool(value, "ledPermission"), Bool(value, "faceDetected"), Text(value, "reason"));
            default: throw new InvalidDataException("Unsupported messageType.");
        }
    }
    private static void Fields(JObject value, params string[] names) { if (value.Properties().Count() != names.Length || names.Any(n => value[n] == null)) throw new InvalidDataException("Unknown or missing protocol fields."); }
    private static string Text(JObject value, string key) { if (value[key]?.Type != JTokenType.String) throw new InvalidDataException("Expected string: " + key); return EltsBlockContext.Required((string)value[key]!, key); }
    private static long Integer(JObject value, string key) { if (value[key]?.Type != JTokenType.Integer) throw new InvalidDataException("Expected integer ticks/sequence: " + key); var result = (long)value[key]!; if (result < 0) throw new InvalidDataException("Negative integer: " + key); return result; }
    private static long PositiveInteger(JObject value, string key) { var result = Integer(value, key); if (result == 0) throw new InvalidDataException("Positive integer required: " + key); return result; }
    private static bool Bool(JObject value, string key) { if (value[key]?.Type != JTokenType.Boolean) throw new InvalidDataException("Expected bool: " + key); return (bool)value[key]!; }
    private static EltsMessageType ParseType(string value) { foreach (EltsMessageType candidate in Enum.GetValues(typeof(EltsMessageType))) if (EltsMessage.WireName(candidate) == value) return candidate; throw new InvalidDataException("Unsupported messageType."); }
    private static EltsLinkState ParseState(string value) { if (Enum.TryParse<EltsLinkState>(value, false, out var state)) return state; throw new InvalidDataException("Unsupported state."); }
}
}
