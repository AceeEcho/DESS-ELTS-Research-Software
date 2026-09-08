#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Elts.Clock;
using Elts.Logging;
using Elts.Scenario;

namespace Elts.Session
{
    public enum ScenarioBlockState { Ready, Running, Completed, Aborted }

    /// <summary>
    /// Session timing uses only the injected shared monotonic clock. Completion is
    /// emitted at the exact deadline to preserve the half-open [start, end) window.
    /// </summary>
    public sealed class ScenarioBlockController
    {
        private readonly ISharedClock clock;
        private readonly long durationTicks;
        private readonly string blockId;
        private readonly int seed;
        private readonly ISessionEventSequence sequence;
        private MonotonicTimestamp deadline;
        private MonotonicTimestamp startedAt;

        public ScenarioBlockController(
            ISharedClock clock,
            ISessionEventSequence sequence,
            string blockId,
            int seed,
            double durationSeconds = 300,
            bool allowDevelopmentOnlyDurationOverride = false)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (sequence == null) throw new ArgumentNullException(nameof(sequence));
            if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identifier is required.", nameof(blockId));
            if (durationSeconds <= 0 || Double.IsNaN(durationSeconds) || Double.IsInfinity(durationSeconds)
                || (durationSeconds != 300 && !allowDevelopmentOnlyDurationOverride))
                throw new ArgumentException("Block duration is fixed at 300 seconds unless an explicit development override is supplied.", nameof(durationSeconds));

            this.clock = clock;
            this.sequence = sequence;
            this.blockId = blockId;
            this.seed = seed;
            durationTicks = checked((long)(durationSeconds * TimeSpan.TicksPerSecond));
            State = ScenarioBlockState.Ready;
        }

        public ScenarioBlockState State { get; private set; }

        public IReadOnlyList<SessionEvent> Start()
        {
            if (State != ScenarioBlockState.Ready) throw new InvalidOperationException("A block can start only once.");
            startedAt = clock.Now;
            deadline = new MonotonicTimestamp(checked(startedAt.Ticks + durationTicks));
            State = ScenarioBlockState.Running;
            return new[] { Event(startedAt, "BlockStarted", LogField.NumberValue("seed", seed)) };
        }

        public IReadOnlyList<SessionEvent> Update()
        {
            if (State != ScenarioBlockState.Running || clock.Now < deadline) return Array.Empty<SessionEvent>();
            State = ScenarioBlockState.Completed;
            return new[] { Event(deadline, "BlockEnded") };
        }

        public IReadOnlyList<SessionEvent> Abort(string reason)
        {
            if (State != ScenarioBlockState.Running) throw new InvalidOperationException("Only a running block can be aborted.");
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An abort reason is required.", nameof(reason));
            State = ScenarioBlockState.Aborted;
            return new[] { Event(clock.Now, "BlockAborted", LogField.String("reason", reason)) };
        }

        /// <summary>Only this block-owned path may add shot events to the active block.</summary>
        public IReadOnlyList<SessionEvent> Fire(
            IShotModel shotModel,
            ShotContext shot,
            IEnumerable<TargetEntity> targets,
            Func<TargetEntity, bool> isVisible)
        {
            if (shotModel == null) throw new ArgumentNullException(nameof(shotModel));
            if (shot == null) throw new ArgumentNullException(nameof(shot));
            if (State != ScenarioBlockState.Running || clock.Now >= deadline || shot.Timestamp < startedAt || shot.Timestamp >= deadline)
                return Array.Empty<SessionEvent>();
            if (shot.Timestamp > clock.Now) throw new ArgumentException("A shot cannot be timestamped after the shared clock.", nameof(shot));
            return shotModel.Fire(shot, blockId, targets, isVisible);
        }

