#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;
using Elts.Tracking;

namespace Elts.Logging
{
/// <summary>Schema identities are part of the recorded data contract. Bump a product version before changing its shape.</summary>
public static class LogSchemaVersions
{
    public const string Samples = "elts.samples.v1";
    public const string Events = "elts.events.v1";
    public const string Targets = "elts.targets.v1";
    public const string SessionSummary = "elts.session-summary.v1";
}

/// <summary>Adjustable logging limits. Queue operations never wait on disk I/O.</summary>
public sealed class LoggingConfiguration
{
    public const int DefaultSampleQueueCapacity = 4096;
    public const int DefaultCriticalQueueCapacity = 512;
    public const int DefaultFlushIntervalMilliseconds = 250;
    public const int DefaultCloseTimeoutMilliseconds = 10000;

    public LoggingConfiguration(int sampleQueueCapacity = DefaultSampleQueueCapacity, int criticalQueueCapacity = DefaultCriticalQueueCapacity,
        TimeSpan? flushInterval = null, TimeSpan? closeTimeout = null)
    {
        if (sampleQueueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(sampleQueueCapacity));
        if (criticalQueueCapacity < 1) throw new ArgumentOutOfRangeException(nameof(criticalQueueCapacity));
        SampleQueueCapacity = sampleQueueCapacity;
        CriticalQueueCapacity = criticalQueueCapacity;
        FlushInterval = flushInterval ?? TimeSpan.FromMilliseconds(DefaultFlushIntervalMilliseconds);
        CloseTimeout = closeTimeout ?? TimeSpan.FromMilliseconds(DefaultCloseTimeoutMilliseconds);
        if (FlushInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(flushInterval));
        if (CloseTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(closeTimeout));
    }

    public int SampleQueueCapacity { get; }
    public int CriticalQueueCapacity { get; }
    public TimeSpan FlushInterval { get; }
    public TimeSpan CloseTimeout { get; }
}

/// <summary>Immutable, non-sensitive provenance recorded in every session closure.</summary>
public sealed class SessionProvenance
{
    public SessionProvenance(string applicationVersion, string sourceRevision, string configurationHash, string scenarioHash,
        string fixtureHash, bool isSynthetic, DateTimeOffset utcStartupAnchor)
    {
        ApplicationVersion = LoggingValidation.RequiredToken(applicationVersion, nameof(applicationVersion));
        SourceRevision = LoggingValidation.RequiredToken(sourceRevision, nameof(sourceRevision));
        ConfigurationHash = LoggingValidation.Sha256(configurationHash, nameof(configurationHash));
        ScenarioHash = LoggingValidation.Sha256(scenarioHash, nameof(scenarioHash));
        FixtureHash = LoggingValidation.Sha256(fixtureHash, nameof(fixtureHash));
        IsSynthetic = isSynthetic;
        UtcStartupAnchor = utcStartupAnchor.ToUniversalTime();
    }
    public string ApplicationVersion { get; }
    public string SourceRevision { get; }
    public string ConfigurationHash { get; }
    public string ScenarioHash { get; }
    public string FixtureHash { get; }
    public bool IsSynthetic { get; }
    public DateTimeOffset UtcStartupAnchor { get; }
}

/// <summary>One paired capture. Poses are converted room-frame samples with no calibration or rendering prediction.</summary>
public readonly struct TrackingSamplePair
{
    public TrackingSamplePair(TrackingSample head, TrackingSample weapon)
    {
        if (head.Timestamp != weapon.Timestamp || head.Sequence != weapon.Sequence)
            throw new ArgumentException("Head and weapon samples must share one acquisition stamp.");
        if (head.Timestamp.Ticks < 0 || head.Sequence < 1) throw new ArgumentException("Capture stamp is invalid.");
        Head = head;
        Weapon = weapon;
    }
    public TrackingSample Head { get; }
    public TrackingSample Weapon { get; }
    public MonotonicTimestamp Timestamp => Head.Timestamp;
    public long Sequence => Head.Sequence;
}

public enum TargetLifecycle
{
    Spawned = 1,
    Updated = 2,
    Destroyed = 3
}

/// <summary>Canonical world-frame target state. Screen coordinates are deliberately absent.</summary>
public sealed class TargetSnapshot
{
    public TargetSnapshot(long sequence, MonotonicTimestamp timestamp, string targetId, string blockId, TargetLifecycle lifecycle,
        Vector3d worldPositionMeters, Vector3d worldVelocityMetersPerSecond, long scenarioSeed, string scenarioVersion)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (!Enum.IsDefined(typeof(TargetLifecycle), lifecycle)) throw new ArgumentOutOfRangeException(nameof(lifecycle));
        if (scenarioSeed < 0) throw new ArgumentOutOfRangeException(nameof(scenarioSeed));
        Sequence = sequence;
        Timestamp = timestamp;
        TargetId = LoggingValidation.Identifier(targetId, nameof(targetId));
        BlockId = LoggingValidation.Identifier(blockId, nameof(blockId));
        Lifecycle = lifecycle;
        WorldPositionMeters = LoggingValidation.Finite(worldPositionMeters, nameof(worldPositionMeters));
        WorldVelocityMetersPerSecond = LoggingValidation.Finite(worldVelocityMetersPerSecond, nameof(worldVelocityMetersPerSecond));
        ScenarioSeed = scenarioSeed;
        ScenarioVersion = LoggingValidation.RequiredToken(scenarioVersion, nameof(scenarioVersion));
    }
    public long Sequence { get; }
    public MonotonicTimestamp Timestamp { get; }
    public string TargetId { get; }
    public string BlockId { get; }
    public TargetLifecycle Lifecycle { get; }
    public Vector3d WorldPositionMeters { get; }
    public Vector3d WorldVelocityMetersPerSecond { get; }
    public long ScenarioSeed { get; }
    public string ScenarioVersion { get; }
}

