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
        // Scale the whole floor plan together so furniture and paths cannot drift apart.
        public float libraryLayoutScale=1, libraryMoveSpeedMps=3.2f, libraryPauseSeconds=.18f;
        public float crouchSeconds=.45f;
        // Fixed-condition offsets are in virtual metres; yaw is relative to facing the participant.
        public float fixedLateralJitterM=.03f, fixedDepthJitterM=.05f, fixedSidePeekOffsetM=.03f;
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
            if(!Finite(fixedLateralJitterM,0,.04f) || !Finite(fixedDepthJitterM,0,.08f) ||
                !Finite(fixedSidePeekOffsetM,0,.04f) || !Finite(fixedPeekCrouch,.35f,.75f) ||
                fixedYawDegrees==null || fixedYawDegrees.Length==0)
                throw new ArgumentException("Fixed target pose settings are outside their synthetic ranges.");
            foreach(float angle in fixedYawDegrees)
                if(!Finite(angle,-180,180))throw new ArgumentException("Fixed target yaw must be finite and within -180 to 180 degrees.");
            if(activeTargetCount<1 || activeTargetCount>30 || (spawnPositionCount<31 || spawnPositionCount>50) || activeTargetCount>=spawnPositionCount ||
                pathSequenceLength<100000 || pathSequenceLength>1000000 ||
                !Finite(libraryLayoutScale,.95f,1.15f) || !Finite(libraryMoveSpeedMps,1.5f,4.5f) ||
                !Finite(libraryPauseSeconds,.08f,.65f) || magazineCapacity<1 || magazineCapacity>100 ||
                !Finite(crouchSeconds,.4f,1.2f))
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
        public const string Version="library-routes-v3";
        public const string FixedPoseVersion="library-fixed-poses-v2";
        public static DevelopmentFixedPose FixedPose(int slot,int ordinal,DevelopmentGameSettings settings)
        {
            // Derive posture from actual nearby furniture, not a repeating slot pattern.
            var station=LibraryFloorPlan.Station(slot);
            LibraryFixture? nearest=null;double distance=double.PositiveInfinity;
            foreach(var fixture in LibraryFloorPlan.Fixtures)
            {
                if(fixture.Kind==LibraryFixtureKind.Chair || fixture.Kind==LibraryFixtureKind.Column)continue;
                double dx=Math.Max(0,Math.Abs(station.X-fixture.Center.X)-fixture.Size.X/2);
                double dz=Math.Max(0,Math.Abs(station.Z-fixture.Center.Z)-fixture.Size.Z/2);
                double separation=Math.Sqrt(dx*dx+dz*dz);
                if(separation<distance){distance=separation;nearest=fixture;}
            }
            bool nearby=nearest.HasValue && distance<1.35;
            var kind=nearby?nearest!.Value.Kind:LibraryFixtureKind.Column;
            float crouch=kind==LibraryFixtureKind.Table || kind==LibraryFixtureKind.Counter?1:
                kind==LibraryFixtureKind.LowShelf?settings.fixedPeekCrouch:kind==LibraryFixtureKind.Stack?.8f:0;
            bool stride=!nearby && ordinal%2==1;
            string name=kind==LibraryFixtureKind.Table || kind==LibraryFixtureKind.Counter?"Crouched":
                kind==LibraryFixtureKind.LowShelf?"PeekOver":kind==LibraryFixtureKind.Stack?"PeekSide":stride?"FrozenStride":"Standing";
            double lateral=((ordinal*7+slot)%5-2)*settings.fixedLateralJitterM/2;
            if(kind==LibraryFixtureKind.Stack)lateral=Math.Sign(station.X-nearest!.Value.Center.X)*settings.fixedSidePeekOffsetM;
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
            public readonly double EntrySeconds, CycleSeconds, SpeedMps, PauseSeconds;
            public LibraryRoute(int slot,int behaviour,double entrySeconds,double cycleSeconds,double speedMps,double pauseSeconds)
            { Slot=slot;Behaviour=behaviour;EntrySeconds=entrySeconds;CycleSeconds=cycleSeconds;SpeedMps=speedMps;PauseSeconds=pauseSeconds; }
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
            double speed=settings.libraryMoveSpeedMps*(.88+(variant%7)*.04);
            double pause=settings.libraryPauseSeconds*(1+(variant%4)*.35);
            return new LibraryRoute(slot,(int)(variant%4),
                PathDuration(LibraryFloorPlan.Entry(slot),false,settings.libraryLayoutScale,speed,pause),
                PathDuration(LibraryFloorPlan.Circuit(slot),true,settings.libraryLayoutScale,speed,pause),speed,pause);
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
            var local=LibraryFloorPlan.Station(slot)+new Vector3d(0,HumanoidGeometry.StandingCenterAt(0),0);
            return LibraryFloorPlan.World(local,center,settings,floorY);
        }
        public static IReadOnlyList<HumanoidCover> Covers(Vector3d center,DevelopmentGameSettings settings,double floorY=HumanoidGeometry.DefaultFloorY)
            =>LibraryFloorPlan.Covers(center,settings,floorY);
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
            if(age<route.EntrySeconds)
            {
                var entry=SamplePath(LibraryFloorPlan.Entry(slot),false,0,1,Math.Max(0,age),settings.libraryLayoutScale,route.SpeedMps,route.PauseSeconds);
                return LibraryFloorPlan.World(entry,center,settings,floorY)+new Vector3d(0,HumanoidGeometry.StandingCenterAt(0),0);
            }
            double elapsed=age-route.EntrySeconds;
            int cycle=(int)(elapsed/route.CycleSeconds);
            double phase=(elapsed%route.CycleSeconds)/route.CycleSeconds;
            // Reverse only at the same stationary endpoint after two circuits.
            // Turns/hesitations happen at authored clear waypoints, never through desks.
            int direction=((ordinal%settings.pathSequenceLength+cycle/2)&1)==0?1:-1;
            var local=SamplePath(LibraryFloorPlan.Circuit(slot),true,slot%5,direction,
                elapsed%route.CycleSeconds,settings.libraryLayoutScale,route.SpeedMps,route.PauseSeconds);
            var position=LibraryFloorPlan.World(local,center,settings,floorY)+new Vector3d(0,HumanoidGeometry.StandingCenterAt(0),0);
            double duckStart=route.Behaviour==3?.18:.46;
            double duckEnd=route.Behaviour==3?.79:.60;
            duckEnd=Math.Min(duckEnd,1-settings.crouchSeconds/route.CycleSeconds);
            double duck=route.Behaviour<2?0:Ease((phase-duckStart)*route.CycleSeconds/settings.crouchSeconds)
                *(1-Ease((phase-duckEnd)*route.CycleSeconds/settings.crouchSeconds));
            return position+new Vector3d(0,(HumanoidGeometry.CrouchedCenterAt(floorY)-home.Y)*duck,0);
        }

        private static double PathDuration(IReadOnlyList<Vector3d> points,bool closed,double scale,double speed,double pause)
        {
            double duration=0;int segments=closed?points.Count:points.Count-1;
            for(int i=0;i<segments;i++)duration+=(points[(i+1)%points.Count]-points[i]).Length*scale*1.875/speed+pause;
            return duration;
        }
        private static Vector3d SamplePath(IReadOnlyList<Vector3d> points,bool closed,int start,int direction,
            double seconds,double scale,double speed,double pause)
        {
            int segments=closed?points.Count:points.Count-1;
            for(int leg=0;leg<segments;leg++)
            {
                int from=(start+direction*leg+points.Count*2)%points.Count;
                int to=(from+direction+points.Count)%points.Count;
                // Quintic easing peaks at 1.875 times its average speed. Compensate
                // here so the configured speed is an actual maximum, in metres/sec.
                double movingSeconds=(points[to]-points[from]).Length*scale*1.875/speed;
                if(seconds<=movingSeconds)return points[from]+(points[to]-points[from])*Ease(seconds/Math.Max(.0001,movingSeconds));
                seconds-=movingSeconds;
                if(seconds<=pause)return points[to];
                seconds-=pause;
            }
            return closed?points[start]:points[points.Count-1];
        }
        private static double Ease(double value)
        {
            double t=Math.Max(0,Math.Min(1,value));
            return t*t*t*(t*(t*6-15)+10);
        }
    }
}
