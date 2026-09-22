#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;

namespace Elts.Scenario
{
    /// <summary>Manual, finite magazine. The caller supplies one accepted shot
    /// per trigger edge and explicitly requests every reload.</summary>
    public sealed class ManualMagazine
    {
        public int Capacity {get;}
        public int Remaining {get;private set;}
        public ManualMagazine(int capacity)
        {if(capacity<1)throw new ArgumentOutOfRangeException(nameof(capacity));Capacity=capacity;Remaining=capacity;}
        public bool TryConsume()
        {if(Remaining==0)return false;Remaining--;return true;}
        public bool Reload()
        {if(Remaining==Capacity)return false;Remaining=Capacity;return true;}
    }

    /// <summary>One shared synthetic anatomy in metres. The render model uses these
    /// dimensions too, so visible body regions agree with the recorded hit test.</summary>
    public static class HumanoidGeometry
    {
        public const double StandingCenterY=-0.15, CrouchedCenterY=-0.45;
        public const double CrouchScale=0.65;
        public static bool IsCrouched(Vector3d position)=>position.Y<(StandingCenterY+CrouchedCenterY)*0.5;
        public static double HeightScale(Vector3d position)
        {
            double crouch=(StandingCenterY-position.Y)/(StandingCenterY-CrouchedCenterY);
            return 1-(Math.Max(0,Math.Min(1,crouch))*(1-CrouchScale));
        }

        public static IReadOnlyList<BodyPart> Parts(Vector3d center)
        {
            double height=HeightScale(center);
            BodyPart Part(string region,Vector3d offset,Vector3d radii)
                =>new BodyPart(region,center+new Vector3d(offset.X,offset.Y*height,offset.Z),
                    new Vector3d(radii.X,radii.Y*height,radii.Z));
            return new[]{
                Part("head",new Vector3d(0,.65,0),new Vector3d(.115,.17,.11)),
                Part("body",new Vector3d(0,.19,0),new Vector3d(.235,.35,.13)),
                Part("body",new Vector3d(0,-.17,0),new Vector3d(.20,.17,.13)),
                Part("limb",new Vector3d(-.30,.18,0),new Vector3d(.14,.30,.10)),
                Part("limb",new Vector3d(.30,.18,0),new Vector3d(.14,.30,.10)),
                Part("limb",new Vector3d(-.30,-.11,0),new Vector3d(.07,.12,.08)),
                Part("limb",new Vector3d(.30,-.11,0),new Vector3d(.07,.12,.08)),
                Part("limb",new Vector3d(-.11,-.55,0),new Vector3d(.09,.30,.105)),
                Part("limb",new Vector3d(.11,-.55,0),new Vector3d(.09,.30,.105))};
        }
    }

    public readonly struct BodyPart
    {
        public readonly string Region;
        public readonly Vector3d Center,Radii;
        public BodyPart(string region,Vector3d center,Vector3d radii)
        {Region=region;Center=center;Radii=radii;}
    }

    /// <summary>World-axis cover volume; the same dimensions make the visible prop.</summary>
    public readonly struct HumanoidCover
    {
        public readonly string Id;
        public readonly Vector3d Center,HalfSize;
        public HumanoidCover(string id,Vector3d center,Vector3d halfSize)
        {Id=id;Center=center;HalfSize=halfSize;}
    }

    /// <summary>Three-health development shot model. One falling trigger edge emits
    /// one ShotFired event. Cover wins when it is nearer than every body part.</summary>
    public sealed class HumanoidShotModel : IShotModel
    {
        private readonly ISessionEventSequence sequence;
        private readonly TimeSpan minimumInterval;
        private readonly IReadOnlyList<HumanoidCover> covers;
        private MonotonicTimestamp? previous;
        public bool Accepted {get;private set;}
        public string Outcome {get;private set;}="";
        public string Region {get;private set;}="";
        public int Damage {get;private set;}
        public int RemainingHealth {get;private set;}
        public string TargetId {get;private set;}="";
        /// <summary>Set by the development weapon immediately before dispatch.</summary>
        public int MagazineBefore {get;set;}
        public HumanoidShotModel(ISessionEventSequence sequence,TimeSpan minimumInterval,IReadOnlyList<HumanoidCover> covers)
        {this.sequence=sequence;this.minimumInterval=minimumInterval;this.covers=covers;}