/// <summary>A typed scalar permits structured event payloads without accepting arbitrary JSON fragments.</summary>
public readonly struct LogField
{
    private LogField(string name, string? text, double? number, bool? flag)
    {
        Name = LoggingValidation.Identifier(name, nameof(name));
        Text = text;
        Number = number;
        Flag = flag;
    }
    public string Name { get; }
    public string? Text { get; }
    public double? Number { get; }
    public bool? Flag { get; }
    public static LogField String(string name, string value) => new LogField(name, LoggingValidation.RequiredToken(value, nameof(value)), null, null);
    public static LogField NumberValue(string name, double value) => new LogField(name, null, LoggingValidation.Finite(value, nameof(value)), null);
    public static LogField Boolean(string name, bool value) => new LogField(name, null, null, value);
    internal void Validate()
    {
        LoggingValidation.Identifier(Name ?? string.Empty, nameof(Name));
        int values = (Text == null ? 0 : 1) + (Number.HasValue ? 1 : 0) + (Flag.HasValue ? 1 : 0);
        if (values != 1) throw new ArgumentException("Each event field must contain exactly one scalar value.");
    }
}

public sealed class SessionEvent
{
    public SessionEvent(long sequence, MonotonicTimestamp timestamp, string eventType, IReadOnlyList<LogField>? fields = null)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
        Timestamp = timestamp;
        EventType = LoggingValidation.Identifier(eventType, nameof(eventType));
        Fields = fields ?? Array.Empty<LogField>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in Fields) { field.Validate(); if (!names.Add(field.Name)) throw new ArgumentException("Event fields must have unique names.", nameof(fields)); }
    }
    public long Sequence { get; }
    public MonotonicTimestamp Timestamp { get; }
    public string EventType { get; }
    public IReadOnlyList<LogField> Fields { get; }
}

public sealed class LoggingHealth
{
    internal LoggingHealth(bool healthy, string? failure) { IsHealthy = healthy; Failure = failure; }
    public bool IsHealthy { get; }
    public string? Failure { get; }
}

public sealed class LogCloseResult
{
    internal LogCloseResult(bool completed, bool completeOutput, string? error) { Completed = completed; CompleteOutput = completeOutput; Error = error; }
    public bool Completed { get; }
    public bool CompleteOutput { get; }
    public string? Error { get; }
}

internal static class LoggingValidation
{
    public static string RequiredToken(string value, string name)
    {
        if (String.IsNullOrWhiteSpace(value) || value.Length > 256) throw new ArgumentException("A nonempty value no longer than 256 characters is required.", name);
        return value;
    }
    public static string Identifier(string value, string name)
    {
        RequiredToken(value, name);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')) throw new ArgumentException("Identifiers use letters, digits, hyphen, underscore, and period only.", name);
        }
        return value;
    }
    public static string Sha256(string value, string name)
    {
        if (value == null || value.Length != 64) throw new ArgumentException("A lowercase SHA-256 hex digest is required.", name);
        for (int i = 0; i < value.Length; i++) if (!((value[i] >= '0' && value[i] <= '9') || (value[i] >= 'a' && value[i] <= 'f'))) throw new ArgumentException("A lowercase SHA-256 hex digest is required.", name);
        return value;
    }
    public static double Finite(double value, string name)
    {
        if (Double.IsNaN(value) || Double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "A finite number is required.");
        return value;
    }
    public static Vector3d Finite(Vector3d value, string name)
    {
        Finite(value.X, name); Finite(value.Y, name); Finite(value.Z, name); return value;
    }
}
}