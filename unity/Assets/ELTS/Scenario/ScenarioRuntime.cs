#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;

namespace Elts.Scenario
{
/// <summary>All coordinates are synthetic Unity-room meters. They are not measured calibration values.</summary>
public readonly struct SpawnVolume
{
    public readonly Vector3d Minimum, Maximum;
    public SpawnVolume(Vector3d minimum, Vector3d maximum)
    {
        if (minimum.X >= maximum.X || minimum.Y >= maximum.Y || minimum.Z >= maximum.Z) throw new ArgumentException("Spawn volume bounds must increase on every axis.");
        Minimum = minimum; Maximum = maximum;
    }
    public bool Contains(Vector3d point) => point.X >= Minimum.X && point.X <= Maximum.X && point.Y >= Minimum.Y && point.Y <= Maximum.Y && point.Z >= Minimum.Z && point.Z <= Maximum.Z;
}

public sealed class ScenarioDefinition
{
    public ScenarioDefinition(string id, double targetRadiusM, SpawnVolume spawnVolume, int maintainCount, double targetLifetimeSeconds, double respawnDelaySeconds, string movementName, string shotModelName, string scoringRuleName, int? seedOverride = null, bool showHitVisual = false, bool playShotSound = false, bool showScore = false)
    {
        if (String.IsNullOrWhiteSpace(id) || targetRadiusM <= 0 || maintainCount < 1 || targetLifetimeSeconds <= 0 || respawnDelaySeconds < 0) throw new ArgumentException("Scenario values must be positive and complete.");
        Id=id; TargetRadiusM=targetRadiusM; SpawnVolume=spawnVolume; MaintainCount=maintainCount; TargetLifetimeSeconds=targetLifetimeSeconds; RespawnDelaySeconds=respawnDelaySeconds; MovementName=movementName ?? throw new ArgumentNullException(nameof(movementName)); ShotModelName=shotModelName ?? throw new ArgumentNullException(nameof(shotModelName)); ScoringRuleName=scoringRuleName ?? throw new ArgumentNullException(nameof(scoringRuleName)); SeedOverride=seedOverride; ShowHitVisual=showHitVisual; PlayShotSound=playShotSound; ShowScore=showScore;
    }
    public string Id { get; } public double TargetRadiusM { get; } public SpawnVolume SpawnVolume { get; } public int MaintainCount { get; } public double TargetLifetimeSeconds { get; } public double RespawnDelaySeconds { get; } public string MovementName { get; } public string ShotModelName { get; } public string ScoringRuleName { get; } public int? SeedOverride { get; }
    // D-06 remains undecided. These development defaults deliberately give no participant feedback.
    public bool ShowHitVisual { get; } public bool PlayShotSound { get; } public bool ShowScore { get; }
    public int BlockSeed(string participantId, string blockId) => SeedOverride ?? DeterministicSeed.Hash(participantId, blockId);
}

public enum TargetState { Spawned, Active, Destroyed, Despawned }
public sealed class TargetEntity
{
    public TargetEntity(string id, Vector3d position, Vector3d velocity, double radiusM, MonotonicTimestamp spawnedAt)
    { if(String.IsNullOrWhiteSpace(id) || radiusM <= 0) throw new ArgumentException("Target requires an id and positive radius."); Id=id; Position=position; Velocity=velocity; RadiusM=radiusM; SpawnedAt=spawnedAt; State=TargetState.Spawned; }
    public string Id { get; } public Vector3d Position { get; private set; } public Vector3d Velocity { get; private set; } public double RadiusM { get; } public MonotonicTimestamp SpawnedAt { get; } public TargetState State { get; private set; }
    public void Activate() { Require(TargetState.Spawned); State=TargetState.Active; }
    public void Move(Vector3d position, Vector3d velocity) { Require(TargetState.Active); Position=position; Velocity=velocity; }
    public void Destroy() { Require(TargetState.Active); State=TargetState.Destroyed; }
    public void Despawn() { if (State != TargetState.Active && State != TargetState.Destroyed) throw new InvalidOperationException("Only active or destroyed targets may despawn."); State=TargetState.Despawned; }
    private void Require(TargetState expected) { if(State != expected) throw new InvalidOperationException("Invalid target state transition."); }
}

public interface IMovementBehaviour { string Name { get; } void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds); }
public sealed class FixedMovement : IMovementBehaviour { public string Name => "Fixed"; public void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds) { if(deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds)); } }
public sealed class RandomWalkMovement : IMovementBehaviour
{
    private readonly DeterministicRandom random; private readonly double minimumSpeed, maximumSpeed, directionChangeSeconds; private double untilChange; private Vector3d direction;
    public RandomWalkMovement(int seed, double minimumSpeed, double maximumSpeed, double directionChangeSeconds) { if(minimumSpeed < 0 || maximumSpeed < minimumSpeed || directionChangeSeconds <= 0) throw new ArgumentException("Invalid RandomWalk parameters."); random=new DeterministicRandom(seed); this.minimumSpeed=minimumSpeed; this.maximumSpeed=maximumSpeed; this.directionChangeSeconds=directionChangeSeconds; direction=new Vector3d(1,0,0); }
    public string Name => "RandomWalk";
    public void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds) { if(deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds)); untilChange-=deltaSeconds; if(untilChange<=0) { direction=RandomDirection(); untilChange=directionChangeSeconds; } double speed=minimumSpeed+(maximumSpeed-minimumSpeed)*random.Next(); var next=target.Position+direction*speed*deltaSeconds; next=new Vector3d(Clamp(next.X,bounds.Minimum.X,bounds.Maximum.X),Clamp(next.Y,bounds.Minimum.Y,bounds.Maximum.Y),Clamp(next.Z,bounds.Minimum.Z,bounds.Maximum.Z)); target.Move(next,direction*speed); }
    private Vector3d RandomDirection() { var v=new Vector3d(random.Next()*2-1,random.Next()*2-1,random.Next()*2-1); return v.Length > GeometryTolerance.Length ? v.Normalized() : new Vector3d(1,0,0); }
    private static double Clamp(double x,double lo,double hi) => Math.Max(lo,Math.Min(hi,x));
}

