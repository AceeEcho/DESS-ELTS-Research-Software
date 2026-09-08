#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;

namespace Elts.Scenario
{
/// <summary>All coordinates are synthetic Unity-room meters, never measured calibration values.</summary>
public readonly struct SpawnVolume
{
    public readonly Vector3d Minimum, Maximum;
    public SpawnVolume(Vector3d minimum, Vector3d maximum)
    {
        if (!IsOrdered(minimum, maximum)) throw new ArgumentException("Spawn volume bounds must be finite and increase on every axis.");
        Minimum = minimum; Maximum = maximum;
    }
    public bool IsValid => IsOrdered(Minimum, Maximum);
    public void RequireValid() { if (!IsValid) throw new ArgumentException("A constructed, nonempty spawn volume is required."); }
    public bool Contains(Vector3d point) { RequireValid(); return point.X >= Minimum.X && point.X <= Maximum.X && point.Y >= Minimum.Y && point.Y <= Maximum.Y && point.Z >= Minimum.Z && point.Z <= Maximum.Z; }
    private static bool IsOrdered(Vector3d minimum, Vector3d maximum) => minimum.IsFinite && maximum.IsFinite
        && minimum.X < maximum.X && minimum.Y < maximum.Y && minimum.Z < maximum.Z;
}

public sealed class ScenarioDefinition
{
    public ScenarioDefinition(string id, double targetRadiusM, SpawnVolume spawnVolume, int maintainCount, double targetLifetimeSeconds, double respawnDelaySeconds, string movementName, string shotModelName, string scoringRuleName, int? seedOverride = null, bool showHitVisual = false, bool playShotSound = false, bool showScore = false)
    {
        if (String.IsNullOrWhiteSpace(id) || !PositiveFinite(targetRadiusM) || maintainCount < 1 || !PositiveFinite(targetLifetimeSeconds) || !NonnegativeFinite(respawnDelaySeconds)) throw new ArgumentException("Scenario values must be finite, positive, and complete.");
        spawnVolume.RequireValid();
        Id=id; TargetRadiusM=targetRadiusM; SpawnVolume=spawnVolume; MaintainCount=maintainCount; TargetLifetimeSeconds=targetLifetimeSeconds; RespawnDelaySeconds=respawnDelaySeconds;
        MovementName=RequiredName(movementName, nameof(movementName)); ShotModelName=RequiredName(shotModelName, nameof(shotModelName)); ScoringRuleName=RequiredName(scoringRuleName, nameof(scoringRuleName));
        SeedOverride=seedOverride; ShowHitVisual=showHitVisual; PlayShotSound=playShotSound; ShowScore=showScore;
    }
    public string Id { get; } public double TargetRadiusM { get; } public SpawnVolume SpawnVolume { get; } public int MaintainCount { get; } public double TargetLifetimeSeconds { get; } public double RespawnDelaySeconds { get; } public string MovementName { get; } public string ShotModelName { get; } public string ScoringRuleName { get; } public int? SeedOverride { get; }
    // D-06 remains undecided. These development defaults deliberately give no participant feedback.
    public bool ShowHitVisual { get; } public bool PlayShotSound { get; } public bool ShowScore { get; }
    public int BlockSeed(string participantId, string blockId) => SeedOverride ?? DeterministicSeed.Hash(participantId, blockId);
    internal static bool PositiveFinite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value) && value > 0;
    internal static bool NonnegativeFinite(double value) => !Double.IsNaN(value) && !Double.IsInfinity(value) && value >= 0;
    private static string RequiredName(string value, string name) { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("A nonempty name is required.", name); return value; }
}

