#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Scenario;

namespace Elts.Operator
{
    internal enum LibraryFixtureKind { Stack, LowShelf, Table, Chair, Counter, Lounge, Column }

    /// <summary>One architectural fixture, measured in virtual metres from the library
    /// origin/floor. Rendering and shot occlusion both consume this same description.</summary>
    internal readonly struct LibraryFixture
    {
        public readonly string Id;
        public readonly LibraryFixtureKind Kind;
        public readonly Vector3d Center, Size;
        public readonly int Facing;
        public LibraryFixture(string id, LibraryFixtureKind kind, double x, double z,
            double width, double height, double depth, int facing=0)
        { Id=id;Kind=kind;Center=new Vector3d(x,height/2,z);Size=new Vector3d(width,height,depth);Facing=facing; }
    }

    /// <summary>An authored floor plan, not a target grid. Routes use the cross aisles,
    /// stack aisles and reading areas. Coordinates and doorway connections are shared
    /// by every participant; no navigation decisions depend on who they shoot.</summary>
    internal static class LibraryFloorPlan
    {
        public const string Version="library-floorplan-v3";
        public const double HalfWidth=11, Back=28, DoorWidth=1.8;
        public static readonly double[] LeftDoors={4.8,14.4,24.7};
        public static readonly double[] RightDoors={2,12.3,23.8};
        public static readonly IReadOnlyList<LibraryFixture> Fixtures=BuildFixtures();

        private static Vector3d P(double x,double z)=>new Vector3d(x,0,z);
        // Five stations per circulation loop. Deliberately unequal shapes and spacing
        // follow the furnishings, rather than repeating rows of isolated target props.
        private static readonly Vector3d[][] circuits={
            new[]{P(-1,0),P(1.1,1.2),P(1.2,3.8),P(-1.4,3.9),P(-1.6,1)},
            new[]{P(-6.8,5),P(-4.7,5),P(-2.7,5),P(-3,6),P(-6.7,5.85)},
            new[]{P(-5.5,7.2),P(-5.05,8.8),P(-5.05,11.8),P(-5.95,11.8),P(-5.95,9)},
            new[]{P(-2.9,7.3),P(-2.1,8.4),P(-2.1,11.6),P(-3,11.6),P(-3,9.2)},
            new[]{P(-7.4,13.6),P(-3.8,13.2),P(.1,13.8),P(-.2,15.4),P(-7.4,15.1)},
            new[]{P(-5.5,18.1),P(-5.05,19.5),P(-5.05,22.6),P(-5.95,22.6),P(-5.95,20.3)},
            new[]{P(-.15,17.4),P(1.3,18.3),P(1.3,23.5),P(-.15,23.5),P(-.15,20.4)},
            new[]{P(1.9,4.7),P(5.65,4.7),P(5.65,6.5),P(5.65,8.3),P(1.9,8.3)},
            new[]{P(1.8,12.8),P(5.4,12.8),P(5.4,14.8),P(5.4,16.7),P(1.8,16.7)},
            new[]{P(9.1,17),P(9.3,19),P(9.1,21.4),P(5.1,21.4),P(5.1,17.9)}
        };
        // Approach from real side-door openings, then through a clear cross aisle.
        // Waiting actors remain outside the room rather than in an occupied station.
        private static readonly Vector3d[][] approaches={
            new[]{P(12,2),P(9.7,2),P(1.1,2),P(1.1,1.2)},
            new[]{P(-12,4.8),P(-9.5,4.8),P(-6.8,5)},
            new[]{P(-12,14.4),P(-5.5,14.4),P(-5.5,11.8),P(-5.5,7.2)},
            new[]{P(-12,14.4),P(-2.9,14.4),P(-2.9,7.3)},
            new[]{P(-12,14.4),P(-7.4,14.4),P(-7.4,13.6)},
            new[]{P(-12,14.4),P(-5.5,14.4),P(-5.5,18.1)},
            new[]{P(-12,24.7),P(-.15,24.7),P(-.15,17.4)},
            new[]{P(12,2),P(5.65,2),P(5.65,4.7),P(1.9,4.7)},
            new[]{P(12,12.3),P(5.4,12.3),P(1.8,12.3),P(1.8,12.8)},
            new[]{P(12,23.8),P(9.3,23.8),P(9.3,17),P(9.1,17)}
        };
        private static readonly Vector3d[][] entries=BuildEntries();

        public static Vector3d Station(int slot)=>circuits[slot/5][slot%5];
        public static string Zone(int slot)=>new[]{"Entrance and service","Front cross aisle","Literature stacks",
            "Browsing aisle","Main cross aisle","Reference stacks","Rear circulation","Window reading",
            "Shared study","Quiet reading"}[slot/5];
        public static IReadOnlyList<Vector3d> Circuit(int slot)=>circuits[slot/5];
        public static IReadOnlyList<Vector3d> Entry(int slot)=>entries[slot];
        public static Vector3d World(Vector3d local,Vector3d origin,DevelopmentGameSettings settings,double floorY)
            =>new Vector3d(origin.X+local.X*settings.libraryLayoutScale,floorY+local.Y,origin.Z+local.Z*settings.libraryLayoutScale);

