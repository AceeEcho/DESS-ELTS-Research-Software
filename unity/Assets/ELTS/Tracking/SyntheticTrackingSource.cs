#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;

namespace Elts.Tracking
{

public readonly struct SyntheticDropout
{
    public readonly long FirstSample;
    public readonly long SampleCount;
    public SyntheticDropout(long firstSample, long sampleCount)
    {
        if (firstSample < 1 || sampleCount < 1) throw new ArgumentOutOfRangeException("Dropout windows must be positive and one-based.");
        FirstSample = firstSample; SampleCount = sampleCount;
    }
    public bool Contains(long sample) => sample >= FirstSample && sample - FirstSample < SampleCount;
}

public readonly struct SyntheticTriggerWindow
{
    public readonly long FirstSample;
    public readonly long LastSample;
    public SyntheticTriggerWindow(long firstSample, long lastSample)
    {
        if (firstSample < 1 || lastSample < firstSample) throw new ArgumentOutOfRangeException("Trigger windows must be positive and ordered.");
        FirstSample = firstSample; LastSample = lastSample;
    }
    public bool Contains(long sample) => sample >= FirstSample && sample <= LastSample;
}

/// <summary>Immutable fixture settings. Values describe synthetic room-frame data only.</summary>
public sealed class SyntheticTrackingSettings
{
    public const double DefaultSampleRateHz = 250;
    public const int DefaultSeed = 1729;
    public const string DefaultHeadTracker = "SYNTHETIC-HEAD";
    public const string DefaultWeaponTracker = "SYNTHETIC-WEAPON";
    public ISharedClock Clock { get; }
    public int Seed { get; }
    public double SampleRateHz { get; }
    public TrackerId HeadTracker { get; }
    public TrackerId WeaponTracker { get; }
    public TimeSpan TriggerLockout { get; }
    public IReadOnlyList<SyntheticDropout> Dropouts { get; }
    public IReadOnlyList<SyntheticTriggerWindow> TriggerWindows { get; }

    public SyntheticTrackingSettings(ISharedClock clock, int seed = DefaultSeed, double sampleRateHz = DefaultSampleRateHz,
        string headTracker = DefaultHeadTracker, string weaponTracker = DefaultWeaponTracker,
        TimeSpan? triggerLockout = null, IEnumerable<SyntheticDropout>? dropouts = null,
        IEnumerable<SyntheticTriggerWindow>? triggerWindows = null)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (seed < 0) throw new ArgumentOutOfRangeException(nameof(seed));
        if (!double.IsFinite(sampleRateHz) || sampleRateHz < 1 || sampleRateHz > 2000) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        if (string.IsNullOrWhiteSpace(headTracker) || string.IsNullOrWhiteSpace(weaponTracker) || string.Equals(headTracker, weaponTracker, StringComparison.Ordinal)) throw new ArgumentException("Synthetic tracker identities must be nonempty and distinct.");
        var lockout = triggerLockout ?? TimeSpan.FromMilliseconds(20);
        if (lockout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(triggerLockout));
        Seed = seed; SampleRateHz = sampleRateHz; HeadTracker = new TrackerId(headTracker); WeaponTracker = new TrackerId(weaponTracker); TriggerLockout = lockout;
        Dropouts = Copy(dropouts); TriggerWindows = Copy(triggerWindows);
    }
    private static IReadOnlyList<T> Copy<T>(IEnumerable<T>? values)
    {
        var result = values == null ? new List<T>() : new List<T>(values);
        return result.AsReadOnly();
    }
}

public readonly struct SyntheticTriggerEvent
{
    public readonly TrackingAcquisitionStamp Acquisition;
    public readonly string Edge;
    public SyntheticTriggerEvent(TrackingAcquisitionStamp acquisition, string edge) { Acquisition = acquisition; Edge = edge; }
}

/// <summary>Deterministic raw source. It never converts native poses or predicts logged samples.</summary>
public sealed class SyntheticTrackingSource : ITrackingSource
{
    private readonly SyntheticTrackingSettings settings;
    private readonly Queue<TrackingSample> pending = new();
    private readonly Queue<SyntheticTriggerEvent> triggers = new();
    private readonly double phase;
    private long sampleNumber;
    private long sequence;
    private bool previousPressed;
    private MonotonicTimestamp lastTrigger = MonotonicTimestamp.Zero;
    private bool hasTrigger;
    private bool disposed;

    public SyntheticTrackingSource(SyntheticTrackingSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        phase = (settings.Seed % 360) * Math.PI / 180.0;
    }
    public ISharedClock Clock => settings.Clock;
    public TrackingSourceIdentity Source => new("synthetic");
    public bool TryGetNext(out TrackingSample sample)
    {
        if (disposed || !EnsurePending()) { sample = default; return false; }
        sample = pending.Dequeue(); return true;
    }
    public bool TryGetNextTrigger(out SyntheticTriggerEvent trigger)
    {
        if (disposed || triggers.Count == 0) { trigger = default; return false; }
        trigger = triggers.Dequeue(); return true;
    }
    private bool EnsurePending()
    {
        if (pending.Count > 0) return true;
        sampleNumber++; sequence++;
        var stamp = TrackingAcquisitionStamp.Capture(settings.Clock, sequence);
        var dropped = false;
        foreach (var window in settings.Dropouts) if (window.Contains(sampleNumber)) { dropped = true; break; }
        if (dropped)
        {
            pending.Enqueue(TrackingSample.Invalid(stamp, settings.HeadTracker, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable));
            pending.Enqueue(TrackingSample.Invalid(stamp, settings.WeaponTracker, TrackingConnectionState.Disconnected, TrackingValidity.Unavailable));
        }
        else
        {
            var t = (sampleNumber - 1) / settings.SampleRateHz;
            var sway = Math.Sin(t * 1.7 + phase);
            var head = new RigidPose(new Vector3d(0.02 * sway, 1.6 + 0.01 * Math.Cos(t * 1.1 + phase), 0), Quaterniond.Identity);
            var weapon = new RigidPose(new Vector3d(0.2 + 0.03 * Math.Cos(t * 1.3 + phase), 1.2 + 0.02 * sway, 0.5), Quaterniond.FromAxisAngle(new Vector3d(0, 1, 0), 0.04 * sway));
            pending.Enqueue(TrackingSample.Valid(stamp, settings.HeadTracker, head));
            pending.Enqueue(TrackingSample.Valid(stamp, settings.WeaponTracker, weapon));
        }
        var pressed = false;
        foreach (var window in settings.TriggerWindows) if (window.Contains(sampleNumber)) { pressed = true; break; }
        if (previousPressed && !pressed && (!hasTrigger || stamp.Timestamp.Ticks - lastTrigger.Ticks >= settings.TriggerLockout.Ticks))
        { triggers.Enqueue(new SyntheticTriggerEvent(stamp, "falling")); lastTrigger = stamp.Timestamp; hasTrigger = true; }
        previousPressed = pressed;
        return true;
    }
    public void Dispose() { disposed = true; pending.Clear(); triggers.Clear(); }
}
}