        private SessionEvent Event(MonotonicTimestamp timestamp, string eventType, params LogField[] extra)
        {
            var fields = new List<LogField> { LogField.String("blockId", blockId) };
            fields.AddRange(extra);
            return new SessionEvent(sequence.Next(), timestamp, eventType, fields);
        }
    }

    public enum SessionState { Idle, SessionSetup, Calibration, Practice, BlockReady, BlockRunning, BlockEnded, Break, SessionComplete, Aborted, Failed }

    public sealed class SessionTiming
    {
        public SessionTiming(double practiceSeconds, double breakSeconds, bool allowDevelopmentOnlyTiming = false)
        {
            if (practiceSeconds < 0 || breakSeconds < 0 || Double.IsNaN(practiceSeconds) || Double.IsInfinity(practiceSeconds)
                || Double.IsNaN(breakSeconds) || Double.IsInfinity(breakSeconds))
                throw new ArgumentOutOfRangeException();
            PracticeSeconds = practiceSeconds;
            BreakSeconds = breakSeconds;
            AllowDevelopmentOnlyTiming = allowDevelopmentOnlyTiming;
        }

        public double PracticeSeconds { get; }
        public double BreakSeconds { get; }
        public bool AllowDevelopmentOnlyTiming { get; }
    }

    public sealed class SessionReadinessInput
    {
        public bool TwoDisplays { get; set; }
        public bool SteamVrRunning { get; set; }
        public bool TrackersBoundAndValidForTwoSeconds { get; set; }
        public bool ConfigurationHashesComputed { get; set; }
        public long DiskFreeBytes { get; set; }
        public bool LogWriterAlive { get; set; }
        public bool WeLinkReachable { get; set; }
    }

    public sealed class SessionReadinessReport
    {
        internal SessionReadinessReport(IReadOnlyList<string> failures, bool setupAccepted = false)
        {
            Failures = failures;
            IsReady = failures.Count == 0;
            SetupAccepted = setupAccepted;
            StudyReady = false;
        }

        public bool IsReady { get; }
        public bool SetupAccepted { get; }
        public bool StudyReady { get; }
        public IReadOnlyList<string> Failures { get; }

        internal SessionReadinessReport WithSetupAcceptance(bool accepted) => new SessionReadinessReport(Failures, accepted);

        public static SessionReadinessReport Evaluate(SessionReadinessInput input, bool containsWeBlock)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var failures = new List<string>();
            if (!input.TwoDisplays) failures.Add("Two displays are required.");
            if (!input.SteamVrRunning) failures.Add("SteamVR is not running.");
            if (!input.TrackersBoundAndValidForTwoSeconds) failures.Add("Both bound trackers must be valid for at least 2 seconds.");
            if (!input.ConfigurationHashesComputed) failures.Add("Configuration hashes are missing.");
            if (input.DiskFreeBytes < 2L * 1024 * 1024 * 1024) failures.Add("At least 2 GB free disk space is required.");
            if (!input.LogWriterAlive) failures.Add("The log writer is not alive.");
            if (containsWeBlock && !input.WeLinkReachable) failures.Add("The WE link is not reachable.");
            return new SessionReadinessReport(failures);
        }
    }

    public interface ISessionEventSink { bool TryRecord(SessionEvent item); }
    public interface ISessionLink { bool TrySetEnabled(bool enabled); }
    public interface ISessionRecordingLifecycle { Task<bool> ReserveAndStartAsync(string runId); Task<bool> CloseAsync(); }

    /// <summary>
    /// Pure operator-driven session state machine. Sinks own I/O. Any event or
    /// link-delivery failure makes the engine terminal and disables an active WE link.
    /// </summary>
    public sealed class SessionEngine
    {
        private readonly ISharedClock clock;
        private readonly ISessionEventSequence sequence;
        private readonly SessionPlan plan;
        private readonly SessionTiming timing;
        private readonly ISessionEventSink sink;
        private readonly ISessionLink? link;
        private ScenarioBlockController? block;
        private MonotonicTimestamp phaseStarted;
        private int blockIndex;
        private int rerun;
        private bool linkMayBeEnabled;
        private string? failure;

        public SessionEngine(ISharedClock clock, ISessionEventSequence sequence, SessionPlan plan, SessionTiming timing, ISessionEventSink sink, ISessionLink? link = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.timing = timing ?? throw new ArgumentNullException(nameof(timing));
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.link = link;
            State = SessionState.Idle;
        }

        public SessionState State { get; private set; }
        public string? CurrentCondition => blockIndex < plan.ConditionOrder.Count ? plan.ConditionOrder[blockIndex] : null;
        public string? CurrentBlockId => CurrentCondition == null ? null : CurrentCondition + "-attempt-" + (rerun + 1).ToString("D2");
        public int CurrentBlockIndex => blockIndex;
        public int RerunIndex => rerun;
        public string? Failure => failure;
        public bool TargetsActive => State == SessionState.BlockRunning;
        /// <summary>True only when a completed or aborted real block can start a new attempt.</summary>
        public bool CanRerun => (State == SessionState.Aborted || State == SessionState.BlockEnded) && block != null;
        public bool CanAdvance => State == SessionState.SessionSetup || State == SessionState.Calibration || State == SessionState.BlockEnded || State == SessionState.Break;

        public double RemainingSeconds
        {
            get
            {
                if (State == SessionState.BlockRunning && block != null) return Math.Max(0, plan.ConditionDurationSeconds - (clock.Now.Elapsed - phaseStarted.Elapsed).TotalSeconds);
                if (State == SessionState.Practice) return Math.Max(0, timing.PracticeSeconds - (clock.Now.Elapsed - phaseStarted.Elapsed).TotalSeconds);
                if (State == SessionState.Break) return Math.Max(0, timing.BreakSeconds - (clock.Now.Elapsed - phaseStarted.Elapsed).TotalSeconds);
                return 0;
            }
        }

        public SessionReadinessReport StartSetup(SessionReadinessInput readiness)
        {
            Require(SessionState.Idle);
            var report = SessionReadinessReport.Evaluate(readiness, plan.ConditionOrder.Any(c => c.StartsWith("WE_", StringComparison.Ordinal)));
            if (!report.IsReady) return report;

            if (!Record(Event("SessionMetadata", LogField.String("participantId", plan.ParticipantId), LogField.String("conditionOrder", String.Join("|", plan.ConditionOrder)))))
                return report.WithSetupAcceptance(false);
            return report.WithSetupAcceptance(Transition(SessionState.SessionSetup));
        }

        public void Advance()
        {
            if (State == SessionState.SessionSetup) Transition(SessionState.Calibration);
            else if (State == SessionState.Calibration)
            {
                phaseStarted = clock.Now;
                Transition(SessionState.Practice);
            }
            else if (State == SessionState.BlockEnded)
            {
                phaseStarted = clock.Now;
                Transition(SessionState.Break);
            }
            else if (State == SessionState.Break)
            {
                if (clock.Now.Elapsed - phaseStarted.Elapsed < TimeSpan.FromSeconds(timing.BreakSeconds)) return;
                blockIndex++;
                rerun = 0;
                Transition(blockIndex >= plan.ConditionOrder.Count ? SessionState.SessionComplete : SessionState.BlockReady);
            }
            else throw new InvalidOperationException("This state cannot advance.");
        }

        public void Tick()
        {
            if (State == SessionState.Practice && clock.Now.Elapsed - phaseStarted.Elapsed >= TimeSpan.FromSeconds(timing.PracticeSeconds))
            {
                Transition(SessionState.BlockReady);
                return;
            }

            if (State != SessionState.BlockRunning || block == null) return;
            foreach (var item in block.Update())
                if (!Record(item)) return;
            if (State != SessionState.BlockRunning || block.State != ScenarioBlockState.Completed) return;
            if (!StopLink()) return;
            Transition(SessionState.BlockEnded);
        }

        public void StartBlock()
        {
            Require(SessionState.BlockReady);
            string condition = CurrentCondition ?? throw new InvalidOperationException("There is no remaining block.");
            string blockId = CurrentBlockId ?? throw new InvalidOperationException("There is no current block identity.");
            if (condition.StartsWith("WE_", StringComparison.Ordinal) && !TrySetLink(true))
            {
                Fail("WE link start failed.");
                return;
            }

            block = new ScenarioBlockController(clock, sequence, blockId, DeterministicSeedFor(plan.ParticipantId, blockId), plan.ConditionDurationSeconds, plan.AllowsDevelopmentOnlyDurationOverride);
            phaseStarted = clock.Now;
            // Each attempt is independently identifiable, including a rerun
            // reserved in a new output directory by the recording adapter.
            if (!Record(Event("BlockMetadata", LogField.String("blockId", blockId),
                LogField.String("participantId", plan.ParticipantId), LogField.String("condition", condition),
                LogField.String("conditionOrder", String.Join("|", plan.ConditionOrder)),
                LogField.NumberValue("rerunIndex", rerun)))) return;
            if (!Transition(SessionState.BlockRunning)) return;
            foreach (var item in block.Start())
                if (!Record(item)) return;
        }

        public void EndBlock()
        {
            Require(SessionState.BlockRunning);
            Tick();
            if (State == SessionState.BlockRunning)
                throw new InvalidOperationException("A block can end only when its fixed duration has elapsed.");
        }

        /// <summary>Forwards an in-window shot through the active block and records every resulting event.</summary>
        public bool Fire(IShotModel shotModel, ShotContext shot, IEnumerable<TargetEntity> targets, Func<TargetEntity, bool> isVisible)
        {
            ExpireCurrentPhase();
            Require(SessionState.BlockRunning);
            if (block == null) throw new InvalidOperationException("The active block has not been initialized.");
            bool shotRecorded = false;
            foreach (var item in block.Fire(shotModel, shot, targets, isVisible))
            {
                if (!Record(item)) return false;
                if (item.EventType == "ShotFired") shotRecorded = true;
            }
            return shotRecorded;
        }

        /// <summary>
        /// Operator abort is available throughout an unfinished session. Only an active
        /// block emits BlockAborted; setup and phase aborts preserve that distinction.
        /// </summary>
        public void Abort(string reason)
        {
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An abort reason is required.", nameof(reason));
            ExpireCurrentPhase();
            if (State == SessionState.Aborted || State == SessionState.Failed || State == SessionState.SessionComplete) return;

            if (State == SessionState.BlockRunning && block != null)
            {
                foreach (var item in block.Abort(reason))
                    if (!Record(item)) return;
                if (State != SessionState.BlockRunning || !StopLink()) return;
            }
            else if (!StopLink()) return;

            Transition(SessionState.Aborted, LogField.String("reason", reason));
        }

        public void RerunCurrentBlock()
        {
            if ((State != SessionState.Aborted && State != SessionState.BlockEnded) || block == null)
                throw new InvalidOperationException("Only an aborted or ended block can rerun.");
            rerun++;
            block = null;
            Transition(SessionState.BlockReady, LogField.NumberValue("rerunIndex", rerun), LogField.String("nextBlockId", CurrentBlockId ?? String.Empty));
        }

        public void AddNote(string text)
        {
            ExpireCurrentPhase();
            RequireOpenSession();
            if (String.IsNullOrWhiteSpace(text)) throw new ArgumentException("A note is required.", nameof(text));
            Record(Event("OperatorNote", LogField.String("text", text)));
        }

        public void EmitProvisionalSyncMarker(string text)
        {
            ExpireCurrentPhase();
            RequireOpenSession();
            Record(Event("SyncMarker", LogField.String("label", String.IsNullOrWhiteSpace(text) ? "provisional" : text), LogField.Boolean("provisional", true)));
        }

        private void ExpireCurrentPhase()
        {
            if (State == SessionState.Practice || State == SessionState.BlockRunning)
                Tick();
        }

        private bool Transition(SessionState next, params LogField[] extra)
        {
            if (State == SessionState.Failed) return false;
            var fields = new List<LogField> { LogField.String("from", State.ToString()), LogField.String("to", next.ToString()) };
            fields.AddRange(extra);
            if (!Record(Event("SessionTransition", fields.ToArray()))) return false;
            State = next;
            return true;
        }

        private SessionEvent Event(string type, params LogField[] fields) => new SessionEvent(sequence.Next(), clock.Now, type, fields);

        private bool Record(SessionEvent item)
        {
            try
            {
                if (sink.TryRecord(item)) return true;
                Fail("Session event logging failed.");
            }
            catch (Exception e)
            {
                Fail("Session event logging failed: " + e.Message);
            }
            return false;
        }

        private bool TrySetLink(bool enabled)
        {
            if (link == null) return false;
            // A failed enable may still have reached the device. Preserve that uncertainty
            // so Fail always makes one best-effort disable attempt before becoming terminal.
            if (enabled) linkMayBeEnabled = true;
            try
            {
                if (!link.TrySetEnabled(enabled)) return false;
                if (!enabled) linkMayBeEnabled = false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool StopLink()
        {
            if (!linkMayBeEnabled) return true;
            if (TrySetLink(false)) return true;
            Fail("WE link stop failed.");
            return false;
        }

        private void Fail(string message)
        {
            if (State == SessionState.Failed) return;
            SessionState previous = State;
            failure = message;
            State = SessionState.Failed;
            RecordFailureTransition(previous, message);
            if (!linkMayBeEnabled) return;
            try { link?.TrySetEnabled(false); }
            catch { }
            finally { linkMayBeEnabled = false; }
        }

        private void RecordFailureTransition(SessionState previous, string message)
        {
            // This is deliberately non-recursive: the ordinary sink may itself be the
            // failure source, but a healthy sink still receives terminal audit evidence.
            try
            {
                sink.TryRecord(Event("SessionTransition", LogField.String("from", previous.ToString()),
                    LogField.String("to", SessionState.Failed.ToString()), LogField.String("failure", message)));
            }
            catch { }
        }

        private void Require(SessionState expected)
        {
            if (State != expected) throw new InvalidOperationException("Invalid session state transition.");
        }

        private void RequireOpenSession()
        {
            if (State == SessionState.Idle || State == SessionState.SessionComplete || State == SessionState.Aborted || State == SessionState.Failed)
                throw new InvalidOperationException("Notes and markers are unavailable after the session has closed.");
        }

        private static int DeterministicSeedFor(string participantId, string blockId)
        {
            unchecked
            {
                int value = 17;
                foreach (char c in participantId + "|" + blockId) value = value * 31 + c;
                return value & Int32.MaxValue;
            }
        }
    }
}