        private static Vector3d[][] BuildEntries()
        {
            var result=new Vector3d[50][];
            for(int slot=0;slot<50;slot++)
            {
                var path=new List<Vector3d>(approaches[slot/5]);
                var circuit=circuits[slot/5];
                // Group zero approaches vertex one; continue around to vertex zero.
                if(slot/5==0){path.Add(circuit[0]);}
                for(int vertex=0;vertex<=slot%5;vertex++)
                    if((path[path.Count-1]-circuit[vertex]).Length>1e-8)path.Add(circuit[vertex]);
                result[slot]=path.ToArray();
            }
            return result;
        }

        private static IReadOnlyList<LibraryFixture> BuildFixtures()
        {
            var items=new List<LibraryFixture>();
            void Add(string id,LibraryFixtureKind kind,double x,double z,double w,double h,double d,int facing=0)
                =>items.Add(new LibraryFixture(id,kind,x,z,w,h,d,facing));
            void Table(string id,double x,double z)
            {
                Add(id,LibraryFixtureKind.Table,x,z,2.2,.76,1.1);
                foreach(int side in new[]{-1,1})foreach(int seat in new[]{-1,1})
                    Add(id+"-chair-"+side+"-"+seat,LibraryFixtureKind.Chair,x+seat*.55,z+side*.95,.43,.92,.43,side<0?180:0);
            }
            Add("circulation-desk",LibraryFixtureKind.Counter,-3.9,1.8,3.4,1.02,1.15);
            Add("desk-return",LibraryFixtureKind.Counter,-5.3,3,.6,1.02,2.0);
            Add("new-books",LibraryFixtureKind.LowShelf,-.6,5.6,1.1,1.12,1.4);
            Add("literature-west",LibraryFixtureKind.Stack,-6.8,9.5,.76,2.12,6);
            Add("literature-east",LibraryFixtureKind.Stack,-4.2,9.5,.76,2.12,6);
            Add("reference-west",LibraryFixtureKind.Stack,-6.8,20,.76,2.12,6);
            Add("reference-east",LibraryFixtureKind.Stack,-4.2,20,.76,2.12,6);
            Add("history",LibraryFixtureKind.Stack,-1.6,20.5,.76,2.12,6);
            Table("reading-table-1",3.8,6.5);
            Table("reading-table-2",7.3,9.5);
            Table("reading-table-3",3.7,14.7);
            Table("reading-table-4",7,19.4);
            Table("reading-table-5",3.5,22.5);
            Add("window-lounge",LibraryFixtureKind.Lounge,7.7,4,2,.85,1.0);
            Add("periodicals",LibraryFixtureKind.LowShelf,8.9,6.5,2.2,1.12,.45);
            foreach(double x in new[]{-8,-5,-2,2,5,8})
                Add("rear-collection-"+x,LibraryFixtureKind.Stack,x,27.4,2.3,2.3,.45);
            Add("west-column",LibraryFixtureKind.Column,-9,10,.45,3.8,.45);
            Add("east-column",LibraryFixtureKind.Column,9.8,15,.45,3.8,.45);
            return items;
        }

        public static IReadOnlyList<HumanoidCover> Covers(Vector3d origin,DevelopmentGameSettings settings,double floorY)
        {
            var result=new List<HumanoidCover>();
            double scale=settings.libraryLayoutScale;
            void Box(string id,Vector3d at,Vector3d size)=>result.Add(new HumanoidCover(id,
                World(at,origin,settings,floorY),new Vector3d(size.X*scale/2,size.Y/2,size.Z*scale/2)));
            foreach(var item in Fixtures)
            {
                var p=item.Center;var s=item.Size;
                if(item.Kind==LibraryFixtureKind.Table)
                {
                    Box(item.Id+"-top",new Vector3d(p.X,.7375,p.Z),new Vector3d(s.X,.045,s.Z));
                    foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                        Box(item.Id+"-leg-"+x+"-"+z,new Vector3d(p.X+x*(s.X/2-.12),.3575,p.Z+z*(s.Z/2-.12)),new Vector3d(.055,.715,.055));
                }
                else if(item.Kind==LibraryFixtureKind.Chair)
                {
                    Box(item.Id+"-seat",new Vector3d(p.X,.45,p.Z),new Vector3d(.43,.07,.43));
                    Box(item.Id+"-back",new Vector3d(p.X,.70,p.Z+(item.Facing==180?-.19:.19)),new Vector3d(.43,.43,.065));
                    foreach(int x in new[]{-1,1})foreach(int z in new[]{-1,1})
                        Box(item.Id+"-leg-"+x+"-"+z,new Vector3d(p.X+x*.18,.21,p.Z+z*.16),new Vector3d(.028,.42,.028));
                }
                else Box(item.Id,p,s);
            }
            // Walls are solid to hitscan; door openings match the rendered side walls.
            foreach(int side in new[]{-1,1})
            {
                double previous=-10.5;int segment=0;
                foreach(double door in side<0?LeftDoors:RightDoors)
                {
                    double end=door-DoorWidth/2;
                    Box("wall-"+side+"-"+segment++,new Vector3d(side*HalfWidth,1.9,(previous+end)/2),new Vector3d(.2,3.8,end-previous));
                    Box("lintel-"+side+"-"+segment,new Vector3d(side*HalfWidth,3,(door)),new Vector3d(.2,1.6,DoorWidth));
                    previous=door+DoorWidth/2;
                }
                Box("wall-"+side+"-"+segment,new Vector3d(side*HalfWidth,1.9,(previous+Back)/2),new Vector3d(.2,3.8,Back-previous));
            }
            Box("rear-wall",new Vector3d(0,1.9,Back),new Vector3d(HalfWidth*2,3.8,.2));
            return result;
        }
    }
}
