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
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Transport connection state recorded alongside every tracking sample.</summary>
public enum TrackingConnectionState
{
    Disconnected = 0,
    Connected = 1,
    Reconnecting = 2
}

/// <summary>Why a sample has no usable room-frame pose. Invalid samples remain timestamped and loggable.</summary>
public enum TrackingValidity
{
    Unavailable = 0,
    Valid = 1,
    OutOfRange = 2,
    DriverFault = 3,
    RejectedNativePose = 4
}

/// <summary>
/// Shared capture identity for all tracker observations emitted by one native
/// poll. Sequence starts at one and increases in source order; paired head and
/// weapon observations reuse this exact stamp rather than reading the clock twice.
/// </summary>
public readonly struct TrackingAcquisitionStamp
{
    public long Sequence { get; }
    public MonotonicTimestamp Timestamp { get; }
    private TrackingAcquisitionStamp(long sequence, MonotonicTimestamp timestamp)
    {
        Sequence = sequence;
        Timestamp = timestamp;
    }
    public static TrackingAcquisitionStamp Capture(ISharedClock clock, long sequence)
    {
        if (clock == null) throw new ArgumentNullException(nameof(clock));
        if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence), "Tracking acquisition sequence starts at one.");
        return new TrackingAcquisitionStamp(sequence, clock.Now);
    }
    public bool IsAssigned => Sequence > 0;
}

/// <summary>
/// Immutable room-frame sample. A valid sample always has a Unity-convention
/// pose; an invalid sample never carries a pose that a downstream consumer could use.
/// </summary>
public readonly struct TrackingSample
{
    public TrackerId Tracker { get; }
    public TrackingAcquisitionStamp Acquisition { get; }
    public MonotonicTimestamp Timestamp => Acquisition.Timestamp;
    public long Sequence => Acquisition.Sequence;
    public TrackingConnectionState Connection { get; }
    public TrackingValidity Validity { get; }
    public RigidPose? Pose { get; }
    public bool HasValidPose => Acquisition.IsAssigned && Tracker.IsAssigned && Connection == TrackingConnectionState.Connected
        && Validity == TrackingValidity.Valid && Pose.HasValue && Pose.Value.Orientation.IsUnit;

    private TrackingSample(TrackerId tracker, TrackingAcquisitionStamp acquisition, TrackingConnectionState connection, TrackingValidity validity, RigidPose? pose)
    {
        Tracker = tracker;
        Acquisition = acquisition;
        Connection = connection;
        Validity = validity;
        Pose = pose;
    }

    public static TrackingSample Valid(TrackingAcquisitionStamp acquisition, TrackerId tracker, RigidPose roomPose)
    {
        if (!acquisition.IsAssigned) throw new ArgumentException("A valid tracking sample requires an acquisition stamp.", nameof(acquisition));
        if (!tracker.IsAssigned) throw new ArgumentException("A valid tracking sample requires a tracker identifier.", nameof(tracker));
        if (!roomPose.Orientation.IsUnit) throw new ArgumentException("A valid tracking sample requires a valid room-frame pose.", nameof(roomPose));
        return new TrackingSample(tracker, acquisition, TrackingConnectionState.Connected, TrackingValidity.Valid, roomPose);
    }

    public static TrackingSample Invalid(TrackingAcquisitionStamp acquisition, TrackerId tracker, TrackingConnectionState connection, TrackingValidity validity)
    {
        if (!acquisition.IsAssigned) throw new ArgumentException("An invalid tracking sample requires an acquisition stamp.", nameof(acquisition));
        if (!tracker.IsAssigned) throw new ArgumentException("An invalid tracking sample requires a tracker identifier.", nameof(tracker));
        if (!Enum.IsDefined(typeof(TrackingConnectionState), connection)) throw new ArgumentException("An invalid tracking sample requires a known connection state.", nameof(connection));
        if (!Enum.IsDefined(typeof(TrackingValidity), validity) || validity == TrackingValidity.Valid)
            throw new ArgumentException("Invalid samples require an explicit invalid validity value.", nameof(validity));
        return new TrackingSample(tracker, acquisition, connection, validity, null);
    }
}

/// <summary>Identifies the configured source mode in logs and diagnostics without selecting a transport implementation.</summary>
public readonly struct TrackingSourceIdentity : IEquatable<TrackingSourceIdentity>
{
    public string Value { get; }
    public TrackingSourceIdentity(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A tracking source identity is required.", nameof(value));
        Value = value;
    }
    public bool Equals(TrackingSourceIdentity other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is TrackingSourceIdentity other && Equals(other);
    public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>
/// A source has one injected shared clock and emits samples through this
/// interface. Implementations must call the TrackingSample factories so every
/// valid and invalid observation uses that source's clock authority.
/// </summary>
public interface ITrackingSource : IDisposable
{
    ISharedClock Clock { get; }
    TrackingSourceIdentity Source { get; }
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
