#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Elts.Geometry;
using Elts.Tracking;

namespace Elts.Logging
{
/// <summary>Small deterministic serializer for the fixed v1 contracts. It never accepts raw JSON from callers.</summary>
internal static class LogJson
{
    /// <summary>
    /// One v2 sample line. Each tracker is either {"pose":[x,y,z,qx,qy,qz,qw]} (room metres and a unit
    /// quaternion, meaning Connected and Valid) or an explicit connection and validity without a pose.
    /// The caller includes a tracker ID on a stream's first line and whenever that role's ID changes.
    /// </summary>
    public static string Sample(TrackingSamplePair pair, bool includeHeadId, bool includeWeaponId)
    {
        return "{\"schemaVersion\":" + String(LogSchemaVersions.Samples) + ",\"sequence\":" + Integer(pair.Sequence) +
            ",\"monotonicTicks\":" + Integer(pair.Timestamp.Ticks) + ",\"head\":" + Tracking(pair.Head, includeHeadId) +
            ",\"weapon\":" + Tracking(pair.Weapon, includeWeaponId) + "}";
    }
    public static string Target(TargetSnapshot target)
    {
        return "{\"schemaVersion\":" + String(LogSchemaVersions.Targets) + ",\"sequence\":" + Integer(target.Sequence) +
            ",\"monotonicTicks\":" + Integer(target.Timestamp.Ticks) + ",\"targetId\":" + String(target.TargetId) +
            ",\"blockId\":" + String(target.BlockId) + ",\"lifecycle\":" + String(target.Lifecycle.ToString()) +
            ",\"worldPositionMeters\":" + Vector(target.WorldPositionMeters) + ",\"worldVelocityMetersPerSecond\":" + Vector(target.WorldVelocityMetersPerSecond) +
            ",\"scenarioSeed\":" + Integer(target.ScenarioSeed) + ",\"scenarioVersion\":" + String(target.ScenarioVersion) + "}";
    }
    public static string Event(SessionEvent sessionEvent)
    {
        var fields = new StringBuilder();
        for (int i = 0; i < sessionEvent.Fields.Count; i++)
        {
            if (i != 0) fields.Append(',');
            LogField field = sessionEvent.Fields[i];
            fields.Append(String(field.Name)).Append(':');
            if (field.Text != null) fields.Append(String(field.Text));
            else if (field.Number.HasValue) fields.Append(Number(field.Number.Value));
            else if (field.Flag.HasValue) fields.Append(field.Flag.Value ? "true" : "false");
            else throw new InvalidOperationException("A log field has no value.");
        }
        return "{\"schemaVersion\":" + String(LogSchemaVersions.Events) + ",\"sequence\":" + Integer(sessionEvent.Sequence) +
            ",\"monotonicTicks\":" + Integer(sessionEvent.Timestamp.Ticks) + ",\"eventType\":" + String(sessionEvent.EventType) + ",\"payload\":{" + fields + "}}";
    }
    public static string Summary(LoggingRunLease lease, SessionProvenance provenance, bool complete, string? error, long droppedSamples,
        long writtenSamples, long writtenEvents, long writtenTargets, IReadOnlyDictionary<string, string> checksums,
        IReadOnlyDictionary<string, string> supplementaryChecksums)
    {
        return "{\"schemaVersion\":" + String(LogSchemaVersions.SessionSummary) + ",\"runId\":" + String(lease.RunId) +
            ",\"complete\":" + (complete ? "true" : "false") + ",\"error\":" + (error == null ? "null" : String(error)) +
            ",\"provenance\":{\"applicationVersion\":" + String(provenance.ApplicationVersion) + ",\"sourceRevision\":" + String(provenance.SourceRevision) +
            ",\"configurationHash\":" + String(provenance.ConfigurationHash) + ",\"scenarioHash\":" + String(provenance.ScenarioHash) +
            ",\"fixtureHash\":" + String(provenance.FixtureHash) + ",\"synthetic\":" + (provenance.IsSynthetic ? "true" : "false") +
            ",\"utcStartupAnchor\":" + String(provenance.UtcStartupAnchor.ToString("o", CultureInfo.InvariantCulture)) + "},\"counts\":{\"droppedSamples\":" + Integer(droppedSamples) +
            ",\"writtenSamples\":" + Integer(writtenSamples) + ",\"writtenEvents\":" + Integer(writtenEvents) + ",\"writtenTargets\":" + Integer(writtenTargets) +
            "},\"checksumsSha256\":" + Hashes(checksums) + ",\"supplementaryChecksumsSha256\":" + Hashes(supplementaryChecksums) + "}";
    }
    private static string Hashes(IReadOnlyDictionary<string, string> hashes)
    {
        var builder = new StringBuilder("{");
        foreach (var pair in hashes)
        {
            if (builder.Length > 1) builder.Append(',');
            builder.Append(String(pair.Key)).Append(':').Append(String(pair.Value));
        }
        return builder.Append('}').ToString();
    }
    private static string Tracking(TrackingSample sample, bool includeId)
    {
        // TrackingSamplePair guarantees a pose exactly when the tracker is Connected and Valid.
        string state = sample.Pose.HasValue ? "\"pose\":" + Pose(sample.Pose.Value)
            : "\"connection\":" + String(sample.Connection.ToString()) + ",\"validity\":" + String(sample.Validity.ToString());
        return "{" + (includeId ? "\"trackerId\":" + String(sample.Tracker.Value) + "," : "") + state + "}";
    }
    private static string Pose(RigidPose pose) => "[" + Number(pose.Position.X) + "," + Number(pose.Position.Y) + "," + Number(pose.Position.Z) + "," +
        Number(pose.Orientation.X) + "," + Number(pose.Orientation.Y) + "," + Number(pose.Orientation.Z) + "," + Number(pose.Orientation.W) + "]";
    private static string Vector(Vector3d vector) => "{\"x\":" + Number(vector.X) + ",\"y\":" + Number(vector.Y) + ",\"z\":" + Number(vector.Z) + "}";
    private static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Number(double value) { LoggingValidation.Finite(value, nameof(value)); return value.ToString("R", CultureInfo.InvariantCulture); }
    private static string String(string value)
    {
        var output = new StringBuilder(value.Length + 2); output.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '"': output.Append("\\\""); break; case '\\': output.Append("\\\\"); break; case '\b': output.Append("\\b"); break;
                case '\f': output.Append("\\f"); break; case '\n': output.Append("\\n"); break; case '\r': output.Append("\\r"); break; case '\t': output.Append("\\t"); break;
                default: if (c < 32) output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else output.Append(c); break;
            }
        }
        return output.Append('"').ToString();
    }
}
}