public enum TargetState { Spawned, Active, Destroyed, Despawned }
public sealed class TargetEntity
{
    public TargetEntity(string id, Vector3d position, Vector3d velocity, double radiusM, MonotonicTimestamp spawnedAt)
    { if(String.IsNullOrWhiteSpace(id) || !position.IsFinite || !velocity.IsFinite || !ScenarioDefinition.PositiveFinite(radiusM)) throw new ArgumentException("Target requires an id, finite world state, and positive finite radius."); Id=id; Position=position; Velocity=velocity; RadiusM=radiusM; SpawnedAt=spawnedAt; State=TargetState.Spawned; }
    public string Id { get; } public Vector3d Position { get; private set; } public Vector3d Velocity { get; private set; } public double RadiusM { get; } public MonotonicTimestamp SpawnedAt { get; } public TargetState State { get; private set; }
    public void Activate() { Require(TargetState.Spawned); State=TargetState.Active; }
    public void Move(Vector3d position, Vector3d velocity) { Require(TargetState.Active); if (!position.IsFinite || !velocity.IsFinite) throw new ArgumentException("Target world state must be finite."); Position=position; Velocity=velocity; }
    public void Destroy() { Require(TargetState.Active); State=TargetState.Destroyed; }
    public void Despawn() { if (State != TargetState.Active && State != TargetState.Destroyed) throw new InvalidOperationException("Only active or destroyed targets may despawn."); State=TargetState.Despawned; }
    private void Require(TargetState expected) { if(State != expected) throw new InvalidOperationException("Invalid target state transition."); }
}

public interface IMovementBehaviour { string Name { get; } void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds); }
public sealed class FixedMovement : IMovementBehaviour
{
    public string Name => "Fixed";
    public void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds)
    { if (target == null) throw new ArgumentNullException(nameof(target)); if (!ScenarioDefinition.NonnegativeFinite(deltaSeconds)) throw new ArgumentOutOfRangeException(nameof(deltaSeconds)); bounds.RequireValid(); }
}

public sealed class RandomWalkMovement : IMovementBehaviour
{
    private readonly int blockSeed; private readonly double minimumSpeed, maximumSpeed, directionChangeSeconds;
    private readonly Dictionary<string, TargetWalkState> states = new Dictionary<string, TargetWalkState>(StringComparer.Ordinal);
    private sealed class TargetWalkState { public TargetWalkState(int seed) { Random = new DeterministicRandom(seed); Direction = new Vector3d(1, 0, 0); } public DeterministicRandom Random { get; } public double UntilChange; public Vector3d Direction; }
    public RandomWalkMovement(int seed, double minimumSpeed, double maximumSpeed, double directionChangeSeconds)
    { if(!ScenarioDefinition.NonnegativeFinite(minimumSpeed) || !ScenarioDefinition.NonnegativeFinite(maximumSpeed) || maximumSpeed < minimumSpeed || !ScenarioDefinition.PositiveFinite(directionChangeSeconds)) throw new ArgumentException("RandomWalk parameters must be finite and valid."); blockSeed=seed; this.minimumSpeed=minimumSpeed; this.maximumSpeed=maximumSpeed; this.directionChangeSeconds=directionChangeSeconds; }
    public string Name => "RandomWalk";
    public void Step(TargetEntity target, double deltaSeconds, SpawnVolume bounds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target)); if (!ScenarioDefinition.NonnegativeFinite(deltaSeconds)) throw new ArgumentOutOfRangeException(nameof(deltaSeconds)); bounds.RequireValid();
        if (!states.TryGetValue(target.Id, out var state)) { state = new TargetWalkState(DeterministicSeed.Hash(blockSeed.ToString(CultureInfo.InvariantCulture), target.Id)); states.Add(target.Id, state); }
        state.UntilChange -= deltaSeconds;
        if(state.UntilChange <= 0) { state.Direction=RandomDirection(state.Random); state.UntilChange=directionChangeSeconds; }
        double speed=minimumSpeed+(maximumSpeed-minimumSpeed)*state.Random.Next();
        var next=target.Position+state.Direction*speed*deltaSeconds;
        next=new Vector3d(Clamp(next.X,bounds.Minimum.X,bounds.Maximum.X),Clamp(next.Y,bounds.Minimum.Y,bounds.Maximum.Y),Clamp(next.Z,bounds.Minimum.Z,bounds.Maximum.Z));
        target.Move(next,state.Direction*speed);
    }
    private static Vector3d RandomDirection(DeterministicRandom random) { var v=new Vector3d(random.Next()*2-1,random.Next()*2-1,random.Next()*2-1); return v.Length > GeometryTolerance.Length ? v.Normalized() : new Vector3d(1,0,0); }
    private static double Clamp(double x,double lo,double hi) => Math.Max(lo,Math.Min(hi,x));
}

