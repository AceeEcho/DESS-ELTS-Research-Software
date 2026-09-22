#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Config;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using Elts.Tracking;

namespace Elts.Operator
{
    /// <summary>
    /// Synthetic scenario presentation and canonical target recording. Spawn
    /// dimensions and random-walk timing below are development choices, not
    /// approved D-04 tuning. All positions and velocities use room metres.
    /// </summary>
    public sealed class DevelopmentSessionScenario
    {
        private const double SpawnHalfWidthM = 0.45, SpawnHalfHeightM = 0.25, SpawnHalfDepthM = 0.05;
        private readonly DevelopmentConfiguration config;
        private readonly DevelopmentGameSettings gameSettings;
        private readonly ISharedClock clock;
        private readonly SessionEngine engine;
        private readonly SessionRecordingAdapter recording;
        private HumanoidShotModel? shots;
        private ManualMagazine magazine;
        private readonly ISessionEventSequence eventSequence;
        private double nextAimAt;
        private ScenarioPopulation? population;
        private ScenarioDefinition? definition;
        private Vector3d layoutCenter;
        private bool movingBlock;
        private string? blockId;
        private int seed;
        private long targetSequence;
        private readonly Dictionary<string,TargetState> recordedStates = new Dictionary<string,TargetState>();
        private readonly Dictionary<string,Vector3d> positions = new Dictionary<string,Vector3d>();
        public IReadOnlyDictionary<string,Vector3d> Positions => positions;
        public string LastShot { get; private set; } = "No shot";
        public int ShotCount { get; private set; }
        public int HitCount { get; private set; }
        public int HeadHits {get;private set;}
        public int BodyHits {get;private set;}
        public int LimbHits {get;private set;}
        public int CoverHits {get;private set;}
        public int MissCount {get;private set;}
        public int TargetKills {get;private set;}
        public int DamageDealt {get;private set;}
        public int DryFires {get;private set;}
        public int Reloads {get;private set;}
        public int AmmoRemaining => magazine.Remaining;
        public int MagazineCapacity=>gameSettings.magazineCapacity;
        public string? RecordedBlockId => blockId;

