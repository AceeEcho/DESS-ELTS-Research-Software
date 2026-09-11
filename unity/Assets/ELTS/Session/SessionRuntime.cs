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
    public enum ScenarioBlockState { Ready, Running, Completed, Aborted, Paused }

    /// <summary>
    /// Session timing uses only the injected shared monotonic clock. Completion is
    /// emitted at the exact deadline to preserve the half-open [start, end) window.
    /// </summary>
    public sealed class ScenarioBlockController
    {
        private readonly ISharedClock clock;
        private long durationTicks;
        private readonly bool developmentControls;
        private long accumulatedActiveTicks;
        private bool modifiedTiming;
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
            developmentControls = allowDevelopmentOnlyDurationOverride;
            modifiedTiming = durationSeconds != 300;
            State = ScenarioBlockState.Ready;
        }

        public ScenarioBlockState State { get; private set; }
        public bool HasModifiedTiming => modifiedTiming;
        public double DurationSeconds => durationTicks / (double)TimeSpan.TicksPerSecond;
        public long ActiveTicks => accumulatedActiveTicks + (State == ScenarioBlockState.Running
            ? Math.Max(0, Math.Min(clock.Now.Ticks, deadline.Ticks) - startedAt.Ticks) : 0);
        public double RemainingSeconds => Math.Max(0, durationTicks - ActiveTicks) / (double)TimeSpan.TicksPerSecond;

        public IReadOnlyList<SessionEvent> Start()
        {
            if (State != ScenarioBlockState.Ready) throw new InvalidOperationException("A block can start only once.");
            startedAt = clock.Now;
            deadline = new MonotonicTimestamp(checked(startedAt.Ticks + durationTicks));
            State = ScenarioBlockState.Running;
            return new[] { Event(startedAt, "BlockStarted", LogField.NumberValue("seed", seed),
                LogField.NumberValue("durationSeconds", DurationSeconds), LogField.Boolean("developmentTiming", modifiedTiming)) };
        }

        public IReadOnlyList<SessionEvent> Update()
        {
            if (State != ScenarioBlockState.Running || clock.Now < deadline) return Array.Empty<SessionEvent>();
            accumulatedActiveTicks = durationTicks;
            State = ScenarioBlockState.Completed;
            return new[] { EndEvent(deadline, "elapsed") };
        }

        // Pauses leave the shared acquisition clock alone. Only the block's active
        // time stops, so raw samples and operator events retain their real ordering.
        public IReadOnlyList<SessionEvent> Pause()
        {
            RequireDevelopment();
            if(State != ScenarioBlockState.Running)throw new InvalidOperationException("Only a running test can pause.");
            var now = clock.Now;
            if(now >= deadline)
            {
                accumulatedActiveTicks = durationTicks; State = ScenarioBlockState.Completed;
                return new[] { EndEvent(deadline, "elapsed") };
            }
            accumulatedActiveTicks += now.Ticks - startedAt.Ticks;
            State = ScenarioBlockState.Paused; modifiedTiming = true;
            return new[] { Event(now, "BlockPaused", LogField.NumberValue("activeSeconds", ActiveTicks/(double)TimeSpan.TicksPerSecond)) };
        }

        public IReadOnlyList<SessionEvent> Resume()
        {
            RequireDevelopment();
            if(State != ScenarioBlockState.Paused)throw new InvalidOperationException("Only a paused test can resume.");
            // Reject delayed trigger observations from before this new active segment.
            startedAt = clock.Now;
            deadline = new MonotonicTimestamp(checked(startedAt.Ticks + durationTicks - accumulatedActiveTicks));
            State = ScenarioBlockState.Running;
            return new[] { Event(startedAt, "BlockResumed", LogField.NumberValue("remainingSeconds", RemainingSeconds)) };
        }

        public void ChangeDuration(double seconds)
        {
            RequireDevelopment();
            if(State != ScenarioBlockState.Paused)throw new InvalidOperationException("Pause the test before changing its duration.");
            SessionEngine.ValidateDevelopmentDuration(seconds);
            long ticks = checked((long)(seconds * TimeSpan.TicksPerSecond));
            if(ticks <= ActiveTicks)throw new ArgumentException("Duration must exceed the time already played. Use Stop test to finish now.");
            durationTicks = ticks; modifiedTiming = true;
        }

        public IReadOnlyList<SessionEvent> Stop()
        {
            RequireDevelopment();
            if(State != ScenarioBlockState.Running && State != ScenarioBlockState.Paused)
                throw new InvalidOperationException("Only a running or paused test can stop.");
            accumulatedActiveTicks = ActiveTicks;
            State = ScenarioBlockState.Completed; modifiedTiming = true;
            return new[] { EndEvent(clock.Now, "operator_stop") };
        }

        private SessionEvent EndEvent(MonotonicTimestamp at, string reason) => Event(at, "BlockEnded",
            LogField.String("reason", reason), LogField.NumberValue("durationSeconds", DurationSeconds),
            LogField.NumberValue("activeSeconds", ActiveTicks/(double)TimeSpan.TicksPerSecond),
            LogField.Boolean("developmentTiming", modifiedTiming));

        private void RequireDevelopment()
        {
            if(!developmentControls)throw new InvalidOperationException("Flexible test controls require an explicit development session.");
        }

        public IReadOnlyList<SessionEvent> Abort(string reason)
        {
            if (State != ScenarioBlockState.Running && State != ScenarioBlockState.Paused) throw new InvalidOperationException("Only a running or paused block can be aborted.");
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

    public enum SessionState { Idle, SessionSetup, Calibration, Practice, BlockReady, BlockRunning, BlockEnded, Break, SessionComplete, Aborted, Failed, BlockPaused }

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
    /// <summary>
    /// Optional richer session-link seam. The engine calls PrepareBlock for every
    /// condition, including NE, so an adapter can apply its explicit development
    /// policy without letting the session engine decide D-10 behavior.
    /// </summary>
    public interface ISessionConditionLink : ISessionLink
    {
        bool PrepareBlock(SessionBlockLinkContext context);
        bool Tick();
    }

    /// <summary>Immutable command context. Duration is monotonic TimeSpan ticks (100 ns).</summary>
    public sealed class SessionBlockLinkContext
    {
        public SessionBlockLinkContext(string participantPseudonym, string blockId, string condition, long durationTicks)
        {
            if (String.IsNullOrWhiteSpace(participantPseudonym)) throw new ArgumentException("A participant pseudonym is required.", nameof(participantPseudonym));
            if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identity is required.", nameof(blockId));
            if (String.IsNullOrWhiteSpace(condition)) throw new ArgumentException("A condition is required.", nameof(condition));
            if (durationTicks <= 0) throw new ArgumentOutOfRangeException(nameof(durationTicks));
            ParticipantPseudonym = participantPseudonym; BlockId = blockId; Condition = condition; DurationTicks = durationTicks;
        }
        public string ParticipantPseudonym { get; }
        public string BlockId { get; }
        public string Condition { get; }
        public long DurationTicks { get; }
    }
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
        // Development repeats extend the execution queue without rewriting the
        // participant's original condition order or reusing an attempt identity.
        private readonly List<string> schedule;
        private readonly Dictionary<string, int> attemptCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<string> finishedConditions = new HashSet<string>(StringComparer.Ordinal);
        private double breakSeconds, practiceSeconds;
        private readonly ISessionEventSink sink;
        private readonly ISessionLink? link;
        private ScenarioBlockController? block;
        private MonotonicTimestamp phaseStarted;
        private int blockIndex;
        private int rerun;
        private bool linkMayBeEnabled;
        private string? failure;
        private readonly Dictionary<string,double> durations = new Dictionary<string,double>(StringComparer.Ordinal);
        public const double MinimumDevelopmentDurationSeconds = 1, MaximumDevelopmentDurationSeconds = 3600;

        public SessionEngine(ISharedClock clock, ISessionEventSequence sequence, SessionPlan plan, SessionTiming timing, ISessionEventSink sink, ISessionLink? link = null)
        {
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.timing = timing ?? throw new ArgumentNullException(nameof(timing));
            schedule = plan.ConditionOrder.ToList();
            breakSeconds = timing.BreakSeconds;
            practiceSeconds = timing.PracticeSeconds;
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.link = link;
            foreach(string condition in plan.ConditionOrder)durations[condition] = plan.ConditionDurationSeconds;
            State = SessionState.Idle;
        }

        public SessionState State { get; private set; }
        public string? CurrentCondition => blockIndex < schedule.Count ? schedule[blockIndex] : null;
        public string? CurrentBlockId => CurrentCondition == null ? null : CurrentCondition + "-attempt-" + (rerun + 1).ToString("D2");
        public int CurrentBlockIndex => blockIndex;
        public int RerunIndex => rerun;
        public string? Failure => failure;
        public bool TargetsActive => State == SessionState.BlockRunning;
        public bool CurrentBlockStoppedEarly { get; private set; }
        public bool CurrentBlockSkipped { get; private set; }
        public bool CurrentBlockModifiedTiming => block?.HasModifiedTiming ?? false;
        public double BreakDurationSeconds => breakSeconds;
        public double PracticeDurationSeconds => practiceSeconds;
        public IReadOnlyList<string> ScheduledConditions => schedule.AsReadOnly();
        public IReadOnlyCollection<string> FinishedConditions => finishedConditions;
        public bool CanScheduleRepeat => plan.AllowsDevelopmentOnlyDurationOverride &&
            (State == SessionState.BlockReady || State == SessionState.BlockEnded || State == SessionState.Break);
        public bool CanReopenForRepeat => plan.AllowsDevelopmentOnlyDurationOverride && State == SessionState.SessionComplete;
        public double CurrentDurationSeconds => CurrentCondition == null ? 0 : durations[CurrentCondition];
        public double CurrentActiveSeconds => block?.ActiveTicks/(double)TimeSpan.TicksPerSecond ?? 0;
        public double DurationFor(string condition) => durations[condition];
        public bool CanChangeDuration(string condition)
        {
            if(!plan.AllowsDevelopmentOnlyDurationOverride || !durations.ContainsKey(condition) ||
                State == SessionState.SessionComplete || State == SessionState.Aborted || State == SessionState.Failed)return false;
            if (condition == CurrentCondition)
                return State != SessionState.BlockRunning && State != SessionState.BlockEnded && State != SessionState.Break;
            return schedule.Skip(blockIndex + 1).Contains(condition);
        }

        public static void ValidateDevelopmentDuration(double seconds)
        {
            if(Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < MinimumDevelopmentDurationSeconds || seconds > MaximumDevelopmentDurationSeconds)
                throw new ArgumentException("Test duration must be between 1 and 3600 seconds.");
        }

        public void SetDevelopmentDuration(string condition, double seconds)
        {
            ValidateDevelopmentDuration(seconds);
            if(!CanChangeDuration(condition))throw new InvalidOperationException("Pause the current test before editing it; finished tests cannot change.");
            double previous = durations[condition];
            if(previous == seconds)return;
            if(condition == CurrentCondition && State == SessionState.BlockPaused)block!.ChangeDuration(seconds);
            if(!Record(Event("BlockDurationChanged", LogField.String("condition", condition),
                LogField.String("blockId", condition == CurrentCondition ? CurrentBlockId! : condition+"-attempt-01"),
                LogField.NumberValue("previousSeconds", previous), LogField.NumberValue("durationSeconds", seconds),
                LogField.Boolean("developmentTiming", true))))return;
            durations[condition] = seconds;
        }
        /// <summary>True only when a completed or aborted real block can start a new attempt.</summary>
        public bool CanRerun => (State == SessionState.Aborted || State == SessionState.BlockEnded) && block != null;
        public bool CanAdvance => State == SessionState.SessionSetup || State == SessionState.Calibration || State == SessionState.BlockEnded || State == SessionState.Break;

        public double RemainingSeconds
        {
            get
            {
                if ((State == SessionState.BlockRunning || State == SessionState.BlockPaused) && block != null) return block.RemainingSeconds;
                if (State == SessionState.Practice) return Math.Max(0, practiceSeconds - (clock.Now.Elapsed - phaseStarted.Elapsed).TotalSeconds);
                if (State == SessionState.Break) return Math.Max(0, breakSeconds - (clock.Now.Elapsed - phaseStarted.Elapsed).TotalSeconds);
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
                if (RemainingSeconds > 0) return;
                FinishBreak();
            }
            else throw new InvalidOperationException("This state cannot advance.");
        }

        public void Tick()
        {
            if (State == SessionState.Practice && clock.Now.Elapsed - phaseStarted.Elapsed >= TimeSpan.FromSeconds(practiceSeconds))
            {
                Transition(SessionState.BlockReady);
                return;
            }

            if (State != SessionState.BlockRunning || block == null) return;
            foreach (var item in block.Update())
                if (!Record(item)) return;
            if (State != SessionState.BlockRunning) return;
            if (block.State == ScenarioBlockState.Completed)
            {
                // At the exact deadline, completion and STOP take precedence over
                // the mock's independent maximum-on-time de-permission check.
                if (!StopLink()) return;
                finishedConditions.Add(CurrentCondition!);
                Transition(SessionState.BlockEnded);
                return;
            }
            if (link is ISessionConditionLink conditionLink)
            {
                try
                {
                    if (!conditionLink.Tick()) Fail("ELTS link heartbeat, time limit, or state check failed.");
                }
                catch (Exception e) { Fail("ELTS link state check failed: " + e.Message); }
            }
        }

        public void StartBlock()
        {
            Require(SessionState.BlockReady);
            string condition = CurrentCondition ?? throw new InvalidOperationException("There is no remaining block.");
            string blockId = CurrentBlockId ?? throw new InvalidOperationException("There is no current block identity.");
            if (link is ISessionConditionLink conditionLink)
            {
                try
                {
                    if (!conditionLink.PrepareBlock(new SessionBlockLinkContext(plan.ParticipantId, blockId, condition, checked((long)(CurrentDurationSeconds * TimeSpan.TicksPerSecond)))))
                    {
                        Fail("ELTS link block preparation failed.");
                        return;
                    }
                }
                catch (Exception e) { Fail("ELTS link block preparation failed: " + e.Message); return; }
            }
            if (condition.StartsWith("WE_", StringComparison.Ordinal) && !TrySetLink(true))
            {
                Fail("WE link start failed.");
                return;
            }

            block = new ScenarioBlockController(clock, sequence, blockId, DeterministicSeedFor(plan.ParticipantId, blockId), CurrentDurationSeconds, plan.AllowsDevelopmentOnlyDurationOverride);
            CurrentBlockStoppedEarly = false;
            CurrentBlockSkipped = false;
            attemptCounts[condition] = rerun + 1;
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

        public void PauseDevelopmentBlock()
        {
            RequireDevelopmentControls(); Tick();
            if(State == SessionState.BlockEnded)return; // Deadline wins over a late pause.
            Require(SessionState.BlockRunning);
            foreach(var item in block!.Pause())if(!Record(item))return;
            if(!StopLink())return;
            if(block.State == ScenarioBlockState.Completed)finishedConditions.Add(CurrentCondition!);
            Transition(block.State == ScenarioBlockState.Completed ? SessionState.BlockEnded : SessionState.BlockPaused);
        }

        public void ResumeDevelopmentBlock()
        {
            RequireDevelopmentControls(); Require(SessionState.BlockPaused);
            // The simulated controller receives the remaining active duration,
            // never the paused wall time or a renewed full-length block.
            if(link is ISessionConditionLink conditionLink)
            {
                try {
                    if(!conditionLink.PrepareBlock(new SessionBlockLinkContext(plan.ParticipantId, CurrentBlockId!, CurrentCondition!,
                        checked((long)(block!.RemainingSeconds*TimeSpan.TicksPerSecond)))))
                    {Fail("ELTS link resume preparation failed.");return;}
                } catch(Exception e) {Fail("ELTS link resume preparation failed: "+e.Message);return;}
            }
            if(CurrentCondition!.StartsWith("WE_", StringComparison.Ordinal) && !TrySetLink(true))
            {Fail("WE link resume failed.");return;}
            foreach(var item in block!.Resume())if(!Record(item))return;
            Transition(SessionState.BlockRunning);
        }

        public void StopDevelopmentBlock()
        {
            RequireDevelopmentControls(); Tick();
            if(State == SessionState.BlockEnded)return;
            if(State != SessionState.BlockRunning && State != SessionState.BlockPaused)
                throw new InvalidOperationException("Only a running or paused test can stop.");
            if(!StopLink())return;
            foreach(var item in block!.Stop())if(!Record(item))return;
            CurrentBlockStoppedEarly = true;
            finishedConditions.Add(CurrentCondition!);
            Transition(SessionState.BlockEnded);
        }

        public static void ValidateDevelopmentPhaseDuration(double seconds)
        {
            if (Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0 || seconds > MaximumDevelopmentDurationSeconds)
                throw new ArgumentException("Practice and break durations must be between 0 and 3600 seconds.");
        }

        public void SetDevelopmentPhaseDurations(double practice, double rest)
        {
            RequireDevelopmentControls();
            ValidateDevelopmentPhaseDuration(practice); ValidateDevelopmentPhaseDuration(rest);
            if (State == SessionState.SessionComplete || State == SessionState.Aborted || State == SessionState.Failed)
                throw new InvalidOperationException("This session has closed.");
            if (!Record(Event("PhaseDurationsChanged", LogField.NumberValue("practiceSeconds", practice),
                LogField.NumberValue("breakSeconds", rest), LogField.Boolean("developmentTiming", true)))) return;
            // Editing a live break changes its total length, not its start time.
            practiceSeconds = practice; breakSeconds = rest;
        }

        public void SkipDevelopmentBreak()
        {
            RequireDevelopmentControls(); Require(SessionState.Break);
            if (!Record(Event("BreakSkipped", LogField.NumberValue("remainingSeconds", RemainingSeconds),
                LogField.String("afterBlockId", CurrentBlockId!)))) return;
            FinishBreak();
        }

        private void FinishBreak()
        {
            blockIndex++;
            rerun = CurrentCondition != null && attemptCounts.TryGetValue(CurrentCondition, out int count) ? count : 0;
            block = null; CurrentBlockSkipped = false; CurrentBlockStoppedEarly = false;
            Transition(blockIndex >= schedule.Count ? SessionState.SessionComplete : SessionState.BlockReady);
        }

        public void SkipDevelopmentTest(string reason)
        {
            RequireDevelopmentControls(); Require(SessionState.BlockReady);
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to skip a test.");
            if (!Record(Event("BlockSkipped", LogField.String("blockId", CurrentBlockId!),
                LogField.String("condition", CurrentCondition!), LogField.String("reason", reason.Trim())))) return;
            // Skipping is not a zero-score test and emits no BlockStarted/BlockEnded.
            attemptCounts[CurrentCondition!] = rerun + 1;
            finishedConditions.Add(CurrentCondition!);
            block = null; CurrentBlockSkipped = true; CurrentBlockStoppedEarly = false;
            Transition(SessionState.BlockEnded);
        }

        public void QueueDevelopmentRepeat(string condition, string reason)
        {
            RequireDevelopmentControls();
            if (!CanScheduleRepeat || !finishedConditions.Contains(condition))
                throw new InvalidOperationException("Choose a finished or skipped test between tests.");
            if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to repeat a test.");
            int position = State == SessionState.BlockReady ? blockIndex : blockIndex + 1;
            if (schedule.Skip(position).Contains(condition)) throw new InvalidOperationException("That test is already queued.");
            if (!Record(Event("TestRepeatQueued", LogField.String("condition", condition),
                LogField.String("reason", reason.Trim()), LogField.NumberValue("queueIndex", position)))) return;
            schedule.Insert(position, condition);
            if (State == SessionState.BlockReady)
            {
                rerun = attemptCounts[condition];
                block = null; CurrentBlockSkipped = false; CurrentBlockStoppedEarly = false;
            }
        }

        public void RestartDevelopmentPractice()
        {
            RequireDevelopmentControls();
            if (State != SessionState.Practice && !(State == SessionState.BlockReady && blockIndex == 0))
                throw new InvalidOperationException("Practice can restart before the first test.");
            if (!Record(Event("PracticeRestarted"))) return;
            phaseStarted = clock.Now;
            Transition(SessionState.Practice);
        }

        /// <summary>The caller must reserve a fresh recording before reopening a finished session.</summary>
        public void ReopenDevelopmentRepeat(string condition, string reason)
        {
            RequireDevelopmentControls(); Require(SessionState.SessionComplete);
            if (!finishedConditions.Contains(condition) || String.IsNullOrWhiteSpace(reason))
                throw new ArgumentException("Choose a finished test and provide a reason.");
            if (!Record(Event("SessionMetadata", LogField.String("participantId", plan.ParticipantId),
                LogField.String("conditionOrder", String.Join("|", plan.ConditionOrder))))) return;
            if (!Record(Event("TestRepeatQueued", LogField.String("condition", condition), LogField.String("reason", reason.Trim()),
                LogField.NumberValue("queueIndex", schedule.Count)))) return;
            schedule.Add(condition);
            rerun = attemptCounts[condition]; block = null;
            CurrentBlockSkipped = false; CurrentBlockStoppedEarly = false;
            Transition(SessionState.BlockReady);
        }

        public void FinishDevelopmentPractice()
        {
            RequireDevelopmentControls(); Require(SessionState.Practice);
            if (!Record(Event("PracticeFinishedEarly", LogField.NumberValue("remainingSeconds", RemainingSeconds)))) return;
            Transition(SessionState.BlockReady);
        }

        private void RequireDevelopmentControls()
        {
            if(!plan.AllowsDevelopmentOnlyDurationOverride)throw new InvalidOperationException("Flexible test controls require an explicit development session.");
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

            if ((State == SessionState.BlockRunning || State == SessionState.BlockPaused) && block != null)
            {
                foreach (var item in block.Abort(reason))
                    if (!Record(item)) return;
                if (State == SessionState.Failed || !StopLink()) return;
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