/// <summary>FT is fixed. MT is a seeded provisional random walk until D-04 defines study motion.</summary>
public static class ScenarioMovementFactory
{
    public static IMovementBehaviour Create(string movementName, int blockSeed, double minimumSpeedMps, double maximumSpeedMps, double directionChangeSeconds)
    {
        if (String.Equals(movementName, "Fixed", StringComparison.Ordinal)) return new FixedMovement();
        if (String.Equals(movementName, "RandomWalk", StringComparison.Ordinal)) return new RandomWalkMovement(blockSeed, minimumSpeedMps, maximumSpeedMps, directionChangeSeconds);
        throw new ArgumentException("The scenario movement name is not supported by the synthetic runtime.", nameof(movementName));
    }
}

/// <summary>One deterministic visible-position sampler. Population ownership is held by ScenarioPopulation.</summary>
public sealed class MaintainCountSpawnRule
{
    private readonly DeterministicRandom random; private long sequence;
    public MaintainCountSpawnRule(int seed) { random=new DeterministicRandom(seed); }
    internal TargetEntity SpawnOne(ScenarioDefinition definition, MonotonicTimestamp now, Func<Vector3d,bool> isVisible)
    {
        if (definition == null) throw new ArgumentNullException(nameof(definition)); if (isVisible == null) throw new ArgumentNullException(nameof(isVisible));
        for (int attempts=0; attempts<10000; attempts++) { var p=Point(definition.SpawnVolume); if (isVisible(p)) { var target=new TargetEntity("target-"+(++sequence),p,Vector3d.Zero,definition.TargetRadiusM,now); target.Activate(); return target; } }
        throw new InvalidOperationException("No visible spawn position found.");
    }
    private Vector3d Point(SpawnVolume volume) => new Vector3d(volume.Minimum.X+(volume.Maximum.X-volume.Minimum.X)*random.Next(),volume.Minimum.Y+(volume.Maximum.Y-volume.Minimum.Y)*random.Next(),volume.Minimum.Z+(volume.Maximum.Z-volume.Minimum.Z)*random.Next());
}

/// <summary>Stateful target population: preserves N active targets, honors expiry and respawn delay, and retains lifecycle history.</summary>
public sealed class ScenarioPopulation
{
    private readonly ScenarioDefinition definition; private readonly MaintainCountSpawnRule spawnRule; private readonly List<TargetEntity> history = new List<TargetEntity>(); private readonly List<MonotonicTimestamp> vacancies = new List<MonotonicTimestamp>();
    private MonotonicTimestamp? lastUpdate;
    public ScenarioPopulation(ScenarioDefinition definition, MaintainCountSpawnRule spawnRule)
    { this.definition=definition ?? throw new ArgumentNullException(nameof(definition)); this.spawnRule=spawnRule ?? throw new ArgumentNullException(nameof(spawnRule)); for (int i=0;i<definition.MaintainCount;i++) vacancies.Add(MonotonicTimestamp.Zero); }
    public IReadOnlyList<TargetEntity> History => history;
    public IReadOnlyList<TargetEntity> ActiveTargets => history.Where(t => t.State == TargetState.Active).ToArray();
    public IReadOnlyList<TargetEntity> Reconcile(MonotonicTimestamp now, Func<Vector3d,bool> isVisible)
    {
        if (isVisible == null) throw new ArgumentNullException(nameof(isVisible)); if (lastUpdate.HasValue && now < lastUpdate.Value) throw new ArgumentException("Population time cannot move backward.", nameof(now));
        foreach (var target in history)
        {
            if (target.State == TargetState.Active && now.Ticks-target.SpawnedAt.Ticks >= SecondsToTicks(definition.TargetLifetimeSeconds)) { target.Despawn(); vacancies.Add(AvailableAfter(now)); }
            else if (target.State == TargetState.Destroyed) { target.Despawn(); vacancies.Add(AvailableAfter(now)); }
        }
        for (int index=vacancies.Count-1; index>=0; index--) if (vacancies[index] <= now) { history.Add(spawnRule.SpawnOne(definition, now, isVisible)); vacancies.RemoveAt(index); }
        lastUpdate=now; return ActiveTargets;
    }
    private MonotonicTimestamp AvailableAfter(MonotonicTimestamp now) => new MonotonicTimestamp(checked(now.Ticks+SecondsToTicks(definition.RespawnDelaySeconds)));
    private static long SecondsToTicks(double seconds) => checked((long)(seconds*TimeSpan.TicksPerSecond));
}

