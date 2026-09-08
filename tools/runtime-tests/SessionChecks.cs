#nullable enable
using System;
using System.Collections.Generic;
using Elts.Clock;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

static class SessionChecks
{
    static int passed;
    static void True(bool value,string name){if(!value)throw new Exception("FAIL: "+name);passed++;}
    static void Main()
    {
        var clock=new ManualSharedClock(DateTimeOffset.UnixEpoch); var events=new List<SessionEvent>(); var sink=new Sink(events); var link=new Link();
        var plan=new SessionPlan("p1",new[]{"WE_MT","NE_MT","WE_FT","NE_FT"}); var engine=new SessionEngine(clock,new SessionEventSequence(),plan,new SessionTiming(1,1,true),sink,link);
        var bad=engine.StartSetup(new SessionReadinessInput()); True(!bad.IsReady&&!bad.StudyReady&&bad.Failures.Count==7,"specific physical readiness failures never enable study");
        var ready=new SessionReadinessInput{TwoDisplays=true,SteamVrRunning=true,TrackersBoundAndValidForTwoSeconds=true,ConfigurationHashesComputed=true,DiskFreeBytes=2L*1024*1024*1024,LogWriterAlive=true,WeLinkReachable=true};
        True(engine.StartSetup(ready).IsReady&&engine.State==SessionState.SessionSetup,"setup begins after synthetic readiness"); engine.Advance(); engine.Advance(); clock.Advance(TimeSpan.FromSeconds(1)); engine.Tick(); True(engine.State==SessionState.BlockReady,"practice timer advances to first block");
        engine.StartBlock(); True(engine.State==SessionState.BlockRunning&&engine.TargetsActive&&link.Enabled,"WE block starts link"); clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick(); True(engine.State==SessionState.BlockEnded&&!link.Enabled,"exact block deadline ends link"); engine.Advance(); clock.Advance(TimeSpan.FromSeconds(1)); engine.Advance(); True(engine.CurrentBlockIndex==1&&engine.State==SessionState.BlockReady,"break advances supplied order");
        engine.StartBlock(); engine.Abort("operator-stop"); True(engine.State==SessionState.Aborted,"abort works from running state"); engine.Abort("repeat"); True(engine.State==SessionState.Aborted,"abort is idempotent"); engine.RerunCurrentBlock(); True(engine.RerunIndex==1&&engine.State==SessionState.BlockReady,"rerun preserves prior attempt identity");
        engine.AddNote("synthetic note"); engine.EmitProvisionalSyncMarker("sync"); True(events.Exists(e=>e.EventType=="OperatorNote")&&events.Exists(e=>e.EventType=="SyncMarker"),"notes and provisional sync marker are events");
        var fail=new SessionEngine(clock,new SessionEventSequence(),plan,new SessionTiming(0,0,true),new Sink(new List<SessionEvent>(),false)); fail.StartSetup(ready); True(fail.State==SessionState.Failed,"event sink failure fails closed");
        True(Strict(events),"session transitions use one strict sequence"); Console.WriteLine("PASS: "+passed+" session checks");
    }
    static bool Strict(List<SessionEvent> events){for(int i=1;i<events.Count;i++)if(events[i].Sequence!=events[i-1].Sequence+1)return false;return true;}
    sealed class Sink:ISessionEventSink{readonly List<SessionEvent> events;readonly bool ok;public Sink(List<SessionEvent> events,bool ok=true){this.events=events;this.ok=ok;}public bool TryRecord(SessionEvent item){if(ok)events.Add(item);return ok;}}
    sealed class Link:ISessionLink{public bool Enabled;public bool TrySetEnabled(bool enabled){Enabled=enabled;return true;}}
}
