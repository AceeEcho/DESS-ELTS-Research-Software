#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        if (unityTicks.HasValue && unityTicks.Value < 0) throw new ArgumentOutOfRangeException(nameof(unityTicks));
        if (jetsonTicks.HasValue && jetsonTicks.Value < 0) throw new ArgumentOutOfRangeException(nameof(jetsonTicks));
        if (applicationVersion != null) EltsBlockContext.Required(applicationVersion, nameof(applicationVersion));
        if (reason != null) EltsBlockContext.Required(reason, nameof(reason));
        if (command != null) { EltsBlockContext.Required(command, nameof(command)); ParseCommandWireName(command); }
        if (state.HasValue && !Enum.IsDefined(typeof(EltsLinkState), state.Value)) throw new ArgumentOutOfRangeException(nameof(state));
        if (error != null) EltsBlockContext.Required(error, nameof(error));
        Type = type; Sequence = sequence; UnityMonotonicTicks = unityTicks; JetsonMonotonicTicks = jetsonTicks;
        ApplicationVersion = applicationVersion; BlockContext = blockContext; Reason = reason; AcknowledgedCommand = command;
        State = state; LedPermission = ledPermission; FaceDetected = faceDetected; Error = error;
    }
    public static EltsMessage Command(EltsMessageType type, long sequence, long unityTicks, string? applicationVersion = null,
        EltsBlockContext? blockContext = null, string? reason = null)
    {
        if (!IsCommand(type))
            throw new ArgumentException("Only a command can be created here.", nameof(type));
        if (unityTicks < 0) throw new ArgumentOutOfRangeException(nameof(unityTicks));
        if (type == EltsMessageType.Hello)
        {
            if (applicationVersion == null || blockContext != null || reason != null) throw new ArgumentException("HELLO requires only applicationVersion in addition to the envelope.");
        }
        else if (type == EltsMessageType.Start)
        {
            if (applicationVersion != null || blockContext == null || reason != null) throw new ArgumentException("START requires only block context in addition to the envelope.");
        }
        else if (type == EltsMessageType.Stop)
        {
            if (applicationVersion != null || blockContext != null || reason == null || (reason != "block_end" && reason != "abort" && reason != "operator")) throw new ArgumentException("STOP requires one allowed reason.");
        }
        else if (applicationVersion != null || blockContext != null || reason != null) throw new ArgumentException("This command accepts no payload fields.");
        return new EltsMessage(EltsProtocolV1.Version, type, sequence, unityTicks: unityTicks, applicationVersion: applicationVersion,
            blockContext: blockContext, reason: reason);
    }
    public static EltsMessage Ack(long sequence, EltsMessageType command, long jetsonTicks, EltsLinkState state, bool ledPermission) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.Ack, sequence, jetsonTicks: jetsonTicks, command: CommandWireName(command), state: state, ledPermission: ledPermission);
    public static EltsMessage Nack(long sequence, EltsMessageType command, long jetsonTicks, EltsLinkState state, string error) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.Nack, sequence, jetsonTicks: jetsonTicks, command: CommandWireName(command), state: state, error: EltsBlockContext.Required(error, nameof(error)));
    public static EltsMessage StateChanged(long sequence, long jetsonTicks, EltsLinkState state, bool ledPermission, bool faceDetected, string reason) =>
        new EltsMessage(EltsProtocolV1.Version, EltsMessageType.State, sequence, jetsonTicks: jetsonTicks, state: state, ledPermission: ledPermission, faceDetected: faceDetected, reason: EltsBlockContext.Required(reason, nameof(reason)));
    public static string WireName(EltsMessageType type) => type.ToString().ToUpperInvariant();
    internal static bool IsCommand(EltsMessageType type) => type == EltsMessageType.Hello || type == EltsMessageType.Arm || type == EltsMessageType.Disarm || type == EltsMessageType.Start || type == EltsMessageType.Stop || type == EltsMessageType.Ping || type == EltsMessageType.Status;
    private static string CommandWireName(EltsMessageType type)
    {
        if (!IsCommand(type)) throw new ArgumentException("ACK/NACK command must be a protocol command.", nameof(type));
        return WireName(type);
    }
    private static void ParseCommandWireName(string value)
    {
        foreach (EltsMessageType candidate in Enum.GetValues(typeof(EltsMessageType)))
            if (WireName(candidate) == value && IsCommand(candidate)) return;
        throw new ArgumentException("ACK/NACK command must name a protocol command.", nameof(value));
    }
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
        ValidateJsonSyntax(newlineDelimitedJson);
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
    // Newtonsoft accepts JavaScript extensions. This grammar gate is shared in
    // concept with RecordedReplay: only RFC JSON reaches JObject.Load.
    private static void ValidateJsonSyntax(string text) { int index = 0; ParseJsonValue(text, ref index); SkipWhitespace(text, ref index); if (index != text.Length) throw new InvalidDataException("Trailing JSON content."); }
    private static void ParseJsonValue(string text, ref int index) { SkipWhitespace(text, ref index); if (index >= text.Length) throw new InvalidDataException("Missing JSON value."); switch (text[index]) { case '{': ParseJsonObject(text, ref index); break; case '[': ParseJsonArray(text, ref index); break; case '"': ParseJsonString(text, ref index); break; case 't': Expect(text, ref index, "true"); break; case 'f': Expect(text, ref index, "false"); break; case 'n': Expect(text, ref index, "null"); break; default: if (text[index] == '-' || char.IsDigit(text[index])) ParseJsonNumber(text, ref index); else throw new InvalidDataException("Unsupported JSON syntax."); break; } }
    private static void ParseJsonObject(string text, ref int index) { index++; SkipWhitespace(text, ref index); if (Take(text, ref index, '}')) return; while (true) { SkipWhitespace(text, ref index); if (index >= text.Length || text[index] != '"') throw new InvalidDataException("JSON object key must be quoted."); ParseJsonString(text, ref index); SkipWhitespace(text, ref index); Expect(text, ref index, ":"); ParseJsonValue(text, ref index); SkipWhitespace(text, ref index); if (Take(text, ref index, '}')) return; if (!Take(text, ref index, ',')) throw new InvalidDataException("Invalid JSON object separator."); SkipWhitespace(text, ref index); if (index < text.Length && text[index] == '}') throw new InvalidDataException("Trailing JSON comma."); } }
    private static void ParseJsonArray(string text, ref int index) { index++; SkipWhitespace(text, ref index); if (Take(text, ref index, ']')) return; while (true) { ParseJsonValue(text, ref index); SkipWhitespace(text, ref index); if (Take(text, ref index, ']')) return; if (!Take(text, ref index, ',')) throw new InvalidDataException("Invalid JSON array separator."); SkipWhitespace(text, ref index); if (index < text.Length && text[index] == ']') throw new InvalidDataException("Trailing JSON comma."); } }
    private static void ParseJsonString(string text, ref int index) { if (!Take(text, ref index, '"')) throw new InvalidDataException("JSON string required."); while (index < text.Length) { char c = text[index++]; if (c == '"') return; if (c < 0x20) throw new InvalidDataException("Control character in JSON string."); if (c == '\\') { if (index >= text.Length) break; char escape = text[index++]; if ("\"\\/bfnrt".IndexOf(escape) < 0) { if (escape != 'u' || index + 4 > text.Length || !IsHex(text[index]) || !IsHex(text[index + 1]) || !IsHex(text[index + 2]) || !IsHex(text[index + 3])) throw new InvalidDataException("Invalid JSON escape."); index += 4; } } } throw new InvalidDataException("Unterminated JSON string."); }
    private static void ParseJsonNumber(string text, ref int index) { if (Take(text, ref index, '-') && index >= text.Length) throw new InvalidDataException("Invalid JSON number."); if (Take(text, ref index, '0')) { if (index < text.Length && char.IsDigit(text[index])) throw new InvalidDataException("Leading zero in JSON number."); } else { if (index >= text.Length || text[index] < '1' || text[index] > '9') throw new InvalidDataException("Invalid JSON number."); while (index < text.Length && char.IsDigit(text[index])) index++; } if (Take(text, ref index, '.')) { if (index >= text.Length || !char.IsDigit(text[index])) throw new InvalidDataException("Invalid JSON fraction."); while (index < text.Length && char.IsDigit(text[index])) index++; } if (index < text.Length && (text[index] == 'e' || text[index] == 'E')) { index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++; if (index >= text.Length || !char.IsDigit(text[index])) throw new InvalidDataException("Invalid JSON exponent."); while (index < text.Length && char.IsDigit(text[index])) index++; } }
    private static void SkipWhitespace(string text, ref int index) { while (index < text.Length && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r' || text[index] == '\n')) index++; }
    private static bool Take(string text, ref int index, char value) { if (index < text.Length && text[index] == value) { index++; return true; } return false; }
    private static void Expect(string text, ref int index, string value) { if (index + value.Length > text.Length || String.CompareOrdinal(text, index, value, 0, value.Length) != 0) throw new InvalidDataException("Invalid JSON token."); index += value.Length; }
    private static bool IsHex(char value) => (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f') || (value >= 'A' && value <= 'F');

    private static EltsMessage DecodeByType(JObject value, string version, EltsMessageType type, long sequence)
    {
        switch (type)
        {
            case EltsMessageType.Hello: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "applicationVersion"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), applicationVersion: Text(value, "applicationVersion"));
            case EltsMessageType.Arm: case EltsMessageType.Disarm: case EltsMessageType.Ping: case EltsMessageType.Status: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"));
            case EltsMessageType.Start: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "participantPseudonym", "block", "condition", "durationTicks"); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), blockContext: new EltsBlockContext(Text(value, "participantPseudonym"), Text(value, "block"), Text(value, "condition"), PositiveInteger(value, "durationTicks")));
            case EltsMessageType.Stop: Fields(value, "protocolVersion", "messageType", "sequence", "unityMonotonicTicks", "reason"); var reason = Text(value, "reason"); if (reason != "block_end" && reason != "abort" && reason != "operator") throw new InvalidDataException("Unsupported STOP reason."); return new EltsMessage(version, type, sequence, unityTicks: Integer(value, "unityMonotonicTicks"), reason: reason);
            case EltsMessageType.Ack: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "command", "state", "ledPermission"); return EltsMessage.Ack(sequence, ParseCommandType(Text(value, "command")), Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Bool(value, "ledPermission"));
            case EltsMessageType.Nack: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "command", "state", "error"); return EltsMessage.Nack(sequence, ParseCommandType(Text(value, "command")), Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Text(value, "error"));
            case EltsMessageType.State: Fields(value, "protocolVersion", "messageType", "sequence", "jetsonMonotonicTicks", "state", "ledPermission", "faceDetected", "reason"); return EltsMessage.StateChanged(sequence, Integer(value, "jetsonMonotonicTicks"), ParseState(Text(value, "state")), Bool(value, "ledPermission"), Bool(value, "faceDetected"), Text(value, "reason"));
            default: throw new InvalidDataException("Unsupported messageType.");
        }
    }
    private static void Fields(JObject value, params string[] names) { if (value.Properties().Count() != names.Length || names.Any(n => value[n] == null)) throw new InvalidDataException("Unknown or missing protocol fields."); }
    private static string Text(JObject value, string key) { if (value[key]?.Type != JTokenType.String) throw new InvalidDataException("Expected string: " + key); return EltsBlockContext.Required((string)value[key]!, key); }
    private static long Integer(JObject value, string key) { if (value[key]?.Type != JTokenType.Integer) throw new InvalidDataException("Expected integer ticks/sequence: " + key); var result = (long)value[key]!; if (result < 0) throw new InvalidDataException("Negative integer: " + key); return result; }
    private static long PositiveInteger(JObject value, string key) { var result = Integer(value, key); if (result == 0) throw new InvalidDataException("Positive integer required: " + key); return result; }
    private static bool Bool(JObject value, string key) { if (value[key]?.Type != JTokenType.Boolean) throw new InvalidDataException("Expected bool: " + key); return (bool)value[key]!; }
    private static EltsMessageType ParseType(string value) { try { return ParseTypeName(value); } catch (ArgumentException ex) { throw new InvalidDataException("Unsupported messageType.", ex); } }
    private static EltsMessageType ParseCommandType(string value) { var type = ParseType(value); if (!EltsMessage.IsCommand(type)) throw new InvalidDataException("ACK/NACK command must be a protocol command."); return type; }
    private static EltsMessageType ParseTypeName(string value) { foreach (EltsMessageType candidate in Enum.GetValues(typeof(EltsMessageType))) if (EltsMessage.WireName(candidate) == value) return candidate; throw new ArgumentException("Unsupported message type.", nameof(value)); }
    private static EltsLinkState ParseState(string value) { foreach (EltsLinkState candidate in Enum.GetValues(typeof(EltsLinkState))) if (candidate.ToString() == value) return candidate; throw new InvalidDataException("Unsupported state."); }
}
}
