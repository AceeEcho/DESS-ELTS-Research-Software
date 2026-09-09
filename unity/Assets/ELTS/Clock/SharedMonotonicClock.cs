#nullable enable
using System;
using System.Diagnostics;
using System.Threading;

namespace Elts.Clock
{
/// <summary>
/// Nonnegative elapsed time from the one process startup clock. Ticks use the
/// TimeSpan scale (100 ns) but must never be interpreted as UTC ticks.
/// </summary>
public readonly struct MonotonicTimestamp : IComparable<MonotonicTimestamp>, IEquatable<MonotonicTimestamp>
{
    public readonly long Ticks;
    public MonotonicTimestamp(long ticks)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks), "Monotonic timestamps cannot be negative.");
        Ticks = ticks;
    }
    public static MonotonicTimestamp Zero => new MonotonicTimestamp(0);
    public TimeSpan Elapsed => TimeSpan.FromTicks(Ticks);
    public int CompareTo(MonotonicTimestamp other) => Ticks.CompareTo(other.Ticks);
    public bool Equals(MonotonicTimestamp other) => Ticks == other.Ticks;
    public override bool Equals(object? obj) => obj is MonotonicTimestamp other && Equals(other);
    public override int GetHashCode() => Ticks.GetHashCode();
    public override string ToString() => Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static bool operator ==(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks == right.Ticks;
    public static bool operator !=(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks != right.Ticks;
    public static bool operator <(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks < right.Ticks;
    public static bool operator >(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks > right.Ticks;
    public static bool operator <=(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks <= right.Ticks;
    public static bool operator >=(MonotonicTimestamp left, MonotonicTimestamp right) => left.Ticks >= right.Ticks;
}

/// <summary>
/// The sole timestamp authority for Unity samples and events. UTC is captured
/// once at startup for later alignment metadata; event ordering uses Now only.
/// </summary>
public interface ISharedClock
{
    DateTimeOffset UtcStartupAnchor { get; }
    MonotonicTimestamp Now { get; }
}

/// <summary>
/// Production shared clock backed by Stopwatch. The implementation clamps a
/// concurrent caller to the greatest timestamp already returned, so consumers
/// never observe a decreasing sequence even across threads.
/// </summary>
public sealed class SharedMonotonicClock : ISharedClock
{
    private readonly long startStopwatchTicks;
    private long lastReturnedTicks;

    public SharedMonotonicClock()
    {
        // The anchor is alignment metadata only. Never derive runtime ordering
        // from wall clock time because it can jump when the system clock changes.
        startStopwatchTicks = Stopwatch.GetTimestamp();
        UtcStartupAnchor = DateTimeOffset.UtcNow;
        lastReturnedTicks = 0;
    }

    public DateTimeOffset UtcStartupAnchor { get; }

    public MonotonicTimestamp Now
    {
        get
        {
            long elapsedStopwatchTicks = Stopwatch.GetTimestamp() - startStopwatchTicks;
            if (elapsedStopwatchTicks < 0) elapsedStopwatchTicks = 0;
            long candidate = ToTimeSpanTicks(elapsedStopwatchTicks);
            while (true)
            {
                long observed = Volatile.Read(ref lastReturnedTicks);
                if (candidate <= observed) return new MonotonicTimestamp(observed);
                if (Interlocked.CompareExchange(ref lastReturnedTicks, candidate, observed) == observed)
                    return new MonotonicTimestamp(candidate);
            }
        }
    }

    private static long ToTimeSpanTicks(long stopwatchTicks)
    {
        // Split the quotient to avoid multiplying a long-running Stopwatch
        // count before division and overflowing a 64-bit integer.
        long seconds = stopwatchTicks / Stopwatch.Frequency;
        long remainder = stopwatchTicks % Stopwatch.Frequency;
        if (seconds > long.MaxValue / TimeSpan.TicksPerSecond) return long.MaxValue;
        return seconds * TimeSpan.TicksPerSecond + remainder * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    }
}

/// <summary>
/// Deterministic clock for unit and integration fixtures. Tests advance time
/// explicitly; it cannot run backward or consult the operating-system clock.
/// </summary>
public sealed class ManualSharedClock : ISharedClock
{
    private long currentTicks;

    public ManualSharedClock(DateTimeOffset utcStartupAnchor)
    {
        UtcStartupAnchor = utcStartupAnchor.ToUniversalTime();
        currentTicks = 0;
    }

    public DateTimeOffset UtcStartupAnchor { get; }
    public MonotonicTimestamp Now => new MonotonicTimestamp(Volatile.Read(ref currentTicks));

    public MonotonicTimestamp Advance(TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount), "A manual monotonic clock cannot move backward.");
        return AdvanceTicks(amount.Ticks);
    }

    public MonotonicTimestamp AdvanceTicks(long ticks)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks), "A manual monotonic clock cannot move backward.");
        while (true)
        {
            long observed = Volatile.Read(ref currentTicks);
            if (ticks > long.MaxValue - observed) throw new OverflowException("Manual clock elapsed time exceeds the timestamp range.");
            long next = observed + ticks;
            if (Interlocked.CompareExchange(ref currentTicks, next, observed) == observed)
                return new MonotonicTimestamp(next);
        }
    }
}
}