/// <summary>
/// Resolves the development movement names carried by a scenario definition.
/// FT deliberately resolves to Fixed; MT resolves to the seeded RandomWalk
/// placeholder.  D-04 must set any study moving-target behavior before release.
/// </summary>
public static class ScenarioMovementFactory
{
    public static IMovementBehaviour Create(string movementName, int blockSeed, double minimumSpeedMps, double maximumSpeedMps, double directionChangeSeconds)
    {
        if (String.Equals(movementName, "Fixed", StringComparison.Ordinal)) return new FixedMovement();
        if (String.Equals(movementName, "RandomWalk", StringComparison.Ordinal))
            return new RandomWalkMovement(blockSeed, minimumSpeedMps, maximumSpeedMps, directionChangeSeconds);
        throw new ArgumentException("The scenario movement name is not supported by the synthetic runtime.", nameof(movementName));
    }
}

public interface ISpawnRule { IEnumerable<TargetEntity> Spawn(ScenarioDefinition definition, MonotonicTimestamp now, Vector3d eyePosition, Func<Vector3d,bool> isVisible); }
public sealed class MaintainCountSpawnRule : ISpawnRule
{
    private readonly DeterministicRandom random; private long sequence;
    public MaintainCountSpawnRule(int seed) { random=new DeterministicRandom(seed); }
    public IEnumerable<TargetEntity> Spawn(ScenarioDefinition definition, MonotonicTimestamp now, Vector3d eyePosition, Func<Vector3d,bool> isVisible)
    { if(isVisible == null) throw new ArgumentNullException(nameof(isVisible)); var targets=new List<TargetEntity>(); int attempts=0; while(targets.Count<definition.MaintainCount && attempts++<10000) { var p=Point(definition.SpawnVolume); if(isVisible(p)) { var t=new TargetEntity("target-" + (++sequence),p,Vector3d.Zero,definition.TargetRadiusM,now); t.Activate(); targets.Add(t); } } if(targets.Count != definition.MaintainCount) throw new InvalidOperationException("No visible spawn position found."); return targets; }
    private Vector3d Point(SpawnVolume v) => new Vector3d(v.Minimum.X+(v.Maximum.X-v.Minimum.X)*random.Next(),v.Minimum.Y+(v.Maximum.Y-v.Minimum.Y)*random.Next(),v.Minimum.Z+(v.Maximum.Z-v.Minimum.Z)*random.Next());
}

