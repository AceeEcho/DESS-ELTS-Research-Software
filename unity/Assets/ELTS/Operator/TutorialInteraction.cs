#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Config;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;

namespace Elts.Operator
{
    /// <summary>
    /// A stationary instance of the real game target, with the same anatomy and
    /// magazine model. Its events carry a separate scope and never enter a scored
    /// block. Raw trigger observations are admitted by the session adapter first.
    /// </summary>
    public sealed class TutorialInteraction
    {
        private readonly DevelopmentConfiguration config;
        private readonly ISharedClock clock;
        private readonly ISessionEventSequence sequence;
        private readonly Action<SessionEvent> record;
        private readonly HumanoidShotModel shots;
        private readonly ManualMagazine magazine;
        private readonly Dictionary<string,Vector3d> positions=new Dictionary<string,Vector3d>();
        private readonly Dictionary<string,DevelopmentFixedPose> poses=new Dictionary<string,DevelopmentFixedPose>();
        private readonly Vector3d center;
        private TargetEntity? target;
        private int targetOrdinal;
        public string Scope { get; }
        public string InteractionId { get; }
        public int Ammo=>magazine.Remaining;
        public IReadOnlyDictionary<string,Vector3d> Positions=>positions;
        public IReadOnlyDictionary<string,DevelopmentFixedPose> FixedPoses=>poses;
        public string Feedback { get; private set; }="";

        public TutorialInteraction(DevelopmentConfiguration config,ISharedClock clock,ISessionEventSequence sequence,
            Action<SessionEvent> record,int capacity,string scope,int attempt,double distanceM)
        {
            this.config=config;this.clock=clock;this.sequence=sequence;this.record=record;Scope=scope;
            InteractionId=scope.ToLowerInvariant()+"-"+attempt.ToString("D2");
            magazine=new ManualMagazine(capacity);
            var screen=config.Rig.Display;
            var middle=screen.Origin+screen.U*(screen.Width*.5)+screen.V*(screen.Height*.5)+screen.Normal*distanceM;
            center=new Vector3d(middle.X,HumanoidGeometry.StandingCenterAt(config.Rig.VirtualFloorY),middle.Z);
            shots=new HumanoidShotModel(sequence,TimeSpan.FromSeconds(config.Runtime.TriggerLockoutSeconds),
                Array.Empty<HumanoidCover>(),config.Rig.VirtualFloorY);
            Record("Started",new[]{LogField.NumberValue("magazineCapacity",capacity)});
            Spawn();
        }
        private void Spawn()
        {
            target=new TargetEntity(InteractionId+"-target-"+(++targetOrdinal),center,Vector3d.Zero,config.Scenario.TargetRadiusM,clock.Now);
            target.Activate();positions.Clear();poses.Clear();positions.Add(target.Id,target.Position);
            poses.Add(target.Id,new DevelopmentFixedPose("TutorialStanding",0,0,0,false,0,0));
            Record("TargetSpawned",new[]{LogField.String("targetId",target.Id),LogField.NumberValue("worldX",center.X),
                LogField.NumberValue("worldY",center.Y),LogField.NumberValue("worldZ",center.Z)});
        }
        public bool Fire(RigidPose weapon,RigidPose head,MonotonicTimestamp observedAt)
        {
            if(target==null)return false;
            if(magazine.Remaining==0){Record("DryFire",Array.Empty<LogField>());Feedback="Reload to continue";return false;}
            var ray=new BoreRay(weapon,config.Rig.WeaponMuzzleOffsetM,config.Rig.WeaponZero,config.Rig.WeaponBoreLocalDirection).Ray;
            var eye=head.TransformPoint(config.Rig.HeadEyeOffsetM);
            shots.MagazineBefore=magazine.Remaining;
            foreach(var item in shots.Fire(new ShotContext(observedAt,ray,true),InteractionId,new[]{target},value=>Visible(eye,value.Position)))
            {
                var fields=new List<LogField>(item.Fields) {LogField.String("scope",Scope),LogField.Boolean("scored",false)};
                record(new SessionEvent(item.Sequence,item.Timestamp,Scope+item.EventType,fields));
            }
            if(!shots.Accepted)return false;
            magazine.TryConsume();Feedback=shots.Outcome=="Hit"?"Target hit":"Shot observed";
            if(target.State==TargetState.Destroyed)Spawn();
            return true;
        }
        public bool Reload()
        {
            if(magazine.Remaining==magazine.Capacity)return false;
            Record("Reloaded",new[]{LogField.NumberValue("magazineBefore",magazine.Remaining),LogField.NumberValue("magazineAfter",magazine.Capacity)});
            magazine.Reload();Feedback="Reload observed";return true;
        }
        public void End()
        {
            if(target==null)return;
            Record("TargetDespawned",new[]{LogField.String("targetId",target.Id)});
            Record("Ended",Array.Empty<LogField>());target=null;positions.Clear();poses.Clear();
        }
        private bool Visible(Vector3d eye,Vector3d point)
        {
            var direction=point-eye;
            return direction.Length>0 && config.Rig.Display.Intersect(new Ray3d(eye,direction),out var distance,out _,out var u,out var v)
                && distance<direction.Length && config.Rig.Display.IsInside(u,v);
        }
        private void Record(string name,IReadOnlyList<LogField> extra)
        {
            var fields=new List<LogField>(extra){LogField.String("blockId",InteractionId),LogField.String("scope",Scope),LogField.Boolean("scored",false)};
            record(new SessionEvent(sequence.Next(),clock.Now,Scope+name,fields));
        }
    }
}
