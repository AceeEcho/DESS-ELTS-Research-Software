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
/// Session timing is entirely driven by the injected shared monotonic clock.
/// Normal completion is emitted at the exact deadline, even when an update is
/// delivered later; this preserves the half-open [start, end) scoring window.
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

    public ScenarioBlockController(ISharedClock clock, ISessionEventSequence sequence, string blockId, int seed, double durationSeconds = 300, bool allowDevelopmentOnlyDurationOverride = false)
    {
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (sequence == null) throw new ArgumentNullException(nameof(sequence));
        if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identifier is required.", nameof(blockId));
        if (durationSeconds <= 0 || Double.IsNaN(durationSeconds) || Double.IsInfinity(durationSeconds)
            || (durationSeconds != 300 && !allowDevelopmentOnlyDurationOverride))
            throw new ArgumentException("Block duration is fixed at 300 seconds unless an explicit development override is supplied.", nameof(durationSeconds));
        this.clock=clock; this.sequence=sequence; this.blockId=blockId; this.seed=seed;
        durationTicks=checked((long)(durationSeconds * TimeSpan.TicksPerSecond));
        State=ScenarioBlockState.Ready;
    }

    public ScenarioBlockState State { get; private set; }

    public IReadOnlyList<SessionEvent> Start()
    {
        if (State != ScenarioBlockState.Ready) throw new InvalidOperationException("A block can start only once.");
        var start=clock.Now;
        startedAt=start;
        deadline=new MonotonicTimestamp(checked(start.Ticks+durationTicks));
        State=ScenarioBlockState.Running;
        return new[] { Event(start, "BlockStarted", LogField.NumberValue("seed", seed)) };
    }

    public IReadOnlyList<SessionEvent> Update()
    {
        if (State != ScenarioBlockState.Running || clock.Now < deadline) return Array.Empty<SessionEvent>();
        State=ScenarioBlockState.Completed;
        return new[] { Event(deadline, "BlockEnded") };
    }

    public IReadOnlyList<SessionEvent> Abort(string reason)
    {
        if (State != ScenarioBlockState.Running) throw new InvalidOperationException("Only a running block can be aborted.");
        if (String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An abort reason is required.", nameof(reason));
        State=ScenarioBlockState.Aborted;
        return new[] { Event(clock.Now, "BlockAborted", LogField.String("reason", reason)) };
    }

    /// <summary>Only this block-owned path may add shot events to the active block.</summary>
    public IReadOnlyList<SessionEvent> Fire(IShotModel shotModel, ShotContext shot, IEnumerable<TargetEntity> targets, Func<TargetEntity, bool> isVisible)
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
        var fields=new List<LogField> { LogField.String("blockId", blockId) };
        fields.AddRange(extra);
        return new SessionEvent(sequence.Next(), timestamp, eventType, fields);
    }
}

public enum SessionState { Idle, SessionSetup, Calibration, Practice, BlockReady, BlockRunning, BlockEnded, Break, SessionComplete, Aborted, Failed }

public sealed class SessionTiming
{
    public SessionTiming(double practiceSeconds, double breakSeconds, bool allowDevelopmentOnlyTiming = false)
    { if (practiceSeconds < 0 || breakSeconds < 0 || Double.IsNaN(practiceSeconds) || Double.IsInfinity(practiceSeconds) || Double.IsNaN(breakSeconds) || Double.IsInfinity(breakSeconds)) throw new ArgumentOutOfRangeException(); PracticeSeconds=practiceSeconds; BreakSeconds=breakSeconds; AllowDevelopmentOnlyTiming=allowDevelopmentOnlyTiming; }
    public double PracticeSeconds { get; } public double BreakSeconds { get; } public bool AllowDevelopmentOnlyTiming { get; }
}

