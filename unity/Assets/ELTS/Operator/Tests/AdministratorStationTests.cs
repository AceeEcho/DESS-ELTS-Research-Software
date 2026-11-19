using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Config;
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
                ParticipantCalibrationFixture.Save(session,4101);
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="4101",
                    ["conditionOrder"]=new JArray("WE_FT","WE_MT","NE_FT","NE_MT")});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Tutorial.Stage!=Elts.Session.TutorialStage.FirstReady && Time.realtimeSinceStartup<limit)
                { ReviewFixtureWhenReady(session,station);clock.Advance(TimeSpan.FromSeconds(.1));yield return null; }
                Assert.That(session.Engine?.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                ParticipantTutorialTests.ArmAfterInstructions(session,station);
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
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(session.Engine.BreakDurationSeconds).Within(.001));
                Assert.Throws<InvalidOperationException>(()=>session.StationArm());
                ParticipantTutorialTests.FinishInstruction(session);
                clock.Advance(TimeSpan.FromSeconds(session.Engine.BreakDurationSeconds));yield return null;
                Assert.That(session.TestArmed,Is.False,"Timer completion never arms a test");
                Assert.That(session.Engine.CurrentCondition,Is.EqualTo("WE_MT"));
                Assert.That(session.Engine.CurrentDurationSeconds,Is.EqualTo(40));
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="stopTest",["blockId"]=blockId}));
                session.StationAbort();yield return null;
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
                var order=new JArray("WE_FT","WE_MT","NE_FT","NE_MT");
                const int botLimit=6;
                Assert.That((int)JObject.Parse(station.StateJson())["activeTargetCount"],Is.EqualTo(view.Configuration.Scenario.ActiveTargetCount));
                Assert.Throws<ArgumentException>(()=>station.ApplyCommand(new JObject{["action"]="setActiveTargetCount",["count"]=6.5}));
                Assert.Throws<ArgumentOutOfRangeException>(()=>station.ApplyCommand(new JObject{["action"]="setActiveTargetCount",["count"]=0}));
                Assert.Throws<ArgumentOutOfRangeException>(()=>station.ApplyCommand(new JObject{["action"]="setActiveTargetCount",["count"]=31}));
                station.ApplyCommand(new JObject{["action"]="setActiveTargetCount",["count"]=botLimit});
                Assert.That((int)JObject.Parse(station.StateJson())["activeTargetCount"],Is.EqualTo(botLimit));
                // Participant IDs are numeric; nothing starts otherwise. A partly numeric ID is never trimmed to a number.
                Assert.Throws<ArgumentException>(()=>station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="station-test",["conditionOrder"]=order}));
                Assert.Throws<ArgumentException>(()=>station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="12x",["conditionOrder"]=order}));
                Assert.Throws<ArgumentException>(()=>station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="",["conditionOrder"]=order}));
                Assert.That(session.Engine,Is.Null);
                ParticipantCalibrationFixture.Save(session,4102,"Synthetic Test Alias",eye:new[]{0.01,-0.02,-0.09});
                var saved=ParticipantCalibration.Load(session.CalibrationProfileDirectory,4102);
                float scanLimit=Time.realtimeSinceStartup+5;
                while(!JObject.Parse(station.StateJson())["participantCalibrations"].Any(c=>(int)c["id"]==4102) && Time.realtimeSinceStartup<scanLimit)yield return null;
                var listed=JObject.Parse(station.StateJson())["participantCalibrations"].Single(c=>(int)c["id"]==4102);
                Assert.That((bool)listed["available"],Is.True);
                Assert.That(listed["name"],Is.Null,"Legacy names never reach the dashboard");
                // Leading zeros normalize to the same participant.
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="04102",["conditionOrder"]=order});
                Assert.That(session.StationParticipant,Is.EqualTo("4102"));
                Assert.That(view.Configuration.Rig.HeadEyeOffsetM,Is.EqualTo(saved.HeadEyeOffsetM),"Saved eye offset is applied");
                Assert.That(view.Configuration.Rig.CalibrationId,Is.EqualTo(saved.CalibrationId));
                float limit=Time.realtimeSinceStartup+20;
                while(session.Tutorial.Stage!=Elts.Session.TutorialStage.FirstReady && Time.realtimeSinceStartup<limit)
                { ReviewFixtureWhenReady(session,station);clock.Advance(TimeSpan.FromSeconds(0.1));yield return null; }
                Assert.That(session.Engine?.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                string run=session.StationRecordingPath;
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="setActiveTargetCount",["count"]=botLimit+1}));
                for(int block=0;block<4;block++)
                {
                    Assert.That(session.StationParticipantTrigger(),Is.False,"Unarmed participant cannot begin");
                    // A mouse press made before arming cannot later start a block.
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    ParticipantTutorialTests.ArmAfterInstructions(session,station);yield return null;
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
                        for(int step=0;step<80;step++){clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                    // Wait for the current posed actors before choosing an aim
                    // point. Keep that simulated pose fixed through the click.
                    clock.Advance(TimeSpan.FromSeconds(0.03));
                    yield return null;yield return null;
                    float poseDeadline=Time.realtimeSinceStartup+2;
                    while(scenario.Positions.Count==0 && Time.realtimeSinceStartup<poseDeadline)yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning),session.StationMessage);
                    Assert.That(scenario.Positions.Count,Is.EqualTo(botLimit),session.Engine.CurrentCondition);
                    var point=TargetSurfaceTestPoints.Head(view,scenario.Positions.First().Key);
                    var viewport=view.ParticipantCamera.WorldToViewportPoint(point);
                    var bounds=station.ParticipantRoot.worldBound;
                    var pointer=new Vector2(bounds.xMin+viewport.x*bounds.width,bounds.yMax-viewport.y*bounds.height);
                    Assert.That(bounds.Contains(pointer),Is.True,"Head aim must remain inside the participant surface");
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,pressed:true);yield return null;
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,released:true);yield return null;
                    Assert.That(session.StationShots,Is.EqualTo(1));Assert.That(session.StationHits,Is.EqualTo(1));
                    Assert.That(session.StationTargetKills,Is.EqualTo(1));
                    Assert.That(scenario.Positions.Count,Is.EqualTo(botLimit),"A replacement refills the same limit");
                    clock.Advance(TimeSpan.FromSeconds(300));yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                    limit=Time.realtimeSinceStartup+15;
                    while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                    clock.Advance(TimeSpan.FromSeconds(60));yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded),"Waiting does not start the break");
                Assert.That(session.StationCanArm,Is.False);
                station.ApplyCommand(new JObject{["action"]="startBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(session.Engine.BreakDurationSeconds).Within(.001));
                Assert.Throws<InvalidOperationException>(()=>session.StationArm());
                ParticipantTutorialTests.FinishInstruction(session);
                clock.Advance(TimeSpan.FromSeconds(session.Engine.BreakDurationSeconds));yield return null;
                Assert.That(session.TestArmed,Is.False,"Timer completion never arms a test");
                    yield return null;
                }
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.SessionComplete));
                limit=Time.realtimeSinceStartup+15;
                while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(session.IsBusy,Is.False);
                string eventText=File.ReadAllText(Path.Combine(run,"events.ndjson"));
                var events=eventText.Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
                // The name stays out of the immutable recording; the numeric ID and calibration file are recorded.
                Assert.That(eventText+File.ReadAllText(Path.Combine(run,"participant.json")),Does.Not.Contain("Synthetic Test Alias"));
                Assert.That((string)JObject.Parse(File.ReadAllText(Path.Combine(run,"participant.json")))["participantId"],Is.EqualTo("4102"));
                var details=events.Single(e=>(string)e["eventType"]=="OperatorNote" &&
                    ((string)e["payload"]["text"]).StartsWith("Calibration: participant-4102.json · SHA-256 "+saved.Sha256,StringComparison.Ordinal));
                Assert.That((long)details["sequence"],Is.LessThan((long)events.First(e=>(string)e["eventType"]=="TestArmed")["sequence"]));
                Assert.That(events.Count(e=>(string)e["eventType"]=="TestArmed"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ParticipantStartTrigger"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ShotFired"),Is.EqualTo(4));
                var layouts=events.Where(e=>(string)e["eventType"]=="DevelopmentLayoutConfigured").ToArray();
                Assert.That(layouts.Length,Is.EqualTo(4));
                Assert.That(layouts.Select(e=>(int)e["payload"]["activeTargetCount"]),Is.All.EqualTo(botLimit));
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
        public IEnumerator ParticipantStartsWithoutAUsableSavedCalibration()
        {
            var host=new GameObject("Optional profile intake fixture");
            var view=host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;station.Controls=new Input();
            try
            {
                for(int i=0;i<5;i++)yield return null;
                string stagedCalibration=view.Configuration.Rig.CalibrationId;
                var stagedEye=view.Configuration.Rig.HeadEyeOffsetM;
                Directory.CreateDirectory(session.CalibrationProfileDirectory);
                // 4198 has nothing saved; 4197 has a file that is not a calibration.
                File.Delete(Path.Combine(session.CalibrationProfileDirectory,ParticipantCalibration.FileNameFor(4198)));
                File.WriteAllText(Path.Combine(session.CalibrationProfileDirectory,ParticipantCalibration.FileNameFor(4197)),"{ not a calibration");
                foreach(var (id,note) in new[]{("4198","no saved profile for this ID"),("4197","could not be read")})
                {
                    float limit=Time.realtimeSinceStartup+30;
                    while(!session.StationCanStartParticipant && Time.realtimeSinceStartup<limit)yield return null;
                    station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]=id});
                    Assert.That(session.StationParticipant,Is.EqualTo(id));
                    Assert.That(view.Configuration.Rig.CalibrationId,Is.EqualTo(stagedCalibration),"Station settings apply without a usable profile");
                    Assert.That(view.Configuration.Rig.HeadEyeOffsetM,Is.EqualTo(stagedEye));
                    while(session.Engine?.State!=SessionState.Calibration && Time.realtimeSinceStartup<limit)
                    {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                    Assert.That(session.Engine?.State,Is.EqualTo(SessionState.Calibration),session.StationMessage);
                    string run=session.StationRecordingPath;
                    session.StationAbort();yield return null;
                    yield return WaitForSave(session);
                    Assert.That(File.ReadAllText(Path.Combine(run,"events.ndjson")),Does.Contain(note),"The recording says which calibration applied");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        [UnityTest]
        public IEnumerator ManualModeSkipsWalkthroughAndWaitsForEveryOperatorAction()
        {
            var host=new GameObject("Manual mode station fixture");
            host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;
            var input=new Input();station.Controls=input;
            try
            {
                for(int i=0;i<5;i++)yield return null;
                station.ApplyCommand(new JObject { ["action"]="applySetup",["conditionOrder"]=new JArray("WE_FT","WE_MT","NE_FT","NE_MT"),
                    ["durations"]=new JObject{["WE_FT"]=2,["WE_MT"]=2,["NE_FT"]=2,["NE_MT"]=2},["practiceSeconds"]=10,["breakSeconds"]=5 });
                ParticipantCalibrationFixture.Save(session,4103);
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="4103"});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.Calibration && Time.realtimeSinceStartup<limit)
                {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine?.State,Is.EqualTo(SessionState.Calibration),session.StationMessage);
                Assert.That((bool)JObject.Parse(station.StateJson())["tutorial"]["canManual"],Is.True,"Manual mode is offered before the walkthrough");

                ParticipantTutorialTests.Command(station,session,"tutorialManual");
                string run=session.StationRecordingPath;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                Assert.That(session.Tutorial.Manual,Is.True);
                Assert.That(session.Tutorial.Stage,Is.EqualTo(Elts.Session.TutorialStage.FirstReady));
                Assert.That(Directory.GetFiles(run,"synthetic-calibration-*.json").Length,Is.EqualTo(1),"Manual mode keeps the saved fixture");
                var state=JObject.Parse(station.StateJson());
                Assert.That((bool)state["tutorial"]["canManual"],Is.False);
                Assert.That((bool)state["tools"]["automaticStudy"],Is.False);

                // Finished narration would auto-arm a walkthrough session; manual mode waits.
                ParticipantTutorialTests.FinishInstruction(session);
                for(int i=0;i<10;i++){clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.TestArmed,Is.False,"Manual mode never arms on its own");
                Assert.That(session.StationCanArm,Is.True,session.StationMessage);
                ParticipantTutorialTests.ArmAfterInstructions(session,station);yield return null;
                Assert.That(session.TestArmed,Is.True);
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning),session.StationMessage);

                clock.Advance(TimeSpan.FromSeconds(3));yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                yield return WaitForSave(session);
                ParticipantTutorialTests.FinishInstruction(session);
                for(int i=0;i<10;i++){clock.Advance(TimeSpan.FromSeconds(1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded),"Manual mode never starts a break on its own");
                station.ApplyCommand(new JObject{["action"]="startBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                ParticipantTutorialTests.FinishInstruction(session);
                clock.Advance(TimeSpan.FromSeconds(6));yield return null;
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                ParticipantTutorialTests.FinishInstruction(session);
                for(int i=0;i<10;i++){clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"Manual mode never starts the next test on its own");
                Assert.That(session.TestArmed,Is.False);

                session.StationAbort();yield return null;
                yield return WaitForSave(session);
                string events=File.ReadAllText(Path.Combine(run,"events.ndjson"));
                Assert.That(events,Does.Contain("TutorialSkippedForManualMode"));
                Assert.That(events,Does.Contain("Manual mode: the operator skipped the participant walkthrough and practice."));
                Assert.That(events,Does.Not.Contain("AutomaticScenarioStart"));
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
                ParticipantCalibrationFixture.Save(session,4104);
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="4104"});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.Calibration && Time.realtimeSinceStartup<limit)
                {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine?.State,Is.EqualTo(SessionState.Calibration),session.StationMessage);
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="startPractice"}),"Calibration acceptance is explicit");
                while(session.Engine.State!=SessionState.Practice && Time.realtimeSinceStartup<limit)
                {ParticipantTutorialTests.AdvanceWalkthrough(session,station,false);clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Practice));
                ParticipantTutorialTests.Command(station,session,"tutorialRestartPractice");
                ParticipantTutorialTests.FinishInstruction(session);yield return null;
                ParticipantTutorialTests.Command(station,session,"tutorialFinishPractice");
                while(session.Tutorial.Stage!=Elts.Session.TutorialStage.FirstReady && Time.realtimeSinceStartup<limit)
                {ReviewFixtureWhenReady(session,station);clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                string original=session.StationRecordingPath;
                station.ApplyCommand(new JObject{["action"]="skipTest",["blockId"]=session.Engine.CurrentBlockId,["reason"]="Synthetic skip verification"});
                yield return null;
                yield return WaitForSave(session);
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                var skipped=JObject.Parse(File.ReadAllText(Path.Combine(original,"test-checkpoint-NE_FT-attempt-01.json")));
                Assert.That((string)skipped["status"],Is.EqualTo("Skipped"));
                Assert.That((string)skipped["scoreStatus"],Is.EqualTo("unavailable"));
                station.ApplyCommand(new JObject{["action"]="repeatTest",["blockId"]=session.Engine.CurrentBlockId,["condition"]="NE_FT",["reason"]="Synthetic repeat verification"});
                station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});
                Assert.That(session.Engine.CurrentBlockId,Is.EqualTo("NE_FT-attempt-02"));
                yield return null;
                ParticipantTutorialTests.ArmAfterInstructions(session,station);
                Assert.That(session.StationParticipantTrigger(),Is.True);
                clock.Advance(TimeSpan.FromSeconds(2));yield return null;
                yield return WaitForSave(session);
                Assert.That(File.Exists(Path.Combine(original,"test-checkpoint-NE_FT-attempt-02.json")),Is.True);
                for(int i=0;i<3;i++)
                {
                    station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                    station.ApplyCommand(new JObject{["action"]="skipTest",["blockId"]=session.Engine.CurrentBlockId,["reason"]="Finish fixture without scoring"});
                    yield return null;
                    yield return WaitForSave(session);
                }
                station.ApplyCommand(new JObject{["action"]="skipBreak",["blockId"]=session.Engine.CurrentBlockId});yield return null;
                yield return WaitForSave(session);
                ParticipantTutorialTests.FinishInstruction(session);
                ParticipantTutorialTests.Command(station,session,"tutorialForm");
                Assert.That(File.Exists(Path.Combine(original,"session-summary.json")),Is.True);
                string originalEvents=File.ReadAllText(Path.Combine(original,"events.ndjson"));
                Assert.That(File.ReadAllText(Path.Combine(original,"session-summary.json")), Does.Contain("checksumsSha256"));
                station.ApplyCommand(new JObject{["action"]="repeatTest",["blockId"]="",["condition"]="NE_FT",["reason"]="Repeat after finalization"});
                yield return WaitForSave(session);
                Assert.That(session.Engine.CurrentBlockId,Is.EqualTo("NE_FT-attempt-03"));
                Assert.That(session.StationRecordingPath,Is.Not.EqualTo(original));
                Assert.That(File.ReadAllText(Path.Combine(original,"events.ndjson")),Is.EqualTo(originalEvents),"A finalized recording is never reopened for writes");
                // The continuation builds a new scenario; it must still drive spark, flinch and fall.
                var repeated=typeof(DevelopmentSessionPanel).GetField("scenario",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
                var feedback=(Delegate)typeof(DevelopmentSessionScenario).GetField("TargetHit",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(repeated);
                Assert.That(feedback?.GetInvocationList().Select(handler=>handler.Method.Name),Has.Member("ShowTargetImpact"),
                    "A repeated test lost its hit feedback");
                session.StationAbort();yield return null;
                yield return WaitForSave(session);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        private static IEnumerator WaitForSave(DevelopmentSessionPanel session)
        {
            // First import and background indexing can delay a fixture's save.
            // This is a test timeout in wall-clock seconds, not session timing.
            const float SaveTimeoutSeconds=60;
            float deadline=Time.realtimeSinceStartup+SaveTimeoutSeconds;
            do { yield return null; } while(session.IsBusy && Time.realtimeSinceStartup<deadline);
            Assert.That(session.IsBusy,Is.False,"Save did not finish before the next fixture action: "+session.StationMessage);
        }

        private static void ReviewFixtureWhenReady(DevelopmentSessionPanel session,DevelopmentTestStation station)
        {
            ParticipantTutorialTests.AdvanceWalkthrough(session,station);
        }
    }
}