/// <summary>Allocates one strict, shared event sequence for a complete session log.</summary>
public interface ISessionEventSequence { long Next(); }
public sealed class SessionEventSequence : ISessionEventSequence
{
    private readonly object sync = new object(); private long next;
    public SessionEventSequence(long firstSequence = 1) { if (firstSequence < 1) throw new ArgumentOutOfRangeException(nameof(firstSequence)); next=firstSequence; }
    public long Next() { lock (sync) { if (next == long.MaxValue) throw new OverflowException("Session event sequence is exhausted."); return next++; } }
}

/// <summary>One trigger observation paired with the bore ray at the trigger edge.</summary>
public sealed class ShotContext
{
    public ShotContext(MonotonicTimestamp timestamp, Ray3d boreRay, bool trackingValid, bool isTriggerFallingEdge = true)
    { if (!boreRay.Origin.IsFinite || !boreRay.Direction.IsFinite || boreRay.Direction.Length <= GeometryTolerance.Length) throw new ArgumentException("Shot bore ray must be initialized and finite.", nameof(boreRay)); Timestamp=timestamp; BoreRay=boreRay; TrackingValid=trackingValid; IsTriggerFallingEdge=isTriggerFallingEdge; }
    public MonotonicTimestamp Timestamp { get; } public Ray3d BoreRay { get; } public bool TrackingValid { get; } public bool IsTriggerFallingEdge { get; }
}
public interface IShotModel { IReadOnlyList<SessionEvent> Fire(ShotContext shot, string blockId, IEnumerable<TargetEntity> targets, Func<TargetEntity,bool> isVisible); }
public sealed class HitscanShotModel : IShotModel
{
    private readonly ISessionEventSequence sequence; private readonly TimeSpan? minimumShotInterval; private MonotonicTimestamp? lastAcceptedShot;
    public HitscanShotModel(ISessionEventSequence sequence, TimeSpan? minimumShotInterval = null)
    { this.sequence=sequence ?? throw new ArgumentNullException(nameof(sequence)); if (minimumShotInterval.HasValue && minimumShotInterval.Value < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(minimumShotInterval)); this.minimumShotInterval=minimumShotInterval; }
    public IReadOnlyList<SessionEvent> Fire(ShotContext shot, string blockId, IEnumerable<TargetEntity> targets, Func<TargetEntity,bool> isVisible)
    {
        if (shot == null) throw new ArgumentNullException(nameof(shot)); if (targets == null) throw new ArgumentNullException(nameof(targets)); if (isVisible == null) throw new ArgumentNullException(nameof(isVisible)); if (String.IsNullOrWhiteSpace(blockId)) throw new ArgumentException("A block identifier is required.", nameof(blockId));
        if (lastAcceptedShot.HasValue && shot.Timestamp < lastAcceptedShot.Value) throw new ArgumentException("Shot timestamps cannot move backward within one model.", nameof(shot));
        if (!shot.TrackingValid || !shot.IsTriggerFallingEdge || IsLockedOut(shot.Timestamp)) return Array.Empty<SessionEvent>();
        lastAcceptedShot=shot.Timestamp; var all=new List<SessionEvent>{Event(shot.Timestamp,"ShotFired",blockId,null)}; TargetEntity? hit=null; double nearest=Double.PositiveInfinity;
        foreach(var target in targets) if(target.State==TargetState.Active && isVisible(target) && Sphere(shot.BoreRay,target,out double distance) && distance<nearest) { nearest=distance; hit=target; }
        if(hit != null) { hit.Destroy(); all.Add(Event(shot.Timestamp,"TargetHit",blockId,hit.Id)); all.Add(Event(shot.Timestamp,"TargetDestroyed",blockId,hit.Id)); }
        return all;
    }
    private bool IsLockedOut(MonotonicTimestamp timestamp) => minimumShotInterval.HasValue && lastAcceptedShot.HasValue && timestamp.Ticks-lastAcceptedShot.Value.Ticks<minimumShotInterval.Value.Ticks;
    private SessionEvent Event(MonotonicTimestamp time,string type,string blockId,string? targetId) { var fields=new List<LogField>{LogField.String("blockId",blockId)}; if(targetId!=null) fields.Add(LogField.String("targetId",targetId)); return new SessionEvent(sequence.Next(),time,type,fields); }
    private static bool Sphere(Ray3d ray, TargetEntity target,out double distance) { var oc=ray.Origin-target.Position; double b=Vector3d.Dot(oc,ray.Direction); double c=Vector3d.Dot(oc,oc)-target.RadiusM*target.RadiusM; double disc=b*b-c; if(disc<0) { distance=0; return false; } distance=-b-Math.Sqrt(disc); if(distance<=0) distance=-b+Math.Sqrt(disc); return distance>0; }
}

