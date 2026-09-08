#nullable enable
using System;
using System.Collections.Generic;
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
}
