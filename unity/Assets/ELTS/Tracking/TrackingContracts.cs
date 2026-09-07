#nullable enable
using System;
using Elts.Clock;
using Elts.Geometry;

namespace Elts.Tracking
{
/// <summary>Hardware identity recorded with every tracking sample; it is normally a configured serial number.</summary>
public readonly struct TrackerId : IEquatable<TrackerId>
{
    public readonly string Value;
    public TrackerId(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A tracker identifier is required.", nameof(value));
        Value = value;
    }
    public bool IsAssigned => !string.IsNullOrWhiteSpace(Value);
    public bool Equals(TrackerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is TrackerId other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value;
}

/// <summary>Why a sample has no usable room-frame pose. Invalid samples remain timestamped and loggable.</summary>
public enum TrackingValidity
{
    Valid = 0,
    Unavailable = 1,
    OutOfRange = 2,
    DriverFault = 3,
    RejectedNativePose = 4
}

/// <summary>
/// Immutable room-frame sample. A valid sample always has a Unity-convention
/// pose; an invalid sample never carries a pose that a downstream consumer could use.
/// </summary>
public readonly struct TrackingSample
{
    public TrackerId Tracker { get; }
    public MonotonicTimestamp Timestamp { get; }
    public TrackingValidity Validity { get; }
    public RigidPose? Pose { get; }
    public bool HasValidPose => Validity == TrackingValidity.Valid;

    private TrackingSample(TrackerId tracker, MonotonicTimestamp timestamp, TrackingValidity validity, RigidPose? pose)
    {
        Tracker = tracker;
        Timestamp = timestamp;
        Validity = validity;
        Pose = pose;
    }

    public static TrackingSample Valid(ISharedClock clock, TrackerId tracker, RigidPose roomPose)
    {
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (!tracker.IsAssigned) throw new ArgumentException("A valid tracking sample requires a tracker identifier.", nameof(tracker));
        if (!roomPose.Orientation.IsUnit) throw new ArgumentException("A valid tracking sample requires a valid room-frame pose.", nameof(roomPose));
        return new TrackingSample(tracker, clock.Now, TrackingValidity.Valid, roomPose);
    }

    public static TrackingSample Invalid(ISharedClock clock, TrackerId tracker, TrackingValidity validity)
    {
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (!tracker.IsAssigned) throw new ArgumentException("An invalid tracking sample requires a tracker identifier.", nameof(tracker));
        if (!Enum.IsDefined(typeof(TrackingValidity), validity) || validity == TrackingValidity.Valid)
            throw new ArgumentException("Invalid samples require an explicit invalid validity value.", nameof(validity));
        return new TrackingSample(tracker, clock.Now, validity, null);
    }
}

/// <summary>
/// A source has one injected shared clock and emits samples through this
/// interface. Implementations must call the TrackingSample factories so every
/// valid and invalid observation uses that source's clock authority.
/// </summary>
public interface ITrackingSource : IDisposable
{
    ISharedClock Clock { get; }
    bool TryGetNext(out TrackingSample sample);
}

/// <summary>Native OpenVR-space position. Units are meters; it is not a Unity room vector.</summary>
public readonly struct NativeTrackingVector
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;
    public NativeTrackingVector(double x, double y, double z)
    {
        if (!Finite(x) || !Finite(y) || !Finite(z)) throw new ArgumentException("Native position components must be finite.");
        X = x;
        Y = y;
        Z = z;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Native OpenVR-space orientation, in x/y/z/w order.</summary>
public readonly struct NativeTrackingQuaternion
{
    public readonly double X;
    public readonly double Y;
    public readonly double Z;
    public readonly double W;
    public NativeTrackingQuaternion(double x, double y, double z, double w)
    {
        if (!Finite(x) || !Finite(y) || !Finite(z) || !Finite(w)) throw new ArgumentException("Native orientation components must be finite.");
        X = x;
        Y = y;
        Z = z;
        W = w;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Raw native pose received from an OpenVR adapter before room-frame conversion.</summary>
public readonly struct NativeTrackingPose
{
    public readonly NativeTrackingVector Position;
    public readonly NativeTrackingQuaternion Orientation;
    public NativeTrackingPose(NativeTrackingVector position, NativeTrackingQuaternion orientation)
    {
        Position = position;
        Orientation = orientation;
    }
}

/// <summary>
/// The only native-OpenVR to Unity-handedness boundary. It reflects the native
/// Z axis once, yielding the ELTS room frame: +X right, +Y up, +Z forward, meters.
/// No downstream module may receive a NativeTrackingPose or apply a second conversion.
/// </summary>
public static class TrackingCoordinateConverter
{
    public static RigidPose Convert(NativeTrackingPose nativePose)
    {
        return new RigidPose(
            new Vector3d(nativePose.Position.X, nativePose.Position.Y, -nativePose.Position.Z),
            new Quaterniond(-nativePose.Orientation.X, -nativePose.Orientation.Y, nativePose.Orientation.Z, nativePose.Orientation.W));
    }
}
}
