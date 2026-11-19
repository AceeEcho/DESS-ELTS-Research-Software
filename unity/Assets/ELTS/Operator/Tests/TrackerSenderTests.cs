#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Elts.Clock;
using Elts.EltsLink;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Elts.Operator.Tests
{
    public sealed class TrackerSenderTests
    {
        [TestCase("WE_FT", true)]
        [TestCase("WE_MT", true)]
        [TestCase("NE_FT", false)]
        [TestCase("NE_MT", false)]
        public void OnlyCommittedWeEntrySendsStart(string condition, bool allowed)
        {
            var f = new Fixture(condition);
            Assert.That(f.Peer.Frames, Is.Empty, "Setup, practice and ready are idle");
            f.Start();
            Assert.That(f.Types, Is.EqualTo(allowed ? new[] { "HELLO", "START" } : Array.Empty<string>()));
            if (!allowed) return;
            Assert.That(f.Peer.EnableCount, Is.EqualTo(1));
            var hello = f.Frame(0);
            var start = f.Frame(1);
            Assert.That(start.Properties().Select(key => key.Name),
                Is.EquivalentTo(new[] { "v", "type", "run", "epoch", "seq", "condition" }));
            Assert.That((string?)start["condition"], Is.EqualTo(condition));
            Assert.That((string?)hello["run"], Does.Match("^[0-9a-f]{32}$"));
            Assert.That((string?)start["run"], Is.EqualTo((string?)hello["run"]));
        }

        [TestCase("WE_UNKNOWN")]
        [TestCase("we_ft")]
        [TestCase("")]
        public void UnknownConditionsAreExcluded(string condition)
        {
            Assert.That(TrackerProtocol.AllowsTracking(condition), Is.False);
            Assert.Throws<ArgumentException>(() => new SessionPlan("mock", new[] { condition, "WE_MT", "NE_FT", "NE_MT" }));
        }

        [Test]
        public void FailedStartRecordingNeverPublishesLiveEdge()
        {
            var f = new Fixture();
            f.Sink.Reject = "BlockStarted";
            f.Start();
            Assert.That(f.Engine.State, Is.EqualTo(SessionState.Failed));
            Assert.That(f.Peer.Frames, Is.Empty);
        }

        [Test]
        public void DeadlineStopsBeforeBreakAndNextNeStaysIdle()
        {
            var f = new Fixture();
            f.Start();
            f.Advance(249);
            Assert.That(f.Types.Last(), Is.EqualTo("START"));
            f.Advance(1);
            Assert.That(f.Types.Last(), Is.EqualTo("HEARTBEAT"));
            f.Advance(250);
            f.Advance(250);
            f.Advance(250);
            Assert.That(f.Engine.State, Is.EqualTo(SessionState.BlockEnded));
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START", "HEARTBEAT", "HEARTBEAT", "HEARTBEAT", "STOP" }));
            Assert.That((string?)f.Frame(5)["run"], Is.EqualTo((string?)f.Frame(1)["run"]));
            f.Engine.Advance();
            Assert.That(f.Engine.State, Is.EqualTo(SessionState.Break));
            f.Advance(1000);
            f.Engine.Advance();
            Assert.That(f.Engine.CurrentCondition, Is.EqualTo("NE_FT"));
            f.Start();
            Assert.That(f.Peer.Frames.Count, Is.EqualTo(6));
            Assert.That(f.Peer.EnableCount, Is.EqualTo(1));
        }

        [TestCase("pause")]
        [TestCase("abort")]
        [TestCase("stop")]
        [TestCase("fault")]
        [TestCase("quit")]
        public void ExitRevokesAndOnlyExplicitResumeStartsFresh(string exit)
        {
            var f = new Fixture();
            f.Start();
            switch (exit)
            {
                case "pause": f.Engine.PauseDevelopmentBlock(); break;
                case "abort": f.Engine.Abort(); break;
                case "stop": f.Engine.StopDevelopmentBlock(); break;
                case "fault": f.Sink.Reject = "OperatorNote"; f.Engine.AddNote("fixture"); break;
                case "quit": f.Sender.RequestShutdown(); break;
            }
            f.Pump();
            f.Advance(250, tickEngine: false);
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START", "STOP" }));
            Assert.That(f.Peer.Active, Is.False);
            if (exit != "pause") return;
            f.Engine.ResumeDevelopmentBlock();
            f.Pump();
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START", "STOP", "HELLO", "START" }));
            Assert.That((string?)f.Frame(4)["run"], Is.Not.EqualTo((string?)f.Frame(1)["run"]));
            Assert.That((string?)f.Frame(4)["epoch"], Is.Not.EqualTo((string?)f.Frame(1)["epoch"]));
        }

        [TestCase("HELLO")]
        [TestCase("START")]
        [TestCase("HEARTBEAT")]
        [TestCase("STOP")]
        public void LostAckRetriesTheExactBytesOnce(string command)
        {
            var f = new Fixture();
            f.Peer.DropNext = command;
            f.Start();
            if (command == "HEARTBEAT") f.Advance(250);
            if (command == "STOP") { f.Engine.Abort(); f.Pump(); }
            Assert.That(f.Types.Count(type => type == command), Is.EqualTo(1));
            f.Advance(99);
            Assert.That(f.Types.Count(type => type == command), Is.EqualTo(1));
            f.Advance(1);
            var copies = f.Peer.Frames.Where(line => TypeOf(line) == command).ToArray();
            Assert.That(copies, Has.Length.EqualTo(2));
            Assert.That(copies[1], Is.EqualTo(copies[0]));
            Assert.That(f.Peer.EnableCount, Is.EqualTo(1));
        }

        [Test]
        public void WriteTimeCountsAndTwoMissingStartAcksCauseStop()
        {
            var f = new Fixture();
            f.Peer.DropAll = "START";
            f.Peer.BeforeFrame = command =>
            {
                if (command != "START") return;
                f.Peer.BeforeFrame = null;
                f.Clock.Advance(TimeSpan.FromMilliseconds(100));
            };
            f.Start();
            Assert.That(f.Clock.Now.Elapsed.TotalMilliseconds, Is.EqualTo(1100));
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START", "START" }));
            Assert.That(f.Peer.Frames.ToArray()[1], Is.EqualTo(f.Peer.Frames.ToArray()[2]));
            f.Advance(99);
            Assert.That(f.Types.Last(), Is.EqualTo("START"));
            f.Advance(1);
            Assert.That(f.Types.Last(), Is.EqualTo("STOP"));
            Assert.That((int?)f.Frame(3)["seq"], Is.EqualTo(2));
            Assert.That(f.Peer.Active, Is.False);
        }

        [Test]
        public void LateHelloAndOutstandingStartCannotSurviveRevocation()
        {
            var late = new Fixture();
            late.Peer.DropAll = "HELLO";
            late.Start();
            late.Engine.Abort();
            late.Peer.Inject(late.Peer.LastReply!);
            late.Pump();
            Assert.That(late.Types, Is.EqualTo(new[] { "HELLO" }));

            var outstanding = new Fixture();
            outstanding.Peer.DropAll = "START";
            outstanding.Start();
            Assert.That(outstanding.Peer.Active, Is.True);
            outstanding.Engine.Abort();
            outstanding.Pump();
            Assert.That(outstanding.Types, Is.EqualTo(new[] { "HELLO", "START", "STOP" }));
            Assert.That((int?)outstanding.Frame(2)["seq"], Is.EqualTo(2));
        }

        [Test]
        public void RevocationPoisonsAPartialStartBeforeWritingStop()
        {
            var f = new Fixture();
            f.Peer.HoldFinalLfFor = "START";
            f.Start();
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO" }));
            f.Engine.Abort();
            f.Pump();
            Assert.That(f.Peer.InvalidFrames, Is.EqualTo(1));
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "STOP" }));
            Assert.That((int?)f.Frame(1)["seq"], Is.EqualTo(2));
            Assert.That(f.Peer.EnableCount, Is.Zero);
        }

        [Test]
        public void RejectsBadRepliesAndAcceptsAFragmentedMatchingAck()
        {
            var f = new Fixture();
            f.Peer.DropAll = "HELLO";
            f.Start();
            string ack = f.Peer.LastReply!;
            string run = (string)f.Frame(0)["run"]!;
            foreach (string invalid in new[] {
                ack.Replace(run, new string('0', 32)), ack.Replace("IDLE", "ACTIVE"), ack.Replace("HELLO", "STOP"),
                ack.Replace("\"seq\":0", "\"seq\":true"), ack.Replace("\"seq\":0", "\"seq\":0.0"),
                ack.Replace("\"seq\":0", "\"seq\":0e0"), ack.Replace("\"seq\":0", "\"seq\":2147483648"),
                ack.Replace("\"seq\":0", "\"seq\":9999999999999999999999999"), ack.Replace("\"v\":1", "\"v\":1,\"v\":1"),
                ack.Replace("\"v\":1", "\"v\":1,\"other\":0"), ack.Replace("}\n", ",}\n"),
                ack.Replace("\n", "\r\n"), "\uFEFF" + ack, new string(' ', 513) + ack })
            {
                f.Peer.Inject(invalid);
                f.Pump();
                Assert.That(f.Types, Is.EqualTo(new[] { "HELLO" }));
            }
            f.Peer.Inject(new byte[] { 0xff, (byte)'\n' });
            f.Peer.Inject(ack.Substring(0, 12));
            f.Pump();
            Assert.That(f.Types.Length, Is.EqualTo(1));
            f.Peer.Inject(ack.Substring(12));
            f.Pump();
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START" }));
        }

        [Test]
        public void CorrelatedErrorAndWrongEpochAckAreHandledSeparately()
        {
            var f = new Fixture();
            f.Peer.DropAll = "START";
            f.Start();
            string ack = f.Peer.LastReply!;
            var start = f.Frame(1);
            f.Peer.Inject(ack.Replace((string)start["epoch"]!, new string('0', 32)));
            f.Pump();
            Assert.That(f.Sender.Status, Is.EqualTo("Simulator starting"));
            f.Peer.Inject(new JObject { ["v"] = 1, ["type"] = "ERROR", ["code"] = "APPLY_FAILED",
                ["seq"] = 1, ["run"] = start["run"], ["epoch"] = start["epoch"] }.ToString(Formatting.None) + "\n");
            f.Pump();
            Assert.That(f.Types, Is.EqualTo(new[] { "HELLO", "START", "STOP" }));
            Assert.That(f.Engine.State, Is.EqualTo(SessionState.BlockRunning));
        }

        [TestCase(1000, 250)] // Stalled Unity tick expires the freshness window.
        [TestCase(1000, 1000)] // Scenario deadline also revokes when Unity has not ticked.
        public void StaleSnapshotsAndDeadlinesCannotKeepTheRunAlive(int durationMs, int advanceMs)
        {
            var f = new Fixture(durationMs: durationMs);
            f.Start();
            if (advanceMs == durationMs) f.Advance(durationMs - 100);
            f.Advance(advanceMs == durationMs ? 100 : advanceMs, tickEngine: false);
            Assert.That(f.Types.Last(), Is.EqualTo("STOP"));
            f.Advance(10);
            Assert.That(f.Types.Count(type => type == "START"), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReconnectNeverReplaysTheCurrentEdge(bool connectedAtStart)
        {
            var f = new Fixture(connected: connectedAtStart, durationMs: 5000);
            f.Start();
            if (connectedAtStart) { f.Peer.ReadFault = true; f.Pump(); }
            int writes = f.Peer.Frames.Count;
            int opens = f.Peer.OpenAttempts;
            f.Peer.Available = true;
            f.Advance(999);
            Assert.That(f.Peer.OpenAttempts, Is.EqualTo(opens));
            f.Advance(1);
            Assert.That(f.Peer.OpenAttempts, Is.EqualTo(opens + 1));
            Assert.That(f.Peer.Frames.Count, Is.EqualTo(writes));
            Assert.That(f.Peer.Active, Is.False);
            f.Engine.PauseDevelopmentBlock();
            f.Engine.ResumeDevelopmentBlock();
            f.Pump();
            Assert.That(f.Peer.Frames.Count, Is.EqualTo(writes + 2));
            Assert.That(f.Peer.Active, Is.True);
        }

        [Test]
        public void SequenceExhaustionReservesTheLastNumberForStop()
        {
            var f = new Fixture();
            f.Start();
            // Place the live sender at the actual wire limit without billions of heartbeats.
            var run = typeof(TrackerSender).GetField("run", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(f.Sender)!;
            run.GetType().GetField("LastSequence")!.SetValue(run, int.MaxValue - 1);
            f.Advance(250);
            Assert.That(f.Types.Last(), Is.EqualTo("STOP"));
            Assert.That((int?)f.Frame(2)["seq"], Is.EqualTo(int.MaxValue));
        }

        private static string TypeOf(string line) => (string)JObject.Parse(line)["type"]!;

        private sealed class Fixture
        {
            public readonly ManualSharedClock Clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
            public readonly SessionEngine Engine;
            public readonly TrackerSender Sender;
            public readonly MockTrackerPeer Peer;
            public readonly TestSink Sink = new TestSink();
            public string[] Types => Peer.Frames.Select(TypeOf).ToArray();
            public JObject Frame(int index) => JObject.Parse(Peer.Frames.ToArray()[index]);

            public Fixture(string first = "WE_FT", bool connected = true, int durationMs = 1000)
            {
                var order = new[] { first }.Concat(new[] { "NE_FT", "WE_FT", "WE_MT", "NE_MT" }.Where(code => code != first));
                Engine = new SessionEngine(Clock, new SessionEventSequence(), new SessionPlan("mock", order, durationMs / 1000.0, true),
                    new SessionTiming(1, 1, true), Sink, new TestLink());
                Peer = new MockTrackerPeer { Available = connected };
                Sender = new TrackerSender(Clock, Peer);
                Sender.ObserveSession(Engine);
                Pump();
                Engine.StartSetup(new SessionReadinessInput { TwoDisplays = true, SteamVrRunning = true,
                    TrackersBoundAndValidForTwoSeconds = true, ConfigurationHashesComputed = true,
                    DiskFreeBytes = 4L * 1024 * 1024 * 1024, LogWriterAlive = true, WeLinkReachable = true });
                Engine.Advance();
                Engine.Advance();
                // Let the failed initial open stay in backoff through its first live edge.
                Clock.Advance(TimeSpan.FromSeconds(1));
                Engine.Tick();
                if (connected) Pump();
            }

            public void Start() { Engine.StartBlock(); Pump(); }
            public void Pump() { for (int i = 0; i < 5 && Sender.Pump(); i++) { } }
            public void Advance(int milliseconds, bool tickEngine = true)
            {
                Clock.Advance(TimeSpan.FromMilliseconds(milliseconds));
                if (tickEngine) Engine.Tick();
                Pump();
            }
        }

        private sealed class TestSink : ISessionEventSink
        {
            public string? Reject;
            public bool TryRecord(SessionEvent item) => item.EventType != Reject;
        }

        private sealed class TestLink : ISessionLink
        {
            public bool TrySetEnabled(bool enabled) => true;
        }
    }

    /// <summary>Injected peer for Windows sender tests. All outputs are counters and booleans.</summary>
    internal sealed class MockTrackerPeer : ITrackerByteTransport
    {
        public readonly ConcurrentQueue<string> Frames = new ConcurrentQueue<string>();
        private readonly Queue<byte> replies = new Queue<byte>();
        private readonly List<byte> partial = new List<byte>();
        private string? epoch;
        private string? previousFrame;
        private bool active;
        private int enables;
        public bool Available = true;
        public bool ReadFault;
        public string? DropNext, DropAll, LastReply, HoldFinalLfFor;
        public Action<string>? BeforeFrame;
        public int OpenAttempts, InvalidFrames;
        public int EnableCount => System.Threading.Volatile.Read(ref enables);
        public bool Active => System.Threading.Volatile.Read(ref active);

        public bool TryOpen() { OpenAttempts++; return Available; }
        public void Close()
        {
            System.Threading.Volatile.Write(ref active, false);
            epoch = previousFrame = LastReply = null;
            replies.Clear();
            partial.Clear();
        }

        public void Inject(string reply) => Inject(Encoding.UTF8.GetBytes(reply));
        public void Inject(byte[] reply) { foreach (byte value in reply) replies.Enqueue(value); }

        public int Read(byte[] buffer)
        {
            if (ReadFault) { ReadFault = false; throw new IOException("Mock disconnect"); }
            int count = Math.Min(buffer.Length, replies.Count);
            for (int i = 0; i < count; i++) buffer[i] = replies.Dequeue();
            return count;
        }

        public int Write(byte[] frame, int offset)
        {
            int end = frame.Length;
            if (HoldFinalLfFor != null && Encoding.UTF8.GetString(frame).Contains("\"type\":\"" + HoldFinalLfFor + "\"")) end--;
            for (int i = offset; i < end; i++)
            {
                if (frame[i] != (byte)'\n') partial.Add(frame[i]);
                else
                {
                    string line = Encoding.UTF8.GetString(partial.ToArray());
                    partial.Clear();
                    Receive(line);
                }
            }
            return end - offset;
        }

        private void Receive(string line)
        {
            if (line.EndsWith("!", StringComparison.Ordinal)) { InvalidFrames++; return; }
            var value = JObject.Parse(line);
            string command = (string)value["type"]!;
            BeforeFrame?.Invoke(command);
            Frames.Enqueue(line);
            if (line != previousFrame)
            {
                if (command == "HELLO") epoch = Guid.NewGuid().ToString("N");
                if (command == "START")
                {
                    System.Threading.Interlocked.Increment(ref enables);
                    System.Threading.Volatile.Write(ref active, true);
                }
                if (command == "STOP") System.Threading.Volatile.Write(ref active, false);
                LastReply = new JObject { ["v"] = 1, ["type"] = "ACK", ["cmd"] = command,
                    ["run"] = value["run"], ["seq"] = value["seq"], ["epoch"] = epoch,
                    ["state"] = Active ? "ACTIVE" : "IDLE" }.ToString(Formatting.None) + "\n";
                previousFrame = line;
            }
            if (DropNext == command) DropNext = null;
            else if (DropAll != command) Inject(LastReply!);
        }
    }
}