public sealed class SessionReadinessInput
{
    public bool TwoDisplays { get; set; } public bool SteamVrRunning { get; set; } public bool TrackersBoundAndValidForTwoSeconds { get; set; }
    public bool ConfigurationHashesComputed { get; set; } public long DiskFreeBytes { get; set; } public bool LogWriterAlive { get; set; } public bool WeLinkReachable { get; set; }
}
public sealed class SessionReadinessReport
{
    internal SessionReadinessReport(IReadOnlyList<string> failures) { Failures=failures; IsReady=failures.Count==0; StudyReady=false; }
    public bool IsReady { get; } public bool StudyReady { get; } public IReadOnlyList<string> Failures { get; }
    public static SessionReadinessReport Evaluate(SessionReadinessInput input, bool containsWeBlock)
    { if(input==null) throw new ArgumentNullException(nameof(input)); var failures=new List<string>(); if(!input.TwoDisplays) failures.Add("Two displays are required."); if(!input.SteamVrRunning) failures.Add("SteamVR is not running."); if(!input.TrackersBoundAndValidForTwoSeconds) failures.Add("Both bound trackers must be valid for at least 2 seconds."); if(!input.ConfigurationHashesComputed) failures.Add("Configuration hashes are missing."); if(input.DiskFreeBytes < 2L*1024*1024*1024) failures.Add("At least 2 GB free disk space is required."); if(!input.LogWriterAlive) failures.Add("The log writer is not alive."); if(containsWeBlock&&!input.WeLinkReachable) failures.Add("The WE link is not reachable."); return new SessionReadinessReport(failures); }
}
public interface ISessionEventSink { bool TryRecord(SessionEvent item); }
public interface ISessionLink { bool TrySetEnabled(bool enabled); }
public interface ISessionRecordingLifecycle { Task<bool> ReserveAndStartAsync(string runId); Task<bool> CloseAsync(); }

