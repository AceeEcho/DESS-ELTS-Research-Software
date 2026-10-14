#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;
using Elts.Scenario;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Elts.ViewTests")]

namespace Elts.Operator
{
    /// <summary>Kill-driven library population. Route numbers belong to spawn order,
    /// never participant identity, frame timing, or the order of targets in a dictionary.</summary>
    internal sealed class LibraryTargetPopulation
    {
        private readonly List<TargetEntity> history = new List<TargetEntity>();
        private readonly List<TargetEntity> active = new List<TargetEntity>();
        private readonly Dictionary<string,int> slots = new Dictionary<string,int>();
        private readonly Dictionary<string,double> starts = new Dictionary<string,double>();
        private readonly DevelopmentGameSettings settings;
        private readonly bool moving;
        private readonly double radius;
        private int nextFixedSlot;
        private double lastActiveSeconds;
        public IReadOnlyList<TargetEntity> History => history;
        public IReadOnlyList<TargetEntity> ActiveTargets => active;
        public int Slot(TargetEntity target) => slots[target.Id];
        public double? StartedAt(TargetEntity target) => starts.TryGetValue(target.Id,out var time)?time:(double?)null;

        public LibraryTargetPopulation(DevelopmentGameSettings settings,bool moving,double radius)
        { this.settings=settings;this.moving=moving;this.radius=radius; }

        public void Reconcile(double activeSeconds,Vector3d center,double floorY)
        {
            if(double.IsNaN(activeSeconds) || double.IsInfinity(activeSeconds) || activeSeconds<lastActiveSeconds)
                throw new ArgumentOutOfRangeException(nameof(activeSeconds));
            lastActiveSeconds=activeSeconds;
            var justVacated=new HashSet<int>();
            for(int i=active.Count-1;i>=0;i--)
            {
                var target=active[i];
                if(target.State==TargetState.Active)continue;
                justVacated.Add(slots[target.Id]);
                active.RemoveAt(i);starts.Remove(target.Id);
            }
            var occupied=new HashSet<int>();
            foreach(var target in active)
                if(!moving || starts.ContainsKey(target.Id))occupied.Add(slots[target.Id]);

            while(active.Count<settings.activeTargetCount)
            {
                int ordinal=history.Count;
                int slot;
                if(moving)slot=DevelopmentScenarioLayout.Route(ordinal,settings).Slot;
                else
                {
                    slot=-1;
                    for(int attempt=0;attempt<settings.spawnPositionCount;attempt++)
                    {
                        int candidate=nextFixedSlot++%settings.spawnPositionCount;
                        if(!occupied.Contains(candidate) && !justVacated.Contains(candidate)){slot=candidate;break;}
                    }
                    if(slot<0)throw new InvalidOperationException("No unused library position is available.");
                    occupied.Add(slot);
                }
                var target=new TargetEntity("target-"+(ordinal+1),Vector3d.Zero,Vector3d.Zero,radius,
                    new MonotonicTimestamp(checked((long)(activeSeconds*TimeSpan.TicksPerSecond))));
                slots.Add(target.Id,slot);target.Activate();history.Add(target);active.Add(target);
                if(!moving)starts.Add(target.Id,activeSeconds);
            }
            // Earlier route assignments get first access to their destination.
            // Waiting actors stay outside the view; no route is skipped or relocated.
            foreach(var target in active)
            {
                int ordinal=DevelopmentScenarioLayout.Ordinal(target);
                int slot=slots[target.Id];
                if(moving && !starts.ContainsKey(target.Id) && !occupied.Contains(slot))
                { occupied.Add(slot);starts.Add(target.Id,activeSeconds); }
                double age=starts.TryGetValue(target.Id,out double start)?Math.Max(0,activeSeconds-start):0;
                var next=DevelopmentScenarioLayout.Sample(center,slot,moving,ordinal,age,settings,floorY);
                // Velocity is supplied by the session using its active-time delta.
                target.Move(next,Vector3d.Zero);
            }
        }
    }
}
