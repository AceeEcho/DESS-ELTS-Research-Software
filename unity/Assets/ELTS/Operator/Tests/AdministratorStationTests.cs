using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Geometry;
using Elts.Session;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class AdministratorStationTests
    {
        [Test]
        public void CanceledAdministratorCommandsCannotRunLater()
        {
            var pending=new AdministratorServer.PendingCommand(new JObject{["action"]="abort"});
            pending.CancelIfQueued();
            Assert.That(pending.TryBegin(),Is.False);
            Assert.That((bool)JObject.Parse(pending.Completion.Task.Result)["ok"],Is.False);
        }
        private sealed class Input : IDesktopControls
        {
            public DesktopControlFrame Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero);
            public DesktopControlFrame Read(IPanel panel)
            {var value=Frame;Frame=new DesktopControlFrame(value.Focused,value.Pointer,value.Movement);return value;}
        }
        [UnityTest]
        public IEnumerator PausedTestKeepsTargetsAndRecordingAndCanStopIndependently()
        {
            var host=new GameObject("Flexible administrator test fixture");
            var view=host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;
            var input=new Input();station.Controls=input;
            try
            {
                for(int i=0;i<5;i++)yield return null;
                station.ApplyCommand(new JObject{["action"]="setDuration",["condition"]="WE_FT",["seconds"]=20});
                station.ApplyCommand(new JObject{["action"]="setDuration",["condition"]="WE_MT",["seconds"]=40});
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="pause-test",
                    ["conditionOrder"]=new JArray("WE_FT","WE_MT","NE_FT","NE_MT")});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.BlockReady && Time.realtimeSinceStartup<limit)
                { ReviewFixtureWhenReady(session,station);clock.Advance(TimeSpan.FromSeconds(.1));yield return null; }
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                station.ApplyCommand(new JObject{["action"]="arm"});
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(20).Within(.001));
                string run=session.StationRecordingPath, blockId=session.DesktopBlockId;
                var scenario=(DevelopmentSessionScenario)typeof(DevelopmentSessionPanel).GetField("scenario",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                // Keep the simulated controller heartbeat alive during active time.
                for(int second=0;second<3;second++){clock.Advance(TimeSpan.FromSeconds(1));yield return null;}
                // A fixture can land exactly in a target respawn gap. Wait for an
                // actual target before asserting that pause preserves its identity.
                limit=Time.realtimeSinceStartup+5;
                while(scenario.Positions.Count==0 && Time.realtimeSinceStartup<limit)
                {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                double played=session.Engine.CurrentActiveSeconds;
                station.ApplyCommand(new JObject{["action"]="pause",["blockId"]=blockId});yield return null;
                var frozen=scenario.Positions.ToDictionary(pair=>pair.Key,pair=>pair.Value);
                Assert.That(frozen.Count,Is.GreaterThan(0));
                clock.Advance(TimeSpan.FromSeconds(600));
                for(int i=0;i<8;i++)yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockPaused));
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(20-played).Within(.001));
                Assert.That(scenario.Positions.Keys,Is.EquivalentTo(frozen.Keys));
                foreach(var target in frozen)Assert.That(scenario.Positions[target.Key],Is.EqualTo(target.Value));
                Assert.That(session.StationParticipantTrigger(),Is.False,"Paused clicks cannot score");
                Assert.That(session.HasOpenDesktopRecording,Is.True,"Pause keeps acquisition/recording open");
                Assert.That(station.ParticipantRoot.Q<Label>("participantTitle").text,Is.EqualTo("Test paused"));
                station.ApplyCommand(new JObject{["action"]="setDuration",["condition"]="WE_FT",["seconds"]=30});
                Assert.Throws<ArgumentException>(()=>station.ApplyCommand(new JObject{["action"]="setDuration",["condition"]="WE_FT",["seconds"]=2}));
                station.ApplyCommand(new JObject{["action"]="resume",["blockId"]=blockId});
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                Assert.That(session.StationShots,Is.Zero,"A click held across pause/resume is canceled");
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(30-played).Within(.001));
                Assert.That(scenario.Positions.Keys,Is.EquivalentTo(frozen.Keys),"Paused time cannot expire targets on resume");
                station.ApplyCommand(new JObject{["action"]="stopTest",["blockId"]=blockId});yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                Assert.That(session.HasOpenDesktopRecording,Is.True);
                var state=JObject.Parse(station.StateJson());
                Assert.That((string)state["blocks"][0]["status"],Is.EqualTo("Stopped early"));
                limit=Time.realtimeSinceStartup+15;
                while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(File.Exists(Path.Combine(run,"test-checkpoint-"+blockId+".json")),Is.True,"Each test saves before the participant session closes");
                Assert.That(File.Exists(Path.Combine(run,"session-summary.json")),Is.False,"Checkpoint is not a final session closure");
                clock.Advance(TimeSpan.FromSeconds(60));yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded),"Waiting does not start the break");
                Assert.That(session.StationCanArm,Is.False);
                station.ApplyCommand(new JObject{["action"]="startBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(5).Within(.001));
                Assert.Throws<InvalidOperationException>(()=>session.StationArm());
                clock.Advance(TimeSpan.FromSeconds(5));yield return null;
                Assert.That(session.TestArmed,Is.False,"Timer completion never arms a test");
                Assert.That(session.Engine.CurrentCondition,Is.EqualTo("WE_MT"));
                Assert.That(session.Engine.CurrentDurationSeconds,Is.EqualTo(40));
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="stopTest",["blockId"]=blockId}));
                session.StationAbort("Fixture cleanup after independent stop verification");yield return null;
                limit=Time.realtimeSinceStartup+15;
                while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                var events=File.ReadAllLines(Path.Combine(run,"events.ndjson")).Select(JObject.Parse).ToArray();
                Assert.That(events.Count(e=>(string)e["eventType"]=="BlockStarted"),Is.EqualTo(1));
                Assert.That(events.Count(e=>(string)e["eventType"]=="BlockPaused"),Is.EqualTo(1));
                Assert.That(events.Count(e=>(string)e["eventType"]=="BlockResumed"),Is.EqualTo(1));
                Assert.That(events.Any(e=>(string)e["eventType"]=="BlockAborted"),Is.False,"Stopping a test is not a block abort");
                var end=events.Single(e=>(string)e["eventType"]=="BlockEnded");
                Assert.That((string)end["payload"]["reason"],Is.EqualTo("operator_stop"));
                Assert.That((double)end["payload"]["activeSeconds"],Is.EqualTo(played).Within(.001));
                var pause=events.Single(e=>(string)e["eventType"]=="BlockPaused");
                var resume=events.Single(e=>(string)e["eventType"]=="BlockResumed");
                var samples=File.ReadAllLines(Path.Combine(run,"samples.ndjson")).Select(JObject.Parse).ToArray();
                Assert.That(samples.Any(sample=>(long)sample["monotonicTicks"]>(long)pause["monotonicTicks"] &&
                    (long)sample["monotonicTicks"]<=(long)resume["monotonicTicks"]),Is.True,"Raw logging continues during pause");
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics/station-paused-run.txt")),run);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }
        [UnityTest]
        public IEnumerator ArmedParticipantStartsAllFourRecordedBlocks()
        {
            var host=new GameObject("Separate administrator station fixture");
            var view=host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;
            var input=new Input();station.Controls=input;
            try
            {
                for(int i=0;i<5;i++)yield return null;
                Assert.That(view.ParticipantOnly,Is.True);
                Assert.That(view.ParticipantCamera.targetTexture,Is.Null);
                Assert.That(view.OperatorCamera.targetTexture,Is.Not.Null);
                Assert.That(session.PanelRoot.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                Assert.That(station.ParticipantRoot.Query<Button>().ToList(),Is.Empty,"Participant view must contain no administrator buttons");
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="station-test",
                    ["participantName"]="Synthetic Test Alias",["initialNotes"]="Initial fixture observation",
                    ["conditionOrder"]=new JArray("WE_FT","WE_MT","NE_FT","NE_MT")});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.BlockReady && Time.realtimeSinceStartup<limit)
                { ReviewFixtureWhenReady(session,station);clock.Advance(TimeSpan.FromSeconds(0.1));yield return null; }
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                string run=session.StationRecordingPath;
                for(int block=0;block<4;block++)
                {
                    Assert.That(session.StationParticipantTrigger(),Is.False,"Unarmed participant cannot begin");
                    // A mouse press made before arming cannot later start a block.
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    station.ApplyCommand(new JObject{["action"]="arm"});yield return null;
                    clock.Advance(TimeSpan.FromSeconds(30));yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"Armed waiting must not start the block clock");
                    Assert.That(station.ParticipantRoot.Q<Label>("participantTitle").text,Is.EqualTo("Shoot to begin"));
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    input.Frame=new DesktopControlFrame(false,new Vector2(300,300),Vector3.zero);yield return null;
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"Focus loss cancels a pending start click");
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning),session.StationMessage);
                    Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(300).Within(0.001));
                    Assert.That(session.StationShots,Is.Zero,"The start prompt consumes its click");
                    Assert.That(station.ParticipantRoot.Q("participantPrompt").ClassListContains("participant-hidden"),Is.True);
                    var scenario=(DevelopmentSessionScenario)typeof(DevelopmentSessionPanel).GetField("scenario",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
                    if(session.Engine.CurrentCondition.EndsWith("_MT"))
                        for(int step=0;step<35;step++){clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                    float poseDeadline=Time.realtimeSinceStartup+2;
                    while((scenario.Positions.Count==0 || Math.Abs(scenario.Positions.First().Value.X)>2) && Time.realtimeSinceStartup<poseDeadline)yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning),session.StationMessage);
                    Assert.That(scenario.Positions.Count,Is.GreaterThan(0),session.StationMessage);
                    var point=scenario.Positions.First().Value+new Vector3d(0,.65,0);
                    var viewport=view.ParticipantCamera.WorldToViewportPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                    var bounds=station.ParticipantRoot.worldBound;
                    var pointer=new Vector2(bounds.xMin+viewport.x*bounds.width,bounds.yMax-viewport.y*bounds.height);
                    Assert.That(bounds.Contains(pointer),Is.True,"Head aim must remain inside the participant surface");
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,pressed:true);yield return null;
                    clock.Advance(TimeSpan.FromSeconds(0.03));
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,released:true);yield return null;
                    Assert.That(session.StationShots,Is.EqualTo(1));Assert.That(session.StationHits,Is.EqualTo(1));
                    clock.Advance(TimeSpan.FromSeconds(300));yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                    limit=Time.realtimeSinceStartup+15;
                    while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                    clock.Advance(TimeSpan.FromSeconds(60));yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded),"Waiting does not start the break");
                Assert.That(session.StationCanArm,Is.False);
                station.ApplyCommand(new JObject{["action"]="startBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(5).Within(.001));
                Assert.Throws<InvalidOperationException>(()=>session.StationArm());
                clock.Advance(TimeSpan.FromSeconds(5));yield return null;
                Assert.That(session.TestArmed,Is.False,"Timer completion never arms a test");
                    yield return null;
                }
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.SessionComplete));
                limit=Time.realtimeSinceStartup+15;
                while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(session.IsBusy,Is.False);
                var events=File.ReadAllLines(Path.Combine(run,"events.ndjson")).Select(JObject.Parse).ToArray();
                var details=events.Single(e=>(string)e["eventType"]=="OperatorNote" &&
                    ((string)e["payload"]["text"]).StartsWith("Participant details: "));
                var intake=JObject.Parse(((string)details["payload"]["text"]).Substring("Participant details: ".Length));
                Assert.That((string)intake["participantName"],Is.EqualTo("Synthetic Test Alias"));
                Assert.That((string)intake["participantId"],Is.EqualTo("station-test"));
                Assert.That((string)intake["initialNotes"],Is.EqualTo("Initial fixture observation"));
                Assert.That((long)details["sequence"],Is.LessThan((long)events.First(e=>(string)e["eventType"]=="TestArmed")["sequence"]));
                Assert.That(events.Count(e=>(string)e["eventType"]=="TestArmed"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ParticipantStartTrigger"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ShotFired"),Is.EqualTo(4));
                foreach(var start in events.Where(e=>(string)e["eventType"]=="ParticipantStartTrigger"))
                {
                    string blockId=(string)start["payload"]["blockId"];
                    var blockStart=events.Single(e=>(string)e["eventType"]=="BlockStarted" && (string)e["payload"]["blockId"]==blockId);
                    Assert.That((long)start["sequence"],Is.LessThan((long)blockStart["sequence"]),"Participant trigger must precede the timed block transition");
                }
                for(int i=1;i<events.Length;i++)Assert.That((long)events[i]["monotonicTicks"],Is.GreaterThanOrEqualTo((long)events[i-1]["monotonicTicks"]));
                var state=JObject.Parse(station.StateJson());Assert.That(state["blocks"].Count(),Is.EqualTo(4));
                Assert.That(state["blocks"].All(row=>(string)row["status"]=="Completed"),Is.True);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics/station-pipeline-run.txt")),run);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        [UnityTest]
        public IEnumerator AdministratorPreparationExceptionsCheckpointsAndArchiveAreConnected()
        {
            var host=new GameObject("Administrator workflow integration fixture");
            host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;station.Controls=new Input();
            try
            {
                for(int i=0;i<5;i++)yield return null;
                station.ApplyCommand(new JObject { ["action"]="applySetup",["conditionOrder"]=new JArray("NE_FT","WE_FT","NE_MT","WE_MT"),
                    ["durations"]=new JObject{["NE_FT"]=2,["WE_FT"]=2,["NE_MT"]=2,["WE_MT"]=2},["practiceSeconds"]=10,["breakSeconds"]=30 });
                Assert.That(session.StationOrder[0],Is.EqualTo("NE_FT"),"Order can change before participant intake");
                Assert.That(session.StationParticipant,Is.Empty);
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="workflow-test"});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.Calibration && Time.realtimeSinceStartup<limit)
                {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Calibration));
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="startPractice"}),"Calibration acceptance is explicit");
                station.ApplyCommand(new JObject{["action"]="generateCalibration"});
                Assert.That((bool)JObject.Parse(station.StateJson())["tools"]["calibration"]["canAccept"],Is.True);
                station.ApplyCommand(new JObject{["action"]="redoCalibration"});
                ReviewFixtureWhenReady(session,station);
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Practice));
                station.ApplyCommand(new JObject{["action"]="restartPractice"});
                station.ApplyCommand(new JObject{["action"]="finishPractice"});
                string original=session.StationRecordingPath;
                station.ApplyCommand(new JObject{["action"]="skipTest",["blockId"]=session.Engine.CurrentBlockId,["reason"]="Synthetic skip verification"});
                yield return null;
                limit=Time.realtimeSinceStartup+15;while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                var skipped=JObject.Parse(File.ReadAllText(Path.Combine(original,"test-checkpoint-NE_FT-attempt-01.json")));
                Assert.That((string)skipped["status"],Is.EqualTo("Skipped"));
                Assert.That((string)skipped["scoreStatus"],Is.EqualTo("unavailable"));
                station.ApplyCommand(new JObject{["action"]="repeatTest",["blockId"]=session.Engine.CurrentBlockId,["condition"]="NE_FT",["reason"]="Synthetic repeat verification"});
                station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.CurrentBlockId,Is.EqualTo("NE_FT-attempt-02"));
                yield return null;
                station.ApplyCommand(new JObject{["action"]="arm"});
                Assert.That(session.StationParticipantTrigger(),Is.True);
                clock.Advance(TimeSpan.FromSeconds(2));yield return null;
                limit=Time.realtimeSinceStartup+15;while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(File.Exists(Path.Combine(original,"test-checkpoint-NE_FT-attempt-02.json")),Is.True);
                for(int i=0;i<3;i++)
                {
                    station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                    station.ApplyCommand(new JObject{["action"]="skipTest",["blockId"]=session.Engine.CurrentBlockId,["reason"]="Finish fixture without scoring"});
                    yield return null;
                    limit=Time.realtimeSinceStartup+15;while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                }
                station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});yield return null;
                limit=Time.realtimeSinceStartup+15;while(!session.StationCanStartParticipant && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(File.Exists(Path.Combine(original,"session-summary.json")),Is.True);
                string originalEvents=File.ReadAllText(Path.Combine(original,"events.ndjson"));
                station.ApplyCommand(new JObject{["action"]="reviewRecording",["id"]=Path.GetFileName(original)});
                limit=Time.realtimeSinceStartup+15;
                while((bool)JObject.Parse(station.StateJson())["tools"]["archiveBusy"] && Time.realtimeSinceStartup<limit)yield return null;
                var review=JObject.Parse(station.StateJson())["tools"]["review"];
                Assert.That(review["attempts"].Count(),Is.EqualTo(5));
                Assert.That(review["notes"].Count(),Is.GreaterThan(0));
                station.ApplyCommand(new JObject{["action"]="exportRecording",["id"]=Path.GetFileName(original)});
                limit=Time.realtimeSinceStartup+15;
                while((bool)JObject.Parse(station.StateJson())["tools"]["archiveBusy"] && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(File.Exists((string)JObject.Parse(station.StateJson())["tools"]["exportPath"]),Is.True);
                station.ApplyCommand(new JObject{["action"]="repeatTest",["blockId"]="",["condition"]="NE_FT",["reason"]="Repeat after finalization"});
                limit=Time.realtimeSinceStartup+15;while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(session.Engine.CurrentBlockId,Is.EqualTo("NE_FT-attempt-03"));
                Assert.That(session.StationRecordingPath,Is.Not.EqualTo(original));
                Assert.That(File.ReadAllText(Path.Combine(original,"events.ndjson")),Is.EqualTo(originalEvents),"A finalized recording is never reopened for writes");
                session.StationAbort("Fixture complete");yield return null;
                limit=Time.realtimeSinceStartup+15;while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        private static void ReviewFixtureWhenReady(DevelopmentSessionPanel session,DevelopmentTestStation station)
        {
            if(session.IsBusy || session.Engine?.State!=SessionState.Calibration)return;
            station.ApplyCommand(new JObject{["action"]="generateCalibration"});
            station.ApplyCommand(new JObject{["action"]="acceptCalibration"});
            station.ApplyCommand(new JObject{["action"]="startPractice"});
        }
    }
}