        public IReadOnlyList<SessionEvent> Fire(ShotContext shot,string blockId,IEnumerable<TargetEntity> targets,Func<TargetEntity,bool> isVisible)
        {
            Accepted=false;Outcome="";Region="";Damage=0;TargetId="";RemainingHealth=0;
            if(!shot.TrackingValid || !shot.IsTriggerFallingEdge ||
                (previous.HasValue && shot.Timestamp.Ticks-previous.Value.Ticks<minimumInterval.Ticks))
                return Array.Empty<SessionEvent>();
            if(previous.HasValue && shot.Timestamp<previous.Value)throw new ArgumentException("Shot time moved backward.");
            previous=shot.Timestamp;Accepted=true;
            TargetEntity? hit=null;string region="";double nearest=Double.PositiveInfinity;
            var visible=new List<TargetEntity>();
            foreach(var target in targets)
            {
                if(target.State!=TargetState.Active || !isVisible(target))continue;
                visible.Add(target);
                foreach(var part in HumanoidGeometry.Parts(target.Position))
                    if(RayEllipsoid(shot.BoreRay,part.Center,part.Radii,out double distance) && distance<nearest)
                    {nearest=distance;hit=target;region=part.Region;}
            }
            string coverId="";double coverDistance=Double.PositiveInfinity;
            foreach(var cover in covers)
                if(RayBox(shot.BoreRay,cover.Center,cover.HalfSize,out double distance) && distance<coverDistance)
                {coverDistance=distance;coverId=cover.Id;}
            if(coverDistance<nearest){hit=null;region="";}
            Outcome=hit!=null?"Hit":coverDistance<Double.PositiveInfinity?"Cover":"Miss";
            var fields=AimGeometry.Fields(shot.BoreRay,visible,hit);
            fields.Add(LogField.String("blockId",blockId));
            fields.Add(LogField.String("outcome",Outcome));
            if(MagazineBefore>0)
            {
                fields.Add(LogField.NumberValue("magazineBefore",MagazineBefore));
                fields.Add(LogField.NumberValue("magazineAfter",MagazineBefore-1));
            }
            if(Outcome=="Cover")fields.Add(LogField.String("coverId",coverId));
            var events=new List<SessionEvent>();
            if(hit!=null)
            {
                Region=region;TargetId=hit.Id;
                Damage=region=="head"?3:region=="body"?2:1;
                hit.ApplyDamage(Damage);RemainingHealth=hit.Health;
                fields.Add(LogField.String("targetId",TargetId));
                fields.Add(LogField.String("hitRegion",Region));
                fields.Add(LogField.NumberValue("damage",Damage));
                fields.Add(LogField.NumberValue("remainingHealth",RemainingHealth));
            }
            events.Add(new SessionEvent(sequence.Next(),shot.Timestamp,"ShotFired",fields));
            if(hit!=null)
            {
                var hitFields=new[]{LogField.String("blockId",blockId),LogField.String("targetId",TargetId),
                    LogField.String("hitRegion",Region),LogField.NumberValue("damage",Damage),LogField.NumberValue("remainingHealth",RemainingHealth)};
                events.Add(new SessionEvent(sequence.Next(),shot.Timestamp,"TargetHit",hitFields));
                if(hit.State==TargetState.Destroyed)
                    events.Add(new SessionEvent(sequence.Next(),shot.Timestamp,"TargetDestroyed",hitFields));
            }
            return events;
        }

        private static bool RayEllipsoid(Ray3d ray,Vector3d center,Vector3d radius,out double distance)
        {
            var o=ray.Origin-center;var d=ray.Direction;
            double a=d.X*d.X/(radius.X*radius.X)+d.Y*d.Y/(radius.Y*radius.Y)+d.Z*d.Z/(radius.Z*radius.Z);
            double b=2*(o.X*d.X/(radius.X*radius.X)+o.Y*d.Y/(radius.Y*radius.Y)+o.Z*d.Z/(radius.Z*radius.Z));
            double c=o.X*o.X/(radius.X*radius.X)+o.Y*o.Y/(radius.Y*radius.Y)+o.Z*o.Z/(radius.Z*radius.Z)-1;
            double discriminant=b*b-4*a*c;
            if(discriminant<0){distance=0;return false;}
            distance=(-b-Math.Sqrt(discriminant))/(2*a);
            if(distance<=0)distance=(-b+Math.Sqrt(discriminant))/(2*a);
            return distance>0;
        }

        private static bool RayBox(Ray3d ray,Vector3d center,Vector3d half,out double distance)
        {
            double enter=0,exit=Double.PositiveInfinity;
            double[] origin={ray.Origin.X,ray.Origin.Y,ray.Origin.Z};
            double[] direction={ray.Direction.X,ray.Direction.Y,ray.Direction.Z};
            double[] middle={center.X,center.Y,center.Z},size={half.X,half.Y,half.Z};
            for(int axis=0;axis<3;axis++)
            {
                double low=middle[axis]-size[axis],high=middle[axis]+size[axis];
                if(Math.Abs(direction[axis])<1e-12)
                {if(origin[axis]<low || origin[axis]>high){distance=0;return false;}continue;}
                double a=(low-origin[axis])/direction[axis],b=(high-origin[axis])/direction[axis];
                enter=Math.Max(enter,Math.Min(a,b));exit=Math.Min(exit,Math.Max(a,b));
                if(enter>exit){distance=0;return false;}
            }
            distance=enter>0?enter:exit;
            return distance>0;
        }
    }
}