public interface IScoringRule { int Score(string blockId, MonotonicTimestamp start, MonotonicTimestamp end, IEnumerable<SessionEvent> events); }
public sealed class TargetsDestroyedCount : IScoringRule
{
    public int Score(string blockId, MonotonicTimestamp start, MonotonicTimestamp end, IEnumerable<SessionEvent> events)
    { if(events==null) throw new ArgumentNullException(nameof(events)); if(end<start) throw new ArgumentException("Block end precedes start."); var ids=new HashSet<string>(StringComparer.Ordinal); foreach(var item in events) if(item.EventType=="TargetDestroyed" && item.Timestamp>=start && item.Timestamp<end && Field(item,"blockId")==blockId) { var id=Field(item,"targetId"); if(id==null) throw new ArgumentException("TargetDestroyed requires scalar targetId."); if(!ids.Add(id)) throw new ArgumentException("Duplicate target destruction in a block."); } return ids.Count; }
    private static string? Field(SessionEvent item,string name) { foreach(var field in item.Fields) if(field.Name==name) return field.Text; return null; }
    public int ScoreCompletedPrimaryBlock(string blockId, IEnumerable<SessionEvent> events)
    {
        if(events==null) throw new ArgumentNullException(nameof(events)); SessionEvent? started=null, ended=null;
        foreach(var item in events) { if(Field(item,"blockId")!=blockId) continue; if(item.EventType=="BlockStarted") { if(started!=null) throw new ArgumentException("A completed block has exactly one BlockStarted event."); started=item; } if(item.EventType=="BlockEnded") { if(ended!=null) throw new ArgumentException("A completed block has exactly one BlockEnded event."); ended=item; } if(item.EventType=="BlockAborted") throw new ArgumentException("An aborted block cannot produce a completed primary DV."); }
        if(started==null || ended==null) throw new ArgumentException("Primary scoring requires BlockStarted and BlockEnded events."); if(ended.Timestamp.Ticks-started.Timestamp.Ticks!=TimeSpan.FromSeconds(300).Ticks) throw new ArgumentException("A completed primary block must span exactly 300 seconds."); return Score(blockId,started.Timestamp,ended.Timestamp,events);
    }
}

public sealed class SessionPlan
{
    private static readonly HashSet<string> Required=new HashSet<string>(new[]{"WE_MT","NE_MT","WE_FT","NE_FT"},StringComparer.Ordinal);
    public SessionPlan(string participantId, IEnumerable<string> order, double conditionDurationSeconds=300, bool allowDevelopmentOnlyDurationOverride=false)
    { if(String.IsNullOrWhiteSpace(participantId)) throw new ArgumentException("Participant id is required."); var list=(order??throw new ArgumentNullException(nameof(order))).ToArray(); if(!new HashSet<string>(list,StringComparer.Ordinal).SetEquals(Required)||list.Length!=4) throw new ArgumentException("Order must be a permutation of the four conditions."); if(!ScenarioDefinition.PositiveFinite(conditionDurationSeconds)||(conditionDurationSeconds!=300&&!allowDevelopmentOnlyDurationOverride)) throw new ArgumentException("Condition duration is fixed at 300 seconds unless an explicit development-only override is supplied.",nameof(conditionDurationSeconds)); ParticipantId=participantId; ConditionOrder=Array.AsReadOnly(list); ConditionDurationSeconds=conditionDurationSeconds; AllowsDevelopmentOnlyDurationOverride=allowDevelopmentOnlyDurationOverride; }
    public string ParticipantId { get; } public IReadOnlyList<string> ConditionOrder { get; } public double ConditionDurationSeconds { get; } public bool AllowsDevelopmentOnlyDurationOverride { get; }
}

internal static class DeterministicSeed { public static int Hash(string a,string b) { unchecked { int hash=17; foreach(char c in (a??String.Empty)+"|"+(b??String.Empty)) hash=hash*31+c; return hash&Int32.MaxValue; } } }
internal sealed class DeterministicRandom { private uint state; public DeterministicRandom(int seed) { state=(uint)seed; if(state==0) state=1; } public double Next() { state=1664525u*state+1013904223u; return state/(double)UInt32.MaxValue; } }
}
