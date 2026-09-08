#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Logging;

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
    private long sequence;
    private MonotonicTimestamp deadline;

    public ScenarioBlockController(ISharedClock clock, string blockId, int seed, double durationSeconds = 300, bool overrideDesignDuration = false)
    {
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identifier is required.", nameof(blockId));
        if (durationSeconds <= 0 || Double.IsNaN(durationSeconds) || Double.IsInfinity(durationSeconds)
            || (durationSeconds != 300 && !overrideDesignDuration))
            throw new ArgumentException("Block duration is fixed at 300 seconds unless an explicit development override is supplied.", nameof(durationSeconds));
        this.clock=clock; this.blockId=blockId; this.seed=seed;
        durationTicks=checked((long)(durationSeconds * TimeSpan.TicksPerSecond));
        State=ScenarioBlockState.Ready;
    }

    public ScenarioBlockState State { get; private set; }

    public IReadOnlyList<SessionEvent> Start()
    {
        if (State != ScenarioBlockState.Ready) throw new InvalidOperationException("A block can start only once.");
        var start=clock.Now;
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

    private SessionEvent Event(MonotonicTimestamp timestamp, string eventType, params LogField[] extra)
    {
        var fields=new List<LogField> { LogField.String("blockId", blockId) };
        fields.AddRange(extra);
        return new SessionEvent(++sequence, timestamp, eventType, fields);
    }
}
}
