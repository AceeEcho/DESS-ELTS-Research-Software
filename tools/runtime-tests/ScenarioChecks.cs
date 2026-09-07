#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;

static class ScenarioChecks
{
    static int passed;
    static void True(bool yes,string name){if(!yes)throw new Exception("FAIL: "+name);passed++;}
    static void Throws<T>(Action a,string name)where T:Exception{try{a();throw new Exception("FAIL: "+name);}catch(T){passed++;}}
    static void Main()
    {
        var c=new ManualSharedClock(DateTimeOffset.UnixEpoch); var now=c.Now;
        var volume=new SpawnVolume(new Vector3d(-1,-1,4),new Vector3d(1,1,6));
        var definition=new ScenarioDefinition("synthetic-fixed",.1,volume,2,5,0,"Fixed","Hitscan","TargetsDestroyedCount");
        var rule=new MaintainCountSpawnRule(44); var spawned=new List<TargetEntity>(rule.Spawn(definition,now,Vector3d.Zero,_=>true));
        True(spawned.Count==2 && spawned[0].State==TargetState.Active && volume.Contains(spawned[0].Position),"maintain count spawns visible world targets");
        var fixedMove=new FixedMovement(); var original=spawned[0].Position; fixedMove.Step(spawned[0],1,volume); True(spawned[0].Position.Equals(original),"fixed movement preserves world position");
        var randomTarget=new TargetEntity("random",Vector3d.Zero,Vector3d.Zero,.1,now);randomTarget.Activate(); new RandomWalkMovement(9,1,1,.1).Step(randomTarget,.2,new SpawnVolume(new Vector3d(-2,-2,-2),new Vector3d(2,2,2)));True(randomTarget.Position.Length>0,"seeded random walk changes world position");
        var target=new TargetEntity("shot-target",new Vector3d(0,0,5),Vector3d.Zero,1,now);target.Activate(); var model=new HitscanShotModel(); var events=model.Fire(new ShotContext(now,new Ray3d(Vector3d.Zero,new Vector3d(0,0,1)),true),"block-1",new[]{target},_=>true);True(target.State==TargetState.Destroyed && events.Count==3,"valid trigger edge hits nearest active target");
        var invalid=model.Fire(new ShotContext(now,new Ray3d(Vector3d.Zero,new Vector3d(0,0,1)),false),"block-1",Array.Empty<TargetEntity>(),_=>true);True(invalid.Count==0,"invalid tracking contributes no shot events");
        var scorer=new TargetsDestroyedCount();c.Advance(TimeSpan.FromSeconds(300));var end=c.Now;True(scorer.Score("block-1",now,end,events)==1,"primary score derives from TargetDestroyed event payload");
        var duplicate=new List<SessionEvent>(events){events[2]};Throws<ArgumentException>(()=>scorer.Score("block-1",now,end,duplicate),"duplicate target destruction is rejected");
        var boundary=new SessionEvent(99,end,"TargetDestroyed",new[]{LogField.String("blockId","block-1"),LogField.String("targetId","at-end")});True(scorer.Score("block-1",now,end,new[]{boundary})==0,"block end is excluded by half-open interval");
        var plan=new SessionPlan("p-01",new[]{"WE_MT","NE_MT","WE_FT","NE_FT"});True(plan.ConditionDurationSeconds==300,"session preserves fixed duration");Throws<ArgumentException>(()=>new SessionPlan("p-01",new[]{"WE_MT","WE_MT","NE_FT","NE_MT"}),"invalid order rejected");Throws<ArgumentException>(()=>new SessionPlan("p-01",new[]{"WE_MT","NE_MT","WE_FT","NE_FT"},299),"duration override must be explicit");
        Console.WriteLine("PASS: "+passed+" scenario checks");
    }
}

