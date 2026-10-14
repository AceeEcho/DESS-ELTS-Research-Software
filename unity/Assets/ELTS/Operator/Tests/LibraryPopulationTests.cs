using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Elts.Geometry;
using NUnit.Framework;
using UnityEngine;

namespace Elts.Operator.Tests
{
    public sealed class LibraryPopulationTests
    {
        private static readonly Vector3d Center=new Vector3d(0,0,9);
        private const double Floor=-1.5567;

        [Test]
        public void FixedTargetsStayUntilKilledAndNeverReplaceInTheJustVacatedPosition()
        {
            var settings=new DevelopmentGameSettings();
            var population=new LibraryTargetPopulation(settings,false,.1);
            population.Reconcile(0,Center,Floor);
            var originals=population.ActiveTargets.ToDictionary(t=>t.Id,t=>t.Position);
            population.Reconcile(120,Center,Floor);
            Assert.That(population.History.Count,Is.EqualTo(30),"No ten-second expiry");
            foreach(var target in population.ActiveTargets)Assert.That(target.Position,Is.EqualTo(originals[target.Id]));
            var visited=new System.Collections.Generic.HashSet<int>(population.ActiveTargets.Select(population.Slot));
            for(int kill=0;kill<1000;kill++)
            {
                var victim=population.ActiveTargets[(kill*13)%30];
                int oldSlot=population.Slot(victim);
                victim.Destroy();population.Reconcile(121+kill,Center,Floor);
                var replacement=population.ActiveTargets.Last();
                Assert.That(population.ActiveTargets.Count,Is.EqualTo(30));
                Assert.That(population.Slot(replacement),Is.Not.EqualTo(oldSlot));
                Assert.That(population.ActiveTargets.Select(population.Slot).Distinct().Count(),Is.EqualTo(30));
                visited.Add(population.Slot(replacement));
            }
            Assert.That(visited.Count,Is.EqualTo(50));
        }

        [Test]
        public void DifferentKillOrdersAndFrameRatesKeepTheSameRoutePrefix()
        {
            var settings=new DevelopmentGameSettings();
            var first=new LibraryTargetPopulation(settings,true,.1);
            var second=new LibraryTargetPopulation(settings,true,.1);
            first.Reconcile(0,Center,Floor);second.Reconcile(0,Center,Floor);
            for(int i=0;i<70;i++)
            {
                if(i<50)
                {
                    first.ActiveTargets[(i*7)%30].Destroy();
                    first.Reconcile(i+1,Center,Floor);
                }
                second.ActiveTargets[(i*11+3)%30].Destroy();
                second.Reconcile(i*2+.5,Center,Floor);
                Assert.That(second.ActiveTargets.Count,Is.EqualTo(30));
            }
            Assert.That(first.History.Count,Is.EqualTo(80));Assert.That(second.History.Count,Is.EqualTo(100));
            foreach(var target in first.History)
            {
                var other=second.History.Single(t=>t.Id==target.Id);
                Assert.That(first.Slot(target),Is.EqualTo(second.Slot(other)));
                for(double age=0;age<25;age+=.37)
                {
                    int ordinal=DevelopmentScenarioLayout.Ordinal(target);
                    Assert.That(DevelopmentScenarioLayout.Sample(Center,first.Slot(target),true,ordinal,age,settings,Floor),
                        Is.EqualTo(DevelopmentScenarioLayout.Sample(Center,second.Slot(other),true,ordinal,age,settings,Floor)));
                }
            }
            var lowRate=new LibraryTargetPopulation(settings,true,.1);
            var highRate=new LibraryTargetPopulation(settings,true,.1);
            lowRate.Reconcile(0,Center,Floor);highRate.Reconcile(0,Center,Floor);
            for(int frame=1;frame<=60*12;frame++)highRate.Reconcile(frame/60.0,Center,Floor);
            for(int frame=1;frame<=15*12;frame++)lowRate.Reconcile(frame/15.0,Center,Floor);
            Assert.That(lowRate.ActiveTargets.Select(t=>t.Position),Is.EqualTo(highRate.ActiveTargets.Select(t=>t.Position)));
            var paused=highRate.ActiveTargets.Select(t=>t.Position).ToArray();
            highRate.Reconcile(12,Center,Floor);
            Assert.That(highRate.ActiveTargets.Select(t=>t.Position),Is.EqualTo(paused));
        }