/// <summary>Pure operator-driven session state machine. Sinks own I/O; failed event or link delivery fails closed.</summary>
public sealed class SessionEngine
{
    private readonly ISharedClock clock; private readonly ISessionEventSequence sequence; private readonly SessionPlan plan; private readonly SessionTiming timing; private readonly ISessionEventSink sink; private readonly ISessionLink? link;
    private ScenarioBlockController? block; private MonotonicTimestamp phaseStarted; private int blockIndex; private int rerun; private string? failure;
    public SessionEngine(ISharedClock clock, ISessionEventSequence sequence, SessionPlan plan, SessionTiming timing, ISessionEventSink sink, ISessionLink? link = null)
    { this.clock=clock??throw new ArgumentNullException(nameof(clock)); this.sequence=sequence??throw new ArgumentNullException(nameof(sequence)); this.plan=plan??throw new ArgumentNullException(nameof(plan)); this.timing=timing??throw new ArgumentNullException(nameof(timing)); this.sink=sink??throw new ArgumentNullException(nameof(sink)); this.link=link; State=SessionState.Idle; }
    public SessionState State { get; private set; } public string? CurrentCondition => blockIndex<plan.ConditionOrder.Count ? plan.ConditionOrder[blockIndex] : null; public int CurrentBlockIndex => blockIndex; public int RerunIndex => rerun; public string? Failure => failure; public bool TargetsActive => State==SessionState.BlockRunning; public double RemainingSeconds { get { if(State==SessionState.BlockRunning&&block!=null) return Math.Max(0,plan.ConditionDurationSeconds-(clock.Now.Elapsed-phaseStarted.Elapsed).TotalSeconds); if(State==SessionState.Practice) return Math.Max(0,timing.PracticeSeconds-(clock.Now.Elapsed-phaseStarted.Elapsed).TotalSeconds); if(State==SessionState.Break) return Math.Max(0,timing.BreakSeconds-(clock.Now.Elapsed-phaseStarted.Elapsed).TotalSeconds); return 0; } } public bool CanAdvance => State==SessionState.SessionSetup||State==SessionState.Calibration||State==SessionState.BlockEnded||State==SessionState.Break;
    public SessionReadinessReport StartSetup(SessionReadinessInput readiness) { Require(SessionState.Idle); var report=SessionReadinessReport.Evaluate(readiness,plan.ConditionOrder.Any(c=>c.StartsWith("WE_",StringComparison.Ordinal))); if(!report.IsReady) return report; Transition(SessionState.SessionSetup); return report; }
    public void Advance() { if(State==SessionState.SessionSetup) Transition(SessionState.Calibration); else if(State==SessionState.Calibration) { phaseStarted=clock.Now; Transition(SessionState.Practice); } else if(State==SessionState.BlockEnded) { phaseStarted=clock.Now; Transition(SessionState.Break); } else if(State==SessionState.Break) { if(clock.Now.Elapsed-phaseStarted.Elapsed<TimeSpan.FromSeconds(timing.BreakSeconds)) return; blockIndex++; rerun=0; if(blockIndex>=plan.ConditionOrder.Count) Transition(SessionState.SessionComplete); else Transition(SessionState.BlockReady); } else throw new InvalidOperationException("This state cannot advance."); }
    public void Tick() { if(State==SessionState.Practice&&clock.Now.Elapsed-phaseStarted.Elapsed>=TimeSpan.FromSeconds(timing.PracticeSeconds)) Transition(SessionState.BlockReady); else if(State==SessionState.BlockRunning&&block!=null) { foreach(var item in block.Update()) Record(item); if(block.State==ScenarioBlockState.Completed) { StopLink(); Transition(SessionState.BlockEnded); } } }
    public void StartBlock() { Require(SessionState.BlockReady); string condition=CurrentCondition??throw new InvalidOperationException(); if(condition.StartsWith("WE_",StringComparison.Ordinal)&&(link==null||!link.TrySetEnabled(true))) { Fail("WE link start failed."); return; } block=new ScenarioBlockController(clock,sequence,condition,DeterministicSeedFor(condition),plan.ConditionDurationSeconds,plan.AllowsDevelopmentOnlyDurationOverride); phaseStarted=clock.Now; Transition(SessionState.BlockRunning); foreach(var item in block.Start()) Record(item); }
    public void EndBlock() { if(State!=SessionState.BlockRunning) throw new InvalidOperationException(); Tick(); }
    public void Abort(string reason) { if(String.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An abort reason is required.",nameof(reason)); if(State==SessionState.Aborted) return; if(State==SessionState.BlockRunning&&block!=null) foreach(var item in block.Abort(reason)) Record(item); StopLink(); Transition(SessionState.Aborted,LogField.String("reason",reason)); }
    public void RerunCurrentBlock() { if(State!=SessionState.Aborted&&State!=SessionState.BlockEnded) throw new InvalidOperationException("Only an aborted or ended block can rerun."); rerun++; block=null; Transition(SessionState.BlockReady,LogField.NumberValue("rerunIndex",rerun)); }
    public void AddNote(string text) { if(String.IsNullOrWhiteSpace(text)) throw new ArgumentException("A note is required.",nameof(text)); Record(Event("OperatorNote",LogField.String("text",text))); }
    public void EmitProvisionalSyncMarker(string text) { Record(Event("SyncMarker",LogField.String("label",String.IsNullOrWhiteSpace(text)?"provisional":text),LogField.Boolean("provisional",true))); }
    private void Transition(SessionState next, params LogField[] extra) { var fields=new List<LogField>{LogField.String("from",State.ToString()),LogField.String("to",next.ToString())}; fields.AddRange(extra); Record(Event("SessionTransition",fields.ToArray())); if(State!=SessionState.Failed) State=next; }
    private SessionEvent Event(string type,params LogField[] fields) => new SessionEvent(sequence.Next(),clock.Now,type,fields);
    private void Record(SessionEvent item) { try { if(!sink.TryRecord(item)) { Fail("Session event logging failed."); } } catch(Exception e) { Fail("Session event logging failed: "+e.Message); } }
    private void StopLink() { if(CurrentCondition!=null&&CurrentCondition.StartsWith("WE_",StringComparison.Ordinal)&&link!=null&&!link.TrySetEnabled(false)) Fail("WE link stop failed."); }
    private void Fail(string message) { failure=message; State=SessionState.Failed; }
    private void Require(SessionState expected) { if(State!=expected) throw new InvalidOperationException("Invalid session state transition."); }
    private static int DeterministicSeedFor(string condition) { unchecked { int value=17; foreach(char c in condition) value=value*31+c; return value&Int32.MaxValue; } }
}
}
