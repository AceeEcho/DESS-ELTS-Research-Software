#nullable enable
using System;
using System.Collections.Generic;
using Elts.Config;
using Elts.Geometry;
using Elts.Scenario;

namespace Elts.Operator
{
    /// <summary>Inspector-adjustable synthetic game values. These are development
    /// staging choices, not measured apparatus or approved study parameters.</summary>
    [Serializable]
    public sealed class DevelopmentGameSettings
    {
        public const int DefaultMagazineCapacity=20;
        public int magazineCapacity=DefaultMagazineCapacity;
        public int activeTargetCount=30, spawnPositionCount=50, pathSequenceLength=100000;
        public float libraryFirstRowOffsetM=16, libraryRowSpacingM=4, libraryColumnSpacingM=1.5f;
        // Lateral distance is the maximum excursion on either side of a station.
        public float libraryEntryXM=14, libraryPatrolDistanceM=1.4f, libraryPatrolDepthM=2.0f;
        public float runInSeconds=4.6f, peekCycleSeconds=6.2f, crouchSeconds=.45f;
        // Fixed-condition offsets are in virtual metres; yaw is relative to facing the participant.
        public float fixedLateralJitterM=.12f, fixedDepthJitterM=.3f, fixedSidePeekOffsetM=.10f;
        public float fixedPeekCrouch=.65f;
        public float[] fixedYawDegrees={0,30,-45,90,-90,135,-150,180};
        public void ApplyPopulation(ScenarioConfiguration scenario)
        {
            activeTargetCount=scenario.ActiveTargetCount;
            spawnPositionCount=scenario.SpawnPositionCount;
            pathSequenceLength=scenario.PathSequenceLength;
        }
        public DevelopmentGameSettings Copy()
        {
            var copy=(DevelopmentGameSettings)MemberwiseClone();
            copy.fixedYawDegrees=fixedYawDegrees==null?Array.Empty<float>():(float[])fixedYawDegrees.Clone();
            return copy;
        }
        public void Validate()
        {
            if(!Finite(fixedLateralJitterM,0,.18f) || !Finite(fixedDepthJitterM,0,.35f) ||
                !Finite(fixedSidePeekOffsetM,.05f,.18f) || !Finite(fixedPeekCrouch,.35f,.75f) ||
                fixedYawDegrees==null || fixedYawDegrees.Length==0)
                throw new ArgumentException("Fixed target pose settings are outside their synthetic ranges.");
            foreach(float angle in fixedYawDegrees)
                if(!Finite(angle,-180,180))throw new ArgumentException("Fixed target yaw must be finite and within -180 to 180 degrees.");
            if(activeTargetCount<1 || activeTargetCount>30 || (spawnPositionCount<31 || spawnPositionCount>50) || activeTargetCount>=spawnPositionCount ||
                pathSequenceLength<100000 || pathSequenceLength>1000000 ||
                !Finite(libraryFirstRowOffsetM,14,18) || !Finite(libraryRowSpacingM,3.5f,4.5f) ||
                !Finite(libraryColumnSpacingM,1.4f,1.6f) || !Finite(libraryEntryXM,14,16) ||
                !Finite(libraryPatrolDistanceM,.8f,2.0f) || !Finite(libraryPatrolDepthM,1.5f,2.3f) || magazineCapacity<1 || magazineCapacity>100 ||
                !Finite(runInSeconds,3.5f,8) || !Finite(peekCycleSeconds,3.5f,8) || !Finite(crouchSeconds,.4f,1.2f))
                throw new ArgumentException("Development game settings are outside their safe synthetic ranges.");
        }
        private static bool Finite(float value,float min,float max)=>!float.IsNaN(value) && !float.IsInfinity(value) && value>=min && value<=max;
    }

    /// <summary>One immutable fixed-target pose. The scenario owns offsets and crouch height;
    /// yaw and the sampled stride are presentation, using the existing approximate hit anatomy.</summary>
    public readonly struct DevelopmentFixedPose
    {
        public readonly string Name;
        public readonly float Crouch, YawDegrees, Phase;
        public readonly bool MidStride;
        public readonly double OffsetX, OffsetZ;
        public DevelopmentFixedPose(string name,float crouch,float yawDegrees,float phase,bool midStride,double offsetX,double offsetZ)
        { Name=name;Crouch=crouch;YawDegrees=yawDegrees;Phase=phase;MidStride=midStride;OffsetX=offsetX;OffsetZ=offsetZ; }
    }