        [Test]
        public void OccupiedMovingDestinationsWaitOutsideAndRetainTheirAssignedRoute()
        {
            var settings=new DevelopmentGameSettings();
            var population=new LibraryTargetPopulation(settings,true,.1);
            population.Reconcile(0,Center,Floor);
            // Keep target-1 alive until another indexed route requests its station.
            var survivor=population.ActiveTargets[0];
            for(int i=0;i<100;i++)
            {
                var next=population.ActiveTargets.First(t=>t!=survivor && population.StartedAt(t).HasValue);
                next.Destroy();population.Reconcile(i+1,Center,Floor);
                var waiter=population.ActiveTargets.FirstOrDefault(t=>t!=survivor && population.Slot(t)==population.Slot(survivor));
                if(waiter==null)continue;
                Assert.That(population.StartedAt(waiter),Is.Null);
                Assert.That(Math.Abs(waiter.Position.X-Center.X),Is.EqualTo(settings.libraryEntryXM));
                int slot=population.Slot(waiter);
                survivor.Destroy();population.Reconcile(i+1.5,Center,Floor);
                Assert.That(population.StartedAt(waiter),Is.EqualTo(i+1.5));
                population.Reconcile(i+10,Center,Floor);
                Assert.That(population.Slot(waiter),Is.EqualTo(slot));
                Assert.That(population.ActiveTargets.Count,Is.EqualTo(30));
                return;
            }
            Assert.Fail("The long route sequence must revisit the preserved station.");
        }

        [Test]
        public void DeskOpeningAllowsLowShotsButItsSurfaceAndPanelBlockThem()
        {
            var settings=new DevelopmentGameSettings();
            var station=DevelopmentScenarioLayout.Station(Center,1,settings,Floor);
            var desk=DevelopmentScenarioLayout.Covers(Center,settings,Floor)
                .Where(c=>c.Id=="reading-desk-1" || c.Id=="desk-panel-1" || c.Id.StartsWith("desk-leg-1-")).ToArray();
            foreach(var sample in new[]{(.15,"Miss"),(.5,"Cover"),(.755,"Cover"),(1.0,"Miss")})
            {
                var model=new Elts.Scenario.HumanoidShotModel(new Elts.Scenario.SessionEventSequence(),TimeSpan.Zero,desk,Floor);
                var ray=new Ray3d(new Vector3d(station.X,Floor+sample.Item1,station.Z-2),new Vector3d(0,0,1));
                model.Fire(new Elts.Scenario.ShotContext(Elts.Clock.MonotonicTimestamp.Zero,ray,true),"desk-check",
                    Array.Empty<Elts.Scenario.TargetEntity>(),_=>true);
                Assert.That(model.Outcome,Is.EqualTo(sample.Item2),"Shot height above floor: "+sample.Item1);
            }
        }

        [Test]
        public void RoutesClearFurnitureAndExportTheFullOrderedList()
        {
            var settings=new DevelopmentGameSettings();
            var covers=DevelopmentScenarioLayout.Covers(Center,settings,Floor);
            var stations=Enumerable.Range(0,50).Select(i=>DevelopmentScenarioLayout.Station(Center,i,settings,Floor)).ToArray();
            Assert.That(stations.Distinct().Count(),Is.EqualTo(50));
            Assert.That(covers.Count,Is.EqualTo(60));
            for(int ordinal=0;ordinal<250;ordinal++)
            {
                var route=DevelopmentScenarioLayout.Route(ordinal,settings);
                for(double age=0;age<25;age+=.1)
                {
                    var point=DevelopmentScenarioLayout.Sample(Center,route.Slot,true,ordinal,age,settings,Floor);
                    foreach(var cover in covers)
                        Assert.That(Math.Abs(point.X-cover.Center.X)<cover.HalfSize.X+.38 &&
                            Math.Abs(point.Z-cover.Center.Z)<cover.HalfSize.Z+.2,Is.False,"Route crosses furniture");
                }
            }
            string folder=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/library"));
            Directory.CreateDirectory(folder);
            using(var writer=new StreamWriter(Path.Combine(folder,"library-routes-v1.csv")))
            {
                writer.WriteLine("route_index,position_index,behaviour,entry_seconds,cycle_seconds,x_m,z_m");
                for(int i=0;i<settings.pathSequenceLength;i++)
                {
                    var route=DevelopmentScenarioLayout.Route(i,settings);
                    var station=DevelopmentScenarioLayout.Station(Center,route.Slot,settings,Floor);
                    writer.WriteLine(string.Format(CultureInfo.InvariantCulture,"{0},{1},{2},{3:F3},{4:F3},{5:F3},{6:F3}",
                        i,route.Slot,route.Name,route.EntrySeconds,route.CycleSeconds,station.X,station.Z));
                }
            }
        }
    }
}
