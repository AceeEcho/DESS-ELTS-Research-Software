#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.EltsLink
{
    /// <summary>One explicitly supplied byte stream. Calls belong to the I/O worker and must not block.</summary>
    public interface ITrackerByteTransport
    {
        bool TryOpen();
        // Return zero when no bytes can be transferred yet. Report EOF or link loss as an IOException.
        int Read(byte[] buffer);
        int Write(byte[] frame, int offset);
        void Close();
    }

    public sealed class TrackerReply
    {
        public string? Command { get; }
        public string? Error { get; }
        public string? Run { get; }
        public string? Epoch { get; }
        public int? Sequence { get; }

        internal TrackerReply(string? command, string? error, string? run, string? epoch, int? sequence)
        {
            Command = command;
            Error = error;
            Run = run;
            Epoch = epoch;
            Sequence = sequence;
        }
    }

    /// <summary>Wire contract for the isolated tracker, independent of the simulated ELTS controller.</summary>
    public sealed class TrackerProtocol
    {
        private const int MaxFrameBytes = 512;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly byte[] line = new byte[MaxFrameBytes];
        private int length;
        private bool discard;

        public static bool AllowsTracking(string? condition) => condition == "WE_FT" || condition == "WE_MT";

        public static byte[] Encode(string command, string run, string? epoch, int sequence, string? condition = null)
        {
            var frame = new JObject { ["v"] = 1, ["type"] = command, ["run"] = run, ["seq"] = sequence };
            if (epoch != null) frame["epoch"] = epoch;
            if (condition != null) frame["condition"] = condition;
            return Utf8.GetBytes(frame.ToString(Formatting.None) + "\n");
        }

        public void Reset() { length = 0; discard = false; }

        public void Read(byte[] bytes, int count, Action<TrackerReply> onReply)
        {
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[i];
                if (value == (byte)'\n')
                {
                    var reply = discard ? null : Decode();
                    Reset();
                    if (reply != null) onReply(reply);
                }
                else if (length == line.Length || value == (byte)'\r') discard = true;
                else if (!discard) line[length++] = value;
            }
        }

        private TrackerReply? Decode()
        {
            try
            {
                var value = EltsCodec.ParseStrictJsonObject(Utf8.GetString(line, 0, length));
                if (Integer(value["v"]) != 1) return null;
                var run = Text(value["run"]);
                var epoch = Text(value["epoch"]);
                var sequence = Integer(value["seq"]);
                if (Text(value["type"]) == "ACK" && Fields(value, "v", "type", "cmd", "run", "epoch", "seq", "state")
                    && IsId(run) && IsId(epoch) && sequence.HasValue)
                {
                    var command = Text(value["cmd"]);
                    var state = Text(value["state"]);
                    string? expected = command switch
                    {
                        "HELLO" or "STOP" => "IDLE",
                        "START" or "HEARTBEAT" => "ACTIVE",
                        _ => null
                    };
                    if (expected != null && state == expected)
                        return new TrackerReply(command, null, run, epoch, sequence);
                }
                if (Text(value["type"]) == "ERROR" && Fields(value, "v", "type", "code", "run", "epoch", "seq")
                    && (IsId(run) || value["run"]!.Type == JTokenType.Null)
                    && (IsId(epoch) || value["epoch"]!.Type == JTokenType.Null)
                    && (sequence.HasValue || value["seq"]!.Type == JTokenType.Null))
                {
                    var code = Text(value["code"]);
                    if (code is "BAD_FRAME" or "BAD_VERSION" or "BAD_COMMAND" or "BUSY" or "STALE"
                        or "BAD_SEQUENCE" or "NOT_ACTIVE" or "APPLY_FAILED")
                        return new TrackerReply(null, code, run, epoch, sequence);
                }
            }
            catch (InvalidDataException) { }
            catch (DecoderFallbackException) { }
            return null;
        }

        private static bool Fields(JObject value, params string[] keys) =>
            value.Count == keys.Length && keys.All(key => value[key] != null);

        private static string? Text(JToken? token) => token?.Type == JTokenType.String ? (string?)token : null;

        private static int? Integer(JToken? token) => token?.Type == JTokenType.Integer &&
            int.TryParse(token.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out int result)
                ? result : null;

        private static bool IsId(string? text) => text != null && text.Length == 32 &&
            text.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}
