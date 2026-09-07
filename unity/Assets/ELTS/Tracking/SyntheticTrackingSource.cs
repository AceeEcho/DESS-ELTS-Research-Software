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

public readonly struct SyntheticMotionSettings
{
    public readonly Vector3d HeadBasePosition, HeadSwayAmplitude, WeaponBasePosition, WeaponSwayAmplitude;
    public readonly double HeadSwayFrequencyHz, WeaponSwayFrequencyHz, WeaponYawAmplitudeRadians, WeaponYawFrequencyHz;
    public static SyntheticMotionSettings Defaults => new(new Vector3d(0, 1.6, 0), new Vector3d(0.02, 0.01, 0), 1.7, new Vector3d(0.2, 1.2, 0.5), new Vector3d(0.03, 0.02, 0), 1.3, 0.04, 1.3);
    public SyntheticMotionSettings(Vector3d headBasePosition, Vector3d headSwayAmplitude, double headSwayFrequencyHz, Vector3d weaponBasePosition, Vector3d weaponSwayAmplitude, double weaponSwayFrequencyHz, double weaponYawAmplitudeRadians, double weaponYawFrequencyHz)
    {
        if (!headBasePosition.IsFinite || !headSwayAmplitude.IsFinite || !weaponBasePosition.IsFinite || !weaponSwayAmplitude.IsFinite || !Finite(headSwayFrequencyHz) || !Finite(weaponSwayFrequencyHz) || !Finite(weaponYawAmplitudeRadians) || !Finite(weaponYawFrequencyHz) || headSwayFrequencyHz < 0 || weaponSwayFrequencyHz < 0 || weaponYawAmplitudeRadians < 0 || weaponYawFrequencyHz < 0) throw new ArgumentOutOfRangeException("Synthetic motion settings must be finite and nonnegative.");
        HeadBasePosition = headBasePosition; HeadSwayAmplitude = headSwayAmplitude; HeadSwayFrequencyHz = headSwayFrequencyHz; WeaponBasePosition = weaponBasePosition; WeaponSwayAmplitude = weaponSwayAmplitude; WeaponSwayFrequencyHz = weaponSwayFrequencyHz; WeaponYawAmplitudeRadians = weaponYawAmplitudeRadians; WeaponYawFrequencyHz = weaponYawFrequencyHz;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
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
    public SyntheticMotionSettings Motion { get; }
    public int TriggerQueueCapacity { get; }
    public IReadOnlyList<SyntheticDropout> Dropouts { get; }
    public IReadOnlyList<SyntheticTriggerWindow> TriggerWindows { get; }

    public SyntheticTrackingSettings(ISharedClock clock, int seed = DefaultSeed, double sampleRateHz = DefaultSampleRateHz,
        string headTracker = DefaultHeadTracker, string weaponTracker = DefaultWeaponTracker,
        TimeSpan? triggerLockout = null, IEnumerable<SyntheticDropout>? dropouts = null,
        IEnumerable<SyntheticTriggerWindow>? triggerWindows = null, SyntheticMotionSettings? motion = null, int triggerQueueCapacity = 64)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (seed < 0) throw new ArgumentOutOfRangeException(nameof(seed));
        if (!double.IsFinite(sampleRateHz) || sampleRateHz < 1 || sampleRateHz > 2000) throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
        if (string.IsNullOrWhiteSpace(headTracker) || string.IsNullOrWhiteSpace(weaponTracker) || string.Equals(headTracker, weaponTracker, StringComparison.Ordinal)) throw new ArgumentException("Synthetic tracker identities must be nonempty and distinct.");
        var lockout = triggerLockout ?? TimeSpan.FromMilliseconds(20);
        if (lockout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(triggerLockout));
        if (triggerQueueCapacity < 1 || triggerQueueCapacity > 10000) throw new ArgumentOutOfRangeException(nameof(triggerQueueCapacity));
        Seed = seed; SampleRateHz = sampleRateHz; HeadTracker = new TrackerId(headTracker); WeaponTracker = new TrackerId(weaponTracker); TriggerLockout = lockout;
        Motion = motion ?? SyntheticMotionSettings.Defaults; TriggerQueueCapacity = triggerQueueCapacity;
        Dropouts = Copy(dropouts); TriggerWindows = Copy(triggerWindows);
        foreach (var item in Dropouts) if (item.FirstSample < 1 || item.SampleCount < 1) throw new ArgumentException("Dropout list contains a default or invalid window.");
        foreach (var item in TriggerWindows) if (item.FirstSample < 1 || item.LastSample < item.FirstSample) throw new ArgumentException("Trigger list contains a default or invalid window.");
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
    private long nextDueTicks;
    private bool hasAcquisition;
    private bool previousPressed;
    private MonotonicTimestamp lastTrigger = MonotonicTimestamp.Zero;
    private bool hasTrigger;
    public long SkippedAcquisitionCount { get; private set; }
    public long DroppedTriggerCount { get; private set; }
    public bool TriggerOverflowed { get; private set; }
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
        var now = settings.Clock.Now;
        var intervalTicks = Math.Max(1L, (long)Math.Ceiling(TimeSpan.TicksPerSecond / settings.SampleRateHz));
        if (hasAcquisition && now.Ticks < nextDueTicks) return false;
        if (hasAcquisition && now.Ticks > nextDueTicks)
        {
            var elapsed = now.Ticks - nextDueTicks;
            SkippedAcquisitionCount += elapsed / intervalTicks + (elapsed % intervalTicks == 0 ? 0 : 1);
        }
        hasAcquisition = true;
        nextDueTicks = now.Ticks > long.MaxValue - intervalTicks ? long.MaxValue : now.Ticks + intervalTicks;
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
            var motion = settings.Motion;
            var headSway = Math.Sin(t * motion.HeadSwayFrequencyHz + phase);
            var head = new RigidPose(motion.HeadBasePosition + new Vector3d(motion.HeadSwayAmplitude.X * headSway, motion.HeadSwayAmplitude.Y * Math.Cos(t * motion.HeadSwayFrequencyHz * 0.65 + phase), motion.HeadSwayAmplitude.Z * headSway), Quaterniond.Identity);
            var weaponSway = Math.Sin(t * motion.WeaponSwayFrequencyHz + phase);
            var weapon = new RigidPose(motion.WeaponBasePosition + new Vector3d(motion.WeaponSwayAmplitude.X * Math.Cos(t * motion.WeaponSwayFrequencyHz + phase), motion.WeaponSwayAmplitude.Y * weaponSway, motion.WeaponSwayAmplitude.Z * weaponSway), Quaterniond.FromAxisAngle(new Vector3d(0, 1, 0), motion.WeaponYawAmplitudeRadians * Math.Sin(t * motion.WeaponYawFrequencyHz + phase)));
            pending.Enqueue(TrackingSample.Valid(stamp, settings.HeadTracker, head));
            pending.Enqueue(TrackingSample.Valid(stamp, settings.WeaponTracker, weapon));
        }
        var pressed = false;
        foreach (var window in settings.TriggerWindows) if (window.Contains(sampleNumber)) { pressed = true; break; }
        if (dropped) previousPressed = false;
        else if (previousPressed && !pressed && (!hasTrigger || stamp.Timestamp.Ticks - lastTrigger.Ticks >= settings.TriggerLockout.Ticks))
        {
            if (triggers.Count < settings.TriggerQueueCapacity) { triggers.Enqueue(new SyntheticTriggerEvent(stamp, "falling")); lastTrigger = stamp.Timestamp; hasTrigger = true; }
            else { DroppedTriggerCount++; TriggerOverflowed = true; }
        }
        if (!dropped) previousPressed = pressed;
        return true;
    }
    public void Dispose() { disposed = true; pending.Clear(); triggers.Clear(); }
}
}
