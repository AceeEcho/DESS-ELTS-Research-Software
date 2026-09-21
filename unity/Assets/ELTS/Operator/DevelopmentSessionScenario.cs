#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Config;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

namespace Elts.Operator
{
    /// <summary>
    /// Synthetic scenario presentation and canonical target recording. Spawn
    /// dimensions and random-walk timing below are development choices, not
    /// approved D-04 tuning. All positions and velocities use room metres.
    /// </summary>
    public sealed class DevelopmentSessionScenario
    {
        private const int MaintainTargets = 3;
        private const double SpawnHalfWidthM = 0.45, SpawnHalfHeightM = 0.25, SpawnHalfDepthM = 0.05;
        private const double DirectionChangeSeconds = 1;
        private readonly DevelopmentConfiguration config;
        private readonly ISharedClock clock;
        private readonly SessionEngine engine;
        private readonly SessionRecordingAdapter recording;
        private readonly HitscanShotModel shots;
        private ScenarioPopulation? population;
        private ScenarioDefinition? definition;
        private IMovementBehaviour? movement;
        private string? blockId;
        private int seed;
        private long targetSequence;
        private readonly Dictionary<string,TargetState> recordedStates = new Dictionary<string,TargetState>();
        private readonly Dictionary<string,Vector3d> positions = new Dictionary<string,Vector3d>();
        public IReadOnlyDictionary<string,Vector3d> Positions => positions;
        public string LastShot { get; private set; } = "No shot";
        public int ShotCount { get; private set; }
        public int HitCount { get; private set; }
        public string? RecordedBlockId => blockId;

        public DevelopmentSessionScenario(DevelopmentConfiguration config, ISharedClock clock,
            SessionEngine engine, SessionRecordingAdapter recording, ISessionEventSequence sequence)
        {
            this.config=config;this.clock=clock;this.engine=engine;this.recording=recording;
            shots=new HitscanShotModel(sequence,TimeSpan.FromSeconds(config.Runtime.TriggerLockoutSeconds));
        }

        public void Refresh(double deltaSeconds, RigidPose? head)
        {
            // Preserve target identity, position, movement state and score across a
            // pause. Acquisition continues, but no target updates or shots occur.
            if(engine.State == SessionState.BlockPaused)return;
            positions.Clear();
            if(!engine.TargetsActive)
            {
                RemoveRemainingTargets();
                return;
            }
            if(blockId!=engine.CurrentBlockId) BeginBlock();
            if(population==null || definition==null || movement==null) return;
            // Missing head data cannot invent visibility or a fresh spawn.
            if(!head.HasValue) return;
            var eye=head.Value.TransformPoint(config.Rig.HeadEyeOffsetM);
            Func<Vector3d,bool> visible=point=>IsVisible(eye,point) && HasSpawnClearance(eye,point);
            // Target lifetime and respawn delays follow active test time as well.
            // Logged snapshots below still use the unchanged acquisition clock.
            population.Reconcile(new MonotonicTimestamp(checked((long)(engine.CurrentActiveSeconds*TimeSpan.TicksPerSecond))),visible);
            foreach(var target in population.History)
            {
                bool known=recordedStates.TryGetValue(target.Id,out var previous);
                if(target.State==TargetState.Active)
                {
                    movement.Step(target,Math.Max(0,deltaSeconds),definition.SpawnVolume);
                    Record(target,known?TargetLifecycle.Updated:TargetLifecycle.Spawned);
                    positions.Add(target.Id,target.Position);
                }
                else if(!known || previous==TargetState.Active)
                    Record(target,target.State==TargetState.Destroyed?TargetLifecycle.Destroyed:TargetLifecycle.Despawned);
                recordedStates[target.Id]=target.State;
            }
        }

        public void Fire(RigidPose? weapon, RigidPose? head)
            => Fire(weapon,head,clock.Now);