/// <summary>One trigger observation paired with the bore ray at the trigger edge.</summary>
public sealed class ShotContext
{
    public ShotContext(MonotonicTimestamp timestamp, Ray3d boreRay, bool trackingValid, bool isTriggerFallingEdge = true)
    { Timestamp=timestamp; BoreRay=boreRay; TrackingValid=trackingValid; IsTriggerFallingEdge=isTriggerFallingEdge; }
    public MonotonicTimestamp Timestamp { get; }
    public Ray3d BoreRay { get; }
    public bool TrackingValid { get; }
    public bool IsTriggerFallingEdge { get; }
}
public interface IShotModel { IReadOnlyList<SessionEvent> Fire(ShotContext shot, string blockId, IEnumerable<TargetEntity> targets, Func<TargetEntity,bool> isVisible); }
public sealed class HitscanShotModel : IShotModel
{
    private long eventSequence;
    private readonly TimeSpan? minimumShotInterval;
    private MonotonicTimestamp? lastAcceptedShot;

    /// <summary>
    /// The source defaults the unresolved D-05 limits to no limit.  A caller may
    /// inject the later investigator-approved lockout without changing this model.
    /// </summary>
    public HitscanShotModel(TimeSpan? minimumShotInterval = null)
    {
        if (minimumShotInterval.HasValue && minimumShotInterval.Value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(minimumShotInterval));
        this.minimumShotInterval = minimumShotInterval;
    }
    public IReadOnlyList<SessionEvent> Fire(ShotContext shot, string blockId, IEnumerable<TargetEntity> targets, Func<TargetEntity,bool> isVisible)
    {
        if (shot == null) throw new ArgumentNullException(nameof(shot));
        if (targets == null) throw new ArgumentNullException(nameof(targets));
        if (isVisible == null) throw new ArgumentNullException(nameof(isVisible));
        if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identifier is required.", nameof(blockId));
        if (!shot.TrackingValid || !shot.IsTriggerFallingEdge || IsLockedOut(shot.Timestamp)) return Array.Empty<SessionEvent>();

        lastAcceptedShot = shot.Timestamp;
        var all=new List<SessionEvent>{Event(shot.Timestamp,"ShotFired",blockId,null)};
        TargetEntity? hit=null; double nearest=Double.PositiveInfinity;
        foreach(var target in targets) if(target.State==TargetState.Active && isVisible(target) && Sphere(shot.BoreRay,target,out double d) && d<nearest) { nearest=d; hit=target; }
        if(hit != null) { hit.Destroy(); all.Add(Event(shot.Timestamp,"TargetHit",blockId,hit.Id)); all.Add(Event(shot.Timestamp,"TargetDestroyed",blockId,hit.Id)); }
        return all;
    }
    private bool IsLockedOut(MonotonicTimestamp timestamp) => minimumShotInterval.HasValue && lastAcceptedShot.HasValue
        && timestamp.Ticks - lastAcceptedShot.Value.Ticks < minimumShotInterval.Value.Ticks;
    private SessionEvent Event(MonotonicTimestamp time,string type,string blockId,string? targetId) { var f=new List<LogField>{LogField.String("blockId",blockId)}; if(targetId != null) f.Add(LogField.String("targetId",targetId)); return new SessionEvent(++eventSequence,time,type,f); }
    private static bool Sphere(Ray3d ray, TargetEntity target,out double distance) { var oc=ray.Origin-target.Position; double b=Vector3d.Dot(oc,ray.Direction); double c=Vector3d.Dot(oc,oc)-target.RadiusM*target.RadiusM; double disc=b*b-c; if(disc<0) {distance=0;return false;} distance=-b-Math.Sqrt(disc); if(distance<=0) distance=-b+Math.Sqrt(disc); return distance>0; }
}

