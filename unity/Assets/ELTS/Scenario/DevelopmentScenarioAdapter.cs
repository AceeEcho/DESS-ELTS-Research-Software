#nullable enable
using System;
using Elts.Config;

namespace Elts.Scenario
{
/// <summary>
/// Bridges the already-validated typed development configuration into the
/// scenario runtime without granting any study decision. The adapter preserves
/// the configured sine request as provenance; it never silently substitutes
/// that unresolved D-04 request with the development RandomWalk placeholder.
/// </summary>
public sealed class DevelopmentScenarioAdapter
{
    private DevelopmentScenarioAdapter(ScenarioDefinition definition, double targetDistanceM, double targetSpeedMps, double fixedTargetHoldSeconds, string configuredMovingTargetMotion)
    { Definition=definition; TargetDistanceM=targetDistanceM; TargetSpeedMps=targetSpeedMps; FixedTargetHoldSeconds=fixedTargetHoldSeconds; ConfiguredMovingTargetMotion=configuredMovingTargetMotion; }
    public ScenarioDefinition Definition { get; }
    public double TargetDistanceM { get; }
    public double TargetSpeedMps { get; }
    public double FixedTargetHoldSeconds { get; }
    public string ConfiguredMovingTargetMotion { get; }
    public bool RequiresD04MotionResolution => !String.Equals(ConfiguredMovingTargetMotion, "RandomWalk", StringComparison.Ordinal);

    public static DevelopmentScenarioAdapter Create(ScenarioConfiguration source, SpawnVolume spawnVolume, int maintainCount, string runtimeMovementName, string shotModelName, string scoringRuleName)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var definition=new ScenarioDefinition(source.ScenarioId, source.TargetRadiusM, spawnVolume, maintainCount, source.TargetLifetimeSeconds, source.SpawnIntervalSeconds, runtimeMovementName, shotModelName, scoringRuleName, source.Seed);
        return new DevelopmentScenarioAdapter(definition, source.TargetDistanceM, source.TargetSpeedMps, source.FtHoldSeconds, source.MtMotion);
    }
}
}