        public void Fire(RigidPose? weapon, RigidPose? head, MonotonicTimestamp observedAt)
        {
            if(!engine.TargetsActive || population==null) return;
            if(!weapon.HasValue || !head.HasValue) { LastShot="Ignored: tracking unavailable"; return; }
            var ray=new BoreRay(weapon.Value,config.Rig.WeaponMuzzleOffsetM,config.Rig.WeaponZero,config.Rig.WeaponBoreLocalDirection).Ray;
            var eye=head.Value.TransformPoint(config.Rig.HeadEyeOffsetM);
            bool accepted=engine.Fire(shots,new ShotContext(observedAt,ray,true),population.ActiveTargets,target=>IsVisible(eye,target.Position));
            if(accepted)ShotCount++;
            LastShot=accepted?"Shot logged":"Shot not recorded (lockout or failure)";
            foreach(var target in population.History)
            {
                if(target.State!=TargetState.Destroyed || (recordedStates.TryGetValue(target.Id,out var previous) && previous==TargetState.Destroyed)) continue;
                Record(target,TargetLifecycle.Destroyed);
                recordedStates[target.Id]=target.State;
                positions.Remove(target.Id);
                HitCount++;
                LastShot="Target destroyed";
            }
            // Refill before the next rendered frame. Refresh records the new spawn
            // on the same acquisition clock without moving surviving targets.
            if(accepted) Refresh(0,head);
        }

        private void BeginBlock()
        {
            RemoveRemainingTargets();
            blockId=engine.CurrentBlockId;
            ShotCount=0;HitCount=0;LastShot="Aim at a target, then click and release.";
            var screen=config.Rig.Display;
            var center=screen.Origin+screen.U*(screen.Width*0.5)+screen.V*(screen.Height*0.5)
                +screen.Normal*config.Scenario.TargetDistanceM;
            var bounds=new SpawnVolume(center-new Vector3d(SpawnHalfWidthM,SpawnHalfHeightM,SpawnHalfDepthM),
                center+new Vector3d(SpawnHalfWidthM,SpawnHalfHeightM,SpawnHalfDepthM));
            bool moving=engine.CurrentCondition!.EndsWith("_MT",StringComparison.Ordinal);
            var adapter=DevelopmentScenarioAdapter.Create(config.Scenario,bounds,MaintainTargets,
                moving?"RandomWalk":"Fixed","Hitscan","TargetsDestroyedCount");
            definition=adapter.Definition;
            seed=definition.BlockSeed("synthetic",blockId!);
            population=new ScenarioPopulation(definition,new MaintainCountSpawnRule(seed));
            movement=moving?(IMovementBehaviour)new RandomWalkMovement(seed,config.Scenario.TargetSpeedMps,
                config.Scenario.TargetSpeedMps,DirectionChangeSeconds):new FixedMovement();
            recordedStates.Clear();
        }

        private bool IsVisible(Vector3d eye, Vector3d target)
        {
            var direction=target-eye;
            if(direction.Length<=0) return false;
            return config.Rig.Display.Intersect(new Ray3d(eye,direction),out var distance,out _,out var u,out var v)
                && distance<direction.Length && config.Rig.Display.IsInside(u,v);
        }
        private bool HasSpawnClearance(Vector3d eye, Vector3d candidate)
        {
            // Compare apparent sphere radii, so a replacement cannot hide behind
            // another target even when they occupy different world depths.
            var direction=candidate-eye;
            if(direction.Length<=config.Scenario.TargetRadiusM) return false;
            foreach(var target in population!.ActiveTargets)
            {
                var other=target.Position-eye;
                if(other.Length<=target.RadiusM) return false;
                double clearance=config.Scenario.TargetRadiusM/direction.Length+target.RadiusM/other.Length;
                if((direction.Normalized()-other.Normalized()).Length<clearance*1.2) return false;
            }
            return true;
        }
        private void Record(TargetEntity target, TargetLifecycle lifecycle)
        {
            if(!recording.TryLogTarget(new TargetSnapshot(++targetSequence,clock.Now,target.Id,blockId!,lifecycle,
                target.Position,target.Velocity,seed,config.Scenario.ScenarioId)))
                throw new InvalidOperationException("Critical target recording failed.");
        }
        private void RemoveRemainingTargets()
        {
            if(population==null) return;
            // Destroyed is already a terminal recorded lifecycle. Only targets
            // still active need a Despawned row when the block exits.
            foreach(var target in population.ActiveTargets) Record(target,TargetLifecycle.Despawned);
            population=null;definition=null;movement=null;positions.Clear();
        }
    }
}