    /// <summary>Library geometry and versioned, indexed choreography in virtual metres.</summary>
    internal static class DevelopmentScenarioLayout
    {
        public const int TargetCount=30;
        public const string Version="library-routes-v2";
        public const string FixedPoseVersion="library-fixed-poses-v1";
        public static DevelopmentFixedPose FixedPose(int slot,int ordinal,DevelopmentGameSettings settings)
        {
            // Match poses to furniture: crouch behind desks, peek above/around bookcases,
            // and hold a standing or mid-step pose in open spaces. No per-frame randomness.
            int kind=slot%5;
            float crouch=kind==1 || kind==3?1:kind==2?settings.fixedPeekCrouch:0;
            bool stride=kind==4 || (kind==0 && ordinal%2==1);
            string name=kind==1?"Crouched":kind==2?"PeekOver":kind==3?"PeekSide":stride?"FrozenStride":"Standing";
            double lateral=((ordinal*7+slot)%5-2)*settings.fixedLateralJitterM/2;
            // Kind 3's case is offset to the right; expose just the mannequin's left side.
            if(kind==3)lateral=-settings.fixedSidePeekOffsetM;
            double depth=((ordinal*3+slot)%5-2)*settings.fixedDepthJitterM/2;
            float yaw=settings.fixedYawDegrees[(ordinal*5+slot/10)%settings.fixedYawDegrees.Length];
            float phase=(ordinal%2==0?.15f:.65f);
            return new DevelopmentFixedPose(name,crouch,yaw,phase,stride,lateral,depth);
        }
        public static Vector3d Center(DevelopmentConfiguration config)
        {
            var screen=config.Rig.Display;
            return screen.Origin+screen.U*(screen.Width*.5)+screen.V*(screen.Height*.5)
                +screen.Normal*config.Scenario.TargetDistanceM;
        }
        public static int Ordinal(TargetEntity target)
            =>int.Parse(target.Id.Substring(target.Id.LastIndexOf('-')+1))-1;
        public readonly struct LibraryRoute
        {
            public readonly int Slot, Behaviour;
            public readonly double EntrySeconds, CycleSeconds;
            public LibraryRoute(int slot,int behaviour,double entrySeconds,double cycleSeconds)
            { Slot=slot;Behaviour=behaviour;EntrySeconds=entrySeconds;CycleSeconds=cycleSeconds; }
            public string Name => Behaviour==0?"BurstReturn":Behaviour==1?"HesitateReverse":Behaviour==2?"DuckAndRun":"LowDash";
        }
        // Indexed list, never runtime randomness. Each group visits all 50 stations
        // once. Store the recipe rather than megabytes of duplicated waypoints.
        public static LibraryRoute Route(int ordinal,DevelopmentGameSettings settings)
        {
            int index=ordinal%settings.pathSequenceLength;
            uint variant=unchecked((uint)index*747796405u+2891336453u);
            variant=unchecked(((variant>>((int)(variant>>28)+4))^variant)*277803737u);
            variant=(variant>>22)^variant;
            int stride=settings.spawnPositionCount==34?19:17; // Coprime to every supported position count.
            int slot=(index*stride+index/settings.spawnPositionCount*7)%settings.spawnPositionCount;
            return new LibraryRoute(slot,(int)(variant%4),settings.runInSeconds+(variant%5)*.3,
                settings.peekCycleSeconds+(variant%7)*.25);
        }
        // The opening population is already in the room, at staggered route ages.
        // Replacements still enter from outside. This offset is also recorded for replay.
        public static double InitialRouteAge(int ordinal,DevelopmentGameSettings settings)
        {
            if(ordinal>=settings.activeTargetCount)return 0;
            var route=Route(ordinal,settings);
            return route.EntrySeconds+route.CycleSeconds*((ordinal*37+11)%101)/101.0;
        }
        public static Vector3d Station(Vector3d center,int slot,DevelopmentGameSettings settings,double floorY)
        {
            int row=slot/10,column=slot%10;
            double pitch=settings.libraryColumnSpacingM+row*.10;
            return new Vector3d(center.X+(column-4.5)*pitch+(row%2)*.3,
                HumanoidGeometry.StandingCenterAt(floorY),center.Z+settings.libraryFirstRowOffsetM+row*settings.libraryRowSpacingM);
        }
        public static IReadOnlyList<HumanoidCover> Covers(Vector3d center,DevelopmentGameSettings settings,double floorY=HumanoidGeometry.DefaultFloorY)
        {
            var covers=new List<HumanoidCover>();
            for(int slot=0;slot<settings.spawnPositionCount;slot++)
            {
                var p=Station(center,slot,settings,floorY);
                int kind=slot%5;
                if(kind==0 || kind==4)continue;
                double x=p.X+(kind==3?.45:0),z=p.Z-.85;
                if(kind==1)
                {
                    // The open space below a desk must also be open to authoritative hitscan.
                    covers.Add(new HumanoidCover("reading-desk-"+slot,new Vector3d(x,floorY+.755,z),new Vector3d(.62,.025,.34)));
                    foreach(int side in new[]{-1,1})
                        covers.Add(new HumanoidCover("desk-leg-"+slot+"-"+side,new Vector3d(x+side*.55,floorY+.365,z),new Vector3d(.035,.365,.28)));
                    covers.Add(new HumanoidCover("desk-panel-"+slot,new Vector3d(x,floorY+.52,z+.27),new Vector3d(.52,.21,.025)));
                }
                else
                {
                    double height=kind==2?1.12:1.0;
                    covers.Add(new HumanoidCover("low-bookcase-"+slot,new Vector3d(x,floorY+height*.5,z),new Vector3d(.52,height*.5,.27)));
                }
            }
            return covers;
        }
        public static Vector3d Position(TargetEntity target,Vector3d center,bool moving,double activeSeconds,DevelopmentGameSettings settings,
            double floorY=HumanoidGeometry.DefaultFloorY,int slot=-1)
        {
            int ordinal=Ordinal(target);
            if(slot<0)slot=moving?Route(ordinal,settings).Slot:ordinal%settings.spawnPositionCount;
            double age=Math.Max(0,activeSeconds-target.SpawnedAt.Ticks/(double)TimeSpan.TicksPerSecond);
            return Sample(center,slot,moving,ordinal,age+(moving?InitialRouteAge(ordinal,settings):0),settings,floorY);
        }
        public static Vector3d Sample(Vector3d center,int slot,bool moving,int ordinal,double age,DevelopmentGameSettings settings,double floorY)
        {
            var home=Station(center,slot,settings,floorY);
            if(!moving)
            {
                var pose=FixedPose(slot,ordinal,settings);
                return home+new Vector3d(pose.OffsetX,
                    (HumanoidGeometry.CrouchedCenterAt(floorY)-home.Y)*pose.Crouch,pose.OffsetZ);
            }
            var route=Route(ordinal,settings);
            double side=slot%10<5?-1:1;
            if(age<route.EntrySeconds)
            {
                var entry=new Vector3d(center.X+side*settings.libraryEntryXM,home.Y,home.Z);
                return entry+(home-entry)*Ease(age/route.EntrySeconds);
            }
            double elapsed=age-route.EntrySeconds;
            int cycle=(int)(elapsed/route.CycleSeconds);
            double phase=(elapsed%route.CycleSeconds)/route.CycleSeconds;
            // Change the reach and direction each cycle without frame-dependent random
            // choices. Every cycle starts/ends at home at rest, so there are no teleports.
            uint variation=unchecked((uint)(ordinal%settings.pathSequenceLength)*747796405u+(uint)cycle*2891336453u);
            variation^=variation>>16;
            double direction=(variation&1)==0?-1:1;
            double reach=settings.libraryPatrolDistanceM*(.8+(variation%21)*.01);
            // Keep hurried lateral/diagonal bursts in the open aisle, clear of the
            // next row's furniture even with the smallest configured row spacing.
            double depth=Math.Min(settings.libraryPatrolDepthM,settings.libraryRowSpacingM-1.5);
            int rowStart=slot/10*10;
            double left=Station(center,rowStart,settings,floorY).X;
            double right=Station(center,Math.Min(rowStart+9,settings.spawnPositionCount-1),settings,floorY).X;
            Vector3d Point(double x,double z)=>new Vector3d(Math.Max(left,Math.Min(right,home.X+x)),home.Y,home.Z+z);
            var a=Point(direction*reach,depth*.25);
            var b=Point(-direction*reach,depth*(route.Behaviour==1?.55:.8));
            var c=Point(direction*reach*.55,depth);
            // Short freezes between bursts read as hesitation; no variant stays idle
            // indefinitely. The brief pauses differ by route, as do the crouch windows.
            double pauseEnd=route.Behaviour==1?.28:.23;
            Vector3d position;
            if(phase<.18)position=home+(a-home)*Ease(phase/.18);
            else if(phase<pauseEnd)position=a;
            else if(phase<.46)position=a+(b-a)*Ease((phase-pauseEnd)/(.46-pauseEnd));
            else if(phase<.51)position=b;
            else if(phase<.73)position=b+(c-b)*Ease((phase-.51)/.22);
            else if(phase<.79)position=c;
            else position=c+(home-c)*Ease((phase-.79)/.21);
            double duckStart=route.Behaviour==3?.18:.46;
            double duckEnd=route.Behaviour==3?.79:.60;
            duckEnd=Math.Min(duckEnd,1-settings.crouchSeconds/route.CycleSeconds);
            double duck=route.Behaviour<2?0:Ease((phase-duckStart)*route.CycleSeconds/settings.crouchSeconds)
                *(1-Ease((phase-duckEnd)*route.CycleSeconds/settings.crouchSeconds));
            return position+new Vector3d(0,(HumanoidGeometry.CrouchedCenterAt(floorY)-home.Y)*duck,0);
        }
        private static double Ease(double value)
        {
            double t=Math.Max(0,Math.Min(1,value));
            return t*t*t*(t*(t*6-15)+10);
        }
    }
}