public interface IScoringRule { int Score(string blockId, MonotonicTimestamp start, MonotonicTimestamp end, IEnumerable<SessionEvent> events); }
public sealed class TargetsDestroyedCount : IScoringRule
{
    public int Score(string blockId, MonotonicTimestamp start, MonotonicTimestamp end, IEnumerable<SessionEvent> events)
    { if(end < start) throw new ArgumentException("Block end precedes start."); var ids=new HashSet<string>(StringComparer.Ordinal); foreach(var e in events) if(e.EventType=="TargetDestroyed" && e.Timestamp >= start && e.Timestamp < end && Field(e,"blockId")==blockId) { var id=Field(e,"targetId"); if(id == null) throw new ArgumentException("TargetDestroyed requires scalar targetId."); if(!ids.Add(id)) throw new ArgumentException("Duplicate target destruction in a block."); } return ids.Count; }
    private static string? Field(SessionEvent e,string name) { foreach(var f in e.Fields) if(f.Name == name) return f.Text; return null; }

    /// <summary>
    /// Primary-DV calculation accepts only a normally completed, exact-design
    /// block.  A target lifecycle record alone is deliberately never a score.
    /// </summary>
    public int ScoreCompletedPrimaryBlock(string blockId, IEnumerable<SessionEvent> events)
    {
        if (events == null) throw new ArgumentNullException(nameof(events));
        SessionEvent? started = null;
        SessionEvent? ended = null;
        foreach (var item in events)
        {
            if (Field(item, "blockId") != blockId) continue;
            if (item.EventType == "BlockStarted") { if (started != null) throw new ArgumentException("A completed block has exactly one BlockStarted event."); started = item; }
            if (item.EventType == "BlockEnded") { if (ended != null) throw new ArgumentException("A completed block has exactly one BlockEnded event."); ended = item; }
            if (item.EventType == "BlockAborted") throw new ArgumentException("An aborted block cannot produce a completed primary DV.");
        }
        if (started == null || ended == null) throw new ArgumentException("Primary scoring requires BlockStarted and BlockEnded events.");
        if (ended.Timestamp.Ticks - started.Timestamp.Ticks != TimeSpan.FromSeconds(300).Ticks)
            throw new ArgumentException("A completed primary block must span exactly 300 seconds.");
        return Score(blockId, started.Timestamp, ended.Timestamp, events);
    }
}

public sealed class SessionPlan
{
    private static readonly HashSet<string> Required = new HashSet<string>(new[]{"WE_MT","NE_MT","WE_FT","NE_FT"},StringComparer.Ordinal);
    public SessionPlan(string participantId, IEnumerable<string> order, double conditionDurationSeconds=300, bool overrideDesignDuration=false) { if(String.IsNullOrWhiteSpace(participantId)) throw new ArgumentException("Participant id is required."); var list=(order??throw new ArgumentNullException(nameof(order))).ToArray(); if(!new HashSet<string>(list,StringComparer.Ordinal).SetEquals(Required) || list.Length!=4) throw new ArgumentException("Order must be a permutation of the four conditions."); if(conditionDurationSeconds <= 0 || (conditionDurationSeconds != 300 && !overrideDesignDuration)) throw new ArgumentException("Condition duration is fixed at 300 seconds unless explicitly overridden."); ParticipantId=participantId; ConditionOrder=Array.AsReadOnly(list); ConditionDurationSeconds=conditionDurationSeconds; OverrideDesignDuration=overrideDesignDuration; }
    public string ParticipantId { get; } public IReadOnlyList<string> ConditionOrder { get; } public double ConditionDurationSeconds { get; } public bool OverrideDesignDuration { get; }
}

internal static class DeterministicSeed { public static int Hash(string a,string b) { unchecked { int h=17; foreach(char c in (a??String.Empty)+"|"+(b??String.Empty)) h=h*31+c; return h & Int32.MaxValue; } } }
internal sealed class DeterministicRandom { private uint state; public DeterministicRandom(int seed) { state=(uint)seed; if(state==0) state=1; } public double Next() { state=1664525u*state+1013904223u; return state/(double)UInt32.MaxValue; } }
}