        public DevelopmentSessionScenario(DevelopmentConfiguration config, ISharedClock clock,
            SessionEngine engine, SessionRecordingAdapter recording, ISessionEventSequence sequence,
            DevelopmentGameSettings? gameSettings=null)
        {
            this.config=config;this.clock=clock;this.engine=engine;this.recording=recording;
            this.gameSettings=(gameSettings??new DevelopmentGameSettings()).Copy();this.gameSettings.Validate();
            magazine=new ManualMagazine(MagazineCapacity);
            eventSequence=sequence;
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
            if(population==null || definition==null) return;
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
                    // The trajectory depends on active block time, never frame count.
                    // Pause and replay therefore preserve the same scripted motion.
                    var next=DevelopmentScenarioLayout.Position(target,layoutCenter,movingBlock,engine.CurrentActiveSeconds,gameSettings);
                    var velocity=deltaSeconds>0?(next-target.Position)/deltaSeconds:Vector3d.Zero;
                    target.Move(next,velocity);
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

        /// <summary>Sample the current unpredicted bore at a bounded, configured
        /// cadence. This is frame-sampled telemetry; the full raw tracker stream
        /// remains independent. Stale/invalid observations break motion segments.</summary>
        public void ObserveAim(TrackingSamplePair? pair)
        {
            if(!engine.TargetsActive || population==null)return;
            double active=engine.CurrentActiveSeconds;
            if(active<nextAimAt)return;
            nextAimAt=active+config.Runtime.AimObservationIntervalSeconds;
            var now=clock.Now;
            bool valid=pair.HasValue && pair.Value.Head.HasValidPose && pair.Value.Weapon.HasValidPose &&
                now.Ticks>=pair.Value.Timestamp.Ticks && (now.Ticks-pair.Value.Timestamp.Ticks)/(double)TimeSpan.TicksPerSecond<=config.Runtime.AimMaximumGapSeconds;
            var fields=new List<LogField> {LogField.String("blockId",blockId!),LogField.Boolean("valid",valid),
                LogField.NumberValue("activeSeconds",active),LogField.NumberValue("maximumGapSeconds",config.Runtime.AimMaximumGapSeconds),
                LogField.NumberValue("observationIntervalSeconds",config.Runtime.AimObservationIntervalSeconds)};
            if(valid)
            {
                var ray=new BoreRay(pair!.Value.Weapon.Pose!.Value,config.Rig.WeaponMuzzleOffsetM,config.Rig.WeaponZero,config.Rig.WeaponBoreLocalDirection).Ray;
                var eye=pair.Value.Head.Pose!.Value.TransformPoint(config.Rig.HeadEyeOffsetM);
                var visible=new List<TargetEntity>();
                foreach(var target in population.ActiveTargets)if(IsVisible(eye,target.Position))visible.Add(target);
                fields.AddRange(AimGeometry.Fields(ray,visible));
                fields.Add(LogField.NumberValue("observationTicks",pair.Value.Timestamp.Ticks));
            }
            if(!recording.TryRecord(new SessionEvent(eventSequence.Next(),now,"AimObserved",fields)))
                throw new InvalidOperationException("Aim observation recording failed.");
        }

        public void Fire(RigidPose? weapon, RigidPose? head, MonotonicTimestamp observedAt)
        {
            if(!engine.TargetsActive || population==null || shots==null) return;
            if(AmmoRemaining==0)
            {
                DryFires++;LastShot="Magazine empty — press R to reload.";
                if(!recording.TryRecord(new SessionEvent(eventSequence.Next(),observedAt,"WeaponDryFire",new[]{
                    LogField.String("blockId",blockId!),LogField.NumberValue("magazineRemaining",0)})))
                    throw new InvalidOperationException("Dry-fire recording failed.");
                return;
            }
            if(!weapon.HasValue || !head.HasValue) { LastShot="Ignored: tracking unavailable"; return; }
            var ray=new BoreRay(weapon.Value,config.Rig.WeaponMuzzleOffsetM,config.Rig.WeaponZero,config.Rig.WeaponBoreLocalDirection).Ray;
            var eye=head.Value.TransformPoint(config.Rig.HeadEyeOffsetM);
            shots.MagazineBefore=AmmoRemaining;
            bool accepted=engine.Fire(shots,new ShotContext(observedAt,ray,true),population.ActiveTargets,target=>IsVisible(eye,target.Position));
            if(accepted)
            {
                if(!magazine.TryConsume())throw new InvalidOperationException("Accepted shot exceeded the manual magazine.");
                ShotCount++;
                switch(shots.Outcome)
                {
                    case "Hit":
                        HitCount++;
                        DamageDealt+=shots.Damage;
                        if(shots.Region=="head")HeadHits++;
                        else if(shots.Region=="body")BodyHits++;
                        else LimbHits++;
                        LastShot=shots.Region+" hit · "+shots.Damage+" damage · "+shots.RemainingHealth+" health left";
                        break;
                    case "Cover":CoverHits++;LastShot="Cover hit";break;
                    default:MissCount++;LastShot="Miss";break;
                }
            }
            else LastShot="Shot not recorded (lockout or failure)";
            foreach(var target in population.History)
            {
                if(target.State!=TargetState.Destroyed || (recordedStates.TryGetValue(target.Id,out var previous) && previous==TargetState.Destroyed)) continue;
                Record(target,TargetLifecycle.Destroyed);
                recordedStates[target.Id]=target.State;
                positions.Remove(target.Id);
                TargetKills++;
                LastShot="Target down";
            }
            // Refill before the next rendered frame. Refresh records the new spawn
            // on the same acquisition clock without moving surviving targets.
            if(accepted) Refresh(0,head);
        }

        private void BeginBlock()
        {
            RemoveRemainingTargets();
            blockId=engine.CurrentBlockId;
            nextAimAt=0;
            ShotCount=0;HitCount=0;HeadHits=0;BodyHits=0;LimbHits=0;CoverHits=0;MissCount=0;
            TargetKills=0;DamageDealt=0;DryFires=0;Reloads=0;magazine=new ManualMagazine(MagazineCapacity);
            LastShot="Click and release to fire. Press R to reload.";
            var screen=config.Rig.Display;
            var center=screen.Origin+screen.U*(screen.Width*0.5)+screen.V*(screen.Height*0.5)
                +screen.Normal*config.Scenario.TargetDistanceM;
            layoutCenter=center;
            var bounds=new SpawnVolume(center-new Vector3d(SpawnHalfWidthM,SpawnHalfHeightM,SpawnHalfDepthM),
                center+new Vector3d(SpawnHalfWidthM,SpawnHalfHeightM,SpawnHalfDepthM));
            movingBlock=engine.CurrentCondition!.EndsWith("_MT",StringComparison.Ordinal);
            var adapter=DevelopmentScenarioAdapter.Create(config.Scenario,bounds,DevelopmentScenarioLayout.TargetCount,
                movingBlock?"ScriptedCoverRun":"Fixed","HumanoidHitscan","TargetsDestroyedCount");
            definition=adapter.Definition;
            seed=definition.BlockSeed("synthetic",blockId!);
            population=new ScenarioPopulation(definition,new MaintainCountSpawnRule(seed));
            shots=new HumanoidShotModel(eventSequence,TimeSpan.FromSeconds(config.Runtime.TriggerLockoutSeconds),
                DevelopmentScenarioLayout.Covers(center,gameSettings));
            if(!recording.TryRecord(new SessionEvent(eventSequence.Next(),clock.Now,"WeaponConfigured",new[]{
                LogField.String("blockId",blockId!),LogField.NumberValue("magazineCapacity",MagazineCapacity),
                LogField.Boolean("automaticReload",false),LogField.String("reloadAction","desktop-controls.reload")})))
                throw new InvalidOperationException("Weapon configuration recording failed.");
            recordedStates.Clear();
        }

        public bool Reload()
        {
            if(!engine.TargetsActive || population==null || AmmoRemaining==MagazineCapacity)return false;
            int before=AmmoRemaining;
            if(!recording.TryRecord(new SessionEvent(eventSequence.Next(),clock.Now,"WeaponReloaded",new[]{
                LogField.String("blockId",blockId!),LogField.NumberValue("magazineBefore",before),
                LogField.NumberValue("magazineAfter",MagazineCapacity)})))
                throw new InvalidOperationException("Reload recording failed.");
            magazine.Reload();Reloads++;LastShot="Reloaded · "+MagazineCapacity+" rounds";return true;
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
            population=null;definition=null;shots=null;positions.Clear();
        }
    }
}
