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
        public void ApplyPopulation(ScenarioConfiguration scenario)
        {
            activeTargetCount=scenario.ActiveTargetCount;
            spawnPositionCount=scenario.SpawnPositionCount;
            pathSequenceLength=scenario.PathSequenceLength;
        }
        public DevelopmentGameSettings Copy()=> (DevelopmentGameSettings)MemberwiseClone();
        public void Validate()
        {
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

    /// <summary>Library geometry and versioned, indexed choreography in virtual metres.</summary>
    internal static class DevelopmentScenarioLayout
    {
        public const int TargetCount=30;
        public const string Version="library-routes-v2";
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
            if(!moving)return home;
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
