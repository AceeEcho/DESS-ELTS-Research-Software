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
    public static string Sample(TrackingSamplePair pair)
    {
        return "{\"schemaVersion\":" + String(LogSchemaVersions.Samples) + ",\"sequence\":" + Integer(pair.Sequence) +
            ",\"monotonicTicks\":" + Integer(pair.Timestamp.Ticks) + ",\"head\":" + Tracking(pair.Head) + ",\"weapon\":" + Tracking(pair.Weapon) + "}";
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
        long writtenSamples, long writtenEvents, long writtenTargets, IReadOnlyDictionary<string, string> checksums)
    {
        var products = new StringBuilder();
        bool first = true;
        foreach (var pair in checksums)
        {
            if (!first) products.Append(','); first = false;
            products.Append(String(pair.Key)).Append(':').Append(String(pair.Value));
        }
        return "{\"schemaVersion\":" + String(LogSchemaVersions.SessionSummary) + ",\"runId\":" + String(lease.RunId) +
            ",\"complete\":" + (complete ? "true" : "false") + ",\"error\":" + (error == null ? "null" : String(error)) +
            ",\"provenance\":{\"applicationVersion\":" + String(provenance.ApplicationVersion) + ",\"sourceRevision\":" + String(provenance.SourceRevision) +
            ",\"configurationHash\":" + String(provenance.ConfigurationHash) + ",\"scenarioHash\":" + String(provenance.ScenarioHash) +
            ",\"fixtureHash\":" + String(provenance.FixtureHash) + ",\"synthetic\":" + (provenance.IsSynthetic ? "true" : "false") +
            ",\"utcStartupAnchor\":" + String(provenance.UtcStartupAnchor.ToString("o", CultureInfo.InvariantCulture)) + "},\"counts\":{\"droppedSamples\":" + Integer(droppedSamples) +
            ",\"writtenSamples\":" + Integer(writtenSamples) + ",\"writtenEvents\":" + Integer(writtenEvents) + ",\"writtenTargets\":" + Integer(writtenTargets) +
            "},\"checksumsSha256\":{" + products + "}}";
    }
    private static string Tracking(TrackingSample sample)
    {
        string pose = sample.Pose.HasValue ? Pose(sample.Pose.Value) : "null";
        return "{\"trackerId\":" + String(sample.Tracker.Value) + ",\"connection\":" + String(sample.Connection.ToString()) +
            ",\"validity\":" + String(sample.Validity.ToString()) + ",\"pose\":" + pose + "}";
    }
    private static string Pose(RigidPose pose) => "{\"positionMeters\":" + Vector(pose.Position) + ",\"orientation\":{\"x\":" + Number(pose.Orientation.X) + ",\"y\":" + Number(pose.Orientation.Y) + ",\"z\":" + Number(pose.Orientation.Z) + ",\"w\":" + Number(pose.Orientation.W) + "}}";
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