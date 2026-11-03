using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Session;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class ParticipantTutorialTests
    {
        private sealed class Input:IDesktopControls
        {
            public bool ReloadOnce;
            public DesktopControlFrame Read(IPanel panel)
            {
                bool reload=ReloadOnce;ReloadOnce=false;
                return new DesktopControlFrame(true,new Vector2(640,360),Vector3.zero,reload:reload);
            }
        }

        // Existing station tests exercise the same complete preparation path.
        // Only explanatory reading time is accelerated; input, captures, review
        // and practice actions still go through production entry points.
        public static void AdvanceWalkthrough(DevelopmentSessionPanel session,DevelopmentTestStation station,bool finishPractice=true)
        {
            if(session.IsBusy || session.Engine==null || session.Engine.State==SessionState.Idle)return;
            var flow=session.Tutorial;
            if(flow.Step.Automatic || flow.Stage==TutorialStage.Demonstration || flow.Stage==TutorialStage.HeadCalibration || flow.Stage==TutorialStage.GunCalibration)
                flow.Tick(60,session.TutorialSettings.Duration(flow.Step));
            if(flow.Stage==TutorialStage.Fire)session.StationParticipantTrigger();
            if(flow.Stage==TutorialStage.Reload)session.StationReload();
            var state=JObject.Parse(station.StateJson())["tutorial"];
            if((bool)state["canPrimary"] && flow.Stage!=TutorialStage.Form)
                Command(station,session,(string)state["primaryAction"]);
            else if(finishPractice && flow.Stage==TutorialStage.PracticeRunning)Command(station,session,"tutorialFinishPractice");
        }
        public static void Command(DevelopmentTestStation station,DevelopmentSessionPanel session,string action)
            =>station.ApplyCommand(new JObject { ["action"]=action,["tutorialRevision"]=session.Tutorial.Revision });

        [Test]
        public void SelectedNarrationLoadsAndPacesOnlyExplanations()
        {
            var host=new GameObject("Muted narration integration fixture");
            var narration=new ParticipantTutorialNarration(host,.85f);
            try
            {
                Assert.That(narration.ClipCount,Is.EqualTo(30));
                var flow=new ParticipantTutorial();flow.Begin(0);narration.Refresh(flow);
                Assert.That(narration.CurrentClip.name,Is.EqualTo("01-welcome"));
                double duration=narration.Duration(flow.Step,7);
                Assert.That(duration,Is.GreaterThan(1));
                flow.Tick(duration-.01,duration);Assert.That(flow.Stage,Is.EqualTo(TutorialStage.Welcome));
                flow.SetPaused(true,flow.Revision);narration.Refresh(flow);
                flow.Tick(100,duration);Assert.That(flow.Stage,Is.EqualTo(TutorialStage.Welcome));
                Assert.That(narration.Paused,Is.True);
                flow.Replay(flow.Revision);narration.Refresh(flow);Assert.That(narration.Paused,Is.True);
                flow.SetPaused(false,flow.Revision);narration.Refresh(flow);
                flow.Tick(duration,duration);narration.Refresh(flow);
                Assert.That(narration.CurrentClip.name,Is.EqualTo("01-01-guidance-and-questions"));
                flow.Tick(100,1);narration.Refresh(flow);
                flow.Tick(100,1);Assert.That(flow.Stage,Is.EqualTo(TutorialStage.ReadyForSetup));
                Assert.That(narration.HasClip(ParticipantTutorial.Steps[TutorialStage.Demonstration]),Is.True);
                Assert.That(narration.Duration(ParticipantTutorial.Steps[TutorialStage.Demonstration],7),Is.GreaterThan(7));
                narration.Stop();Assert.That(narration.CurrentClip,Is.Null);
            }
            finally{narration.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }

        [Test]
        public void SimulatedDemonstrationDisarmsWhenItsStartEventCannotBeSaved()
        {
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            var demo=new TutorialDemonstration(clock,command=>
            { if(command=="Start")throw new IOException("Injected recording failure"); });
            Assert.Throws<IOException>(()=>demo.Start("synthetic-test",1,2));
            var link=(Elts.EltsLink.MockEltsLink)typeof(TutorialDemonstration)
                .GetField("link",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(demo);
            Assert.That(demo.Active,Is.False);
            Assert.That(link.State,Is.EqualTo(Elts.EltsLink.EltsLinkState.Idle));
            Assert.That(link.LedPermission,Is.False,"A failed event write must still stop and disarm the simulator.");
        }

        [UnityTest]
        public IEnumerator WalkthroughChecksActionsThenUsesRealScenarioAndBreakEvents()
        {
            var host=new GameObject("Participant tutorial integration fixture");
            var view=host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;
            var input=new Input();station.Controls=input;
            string captureDirectory=Environment.GetEnvironmentVariable("ELTS_TUTORIAL_CAPTURE_DIR");
            RenderTexture capture=null;
            var panel=(PanelSettings)typeof(DevelopmentTestStation).GetField("settings",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(station);
            var captured=new HashSet<TutorialStage>();
            try
            {
                if(!string.IsNullOrEmpty(captureDirectory))
                { Directory.CreateDirectory(captureDirectory);capture=new RenderTexture(1920,1080,0);capture.Create();panel.targetTexture=capture; }
                for(int i=0;i<5;i++)yield return null;
                station.ApplyCommand(new JObject { ["action"]="applySetup",["conditionOrder"]=new JArray("NE_FT","WE_FT","NE_MT","WE_MT"),
                    ["durations"]=new JObject{["NE_FT"]=1,["WE_FT"]=1,["NE_MT"]=1,["WE_MT"]=1},["practiceSeconds"]=10,["breakSeconds"]=2 });
                station.ApplyCommand(new JObject { ["action"]="startParticipant",["participant"]="tutorial-integration" });
                float deadline=Time.realtimeSinceStartup+60;
                while(session.Engine?.State!=SessionState.Calibration && Time.realtimeSinceStartup<deadline)
                {clock.Advance(TimeSpan.FromSeconds(.1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.Calibration));
                Assert.That(session.Tutorial.Started,Is.False,"creating a recording does not start the walkthrough");
                Assert.Throws<InvalidOperationException>(()=>session.StationArm());
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="generateCalibration"}));
                Command(station,session,"tutorialBegin");
                int firstRevision=session.Tutorial.Revision;
                Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="tutorialContinue",["tutorialRevision"]=firstRevision-1}));
                bool testedHead=false,testedFire=false,testedReload=false,testedPractice=false,testedDemo=false;
                deadline=Time.realtimeSinceStartup+100;
                while(!session.StationCanArm && Time.realtimeSinceStartup<deadline)
                {
                    var stage=session.Tutorial.Stage;
                    if(capture!=null && !captured.Contains(stage) &&
                        new[]{TutorialStage.Welcome,TutorialStage.Equipment,TutorialStage.HeadCalibration,TutorialStage.Holding,
                            TutorialStage.GunCalibration,TutorialStage.Demonstration,TutorialStage.BreakOverview}.Contains(stage))
                    {
                        bool wasPaused=session.Tutorial.Paused;
                        // Freeze progression only after the artwork has settled.
                        session.Tutorial.Tick(5,60);
                        Command(station,session,"tutorialPause");
                        for(int frame=0;frame<8;frame++)yield return null;
                        SaveCapture(capture,Path.Combine(captureDirectory,stage+".png"));captured.Add(stage);
                        if(!wasPaused)Command(station,session,"tutorialResume");
                    }
                    if(stage==TutorialStage.HeadCalibration && session.TutorialDotVisible && !testedHead)
                    {
                        Assert.Throws<InvalidOperationException>(()=>Command(station,session,"tutorialAccept"));
                        Command(station,session,"tutorialCapture");
                        int old=session.Tutorial.Revision;Command(station,session,"tutorialRedo");
                        Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="tutorialAccept",["tutorialRevision"]=old}));
                        testedHead=true;
                    }
                    if(stage==TutorialStage.Fire && !testedFire)
                    {
                        Assert.Throws<InvalidOperationException>(()=>Command(station,session,"tutorialContinue"));
                        Assert.That(session.StationParticipantTrigger(),Is.True,"a real recorded synthetic trigger is accepted for teaching");
                        Assert.That(session.StationShots,Is.Zero,"tutorial shots never change scored counters");
                        testedFire=true;
                    }
                    if(stage==TutorialStage.PracticeRunning && !testedPractice)
                    {
                        Assert.That(session.TutorialTargetPosition.HasValue,Is.True,"practice uses a real target");
                        Assert.That(view.TutorialSceneVisible,Is.True,"practice keeps a clear teaching stage");
                        Assert.That(session.StationParticipantTrigger(),Is.True);Assert.That(session.StationReload(),Is.True);
                        Command(station,session,"tutorialPause");double remaining=session.Engine.RemainingSeconds;
                        clock.Advance(TimeSpan.FromSeconds(100));yield return null;
                        Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(remaining).Within(.001));
                        Assert.That(session.StationParticipantTrigger(),Is.False);
                        Command(station,session,"tutorialResume");Command(station,session,"tutorialRestartPractice");
                        Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(10));
                        Assert.That(session.Tutorial.HeadAccepted && session.Tutorial.GunAccepted,Is.True);
                        testedPractice=true;
                    }
                    if(stage==TutorialStage.Reload && !testedReload)
                    {
                        input.ReloadOnce=true;yield return null;
                        Assert.That(session.Tutorial.Reloaded,Is.True,"the participant input frame must satisfy the reload check");
                        Assert.That(session.ParticipantAmmo,Is.EqualTo(session.StationMagazineCapacity));
                        testedReload=true;
                    }
                    if(stage==TutorialStage.Demonstration && session.Tutorial.DemoRequested && !testedDemo)
                    {
                        Command(station,session,"tutorialReplay");Assert.That(session.Tutorial.DemoAttempt,Is.EqualTo(1));testedDemo=true;
                    }
                    AdvanceWalkthrough(session,station);clock.Advance(TimeSpan.FromSeconds(.1));yield return null;
                }
                Assert.That(session.StationCanArm,Is.True,session.StationMessage+" / "+session.Tutorial.Stage);
                Assert.That(view.TutorialSceneVisible,Is.False,"the scenario scenery must return before arming");
                Assert.That(testedHead && testedFire && testedReload && testedPractice && testedDemo,Is.True);
                Assert.That(Directory.GetFiles(session.StationRecordingPath,"synthetic-calibration-*.json").Length,Is.EqualTo(2));
                Assert.That(session.Tutorial.DemoAttempt,Is.EqualTo(1));
                string run=session.StationRecordingPath;
                session.Tutorial.Tick(60,60);
                yield return null;
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.Armed));
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                Assert.That(session.StationParticipantTrigger(),Is.True);
                Assert.That(session.StationShots,Is.Zero,"participant start is not a scored shot");
                for(int i=0;i<4;i++)
                {
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning));
                    clock.Advance(TimeSpan.FromSeconds(1));
                    deadline=Time.realtimeSinceStartup+30;
                    while((session.Engine.State!=SessionState.BlockEnded || session.IsBusy ||
                        session.Tutorial.Stage!=(i==3?TutorialStage.FinalEnded:TutorialStage.ScenarioEnded)) &&
                        Time.realtimeSinceStartup<deadline)yield return null;
                    Assert.That(session.StationBreakReady,Is.True,"checkpoint must be durable before the break");
                    session.Tutorial.Tick(60,60);
                    yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                    Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(2));
                    if(capture!=null && i==3)
                    {for(int frame=0;frame<8;frame++)yield return null;SaveCapture(capture,Path.Combine(captureDirectory,"FinalBreak.png"));}
                    clock.Advance(TimeSpan.FromSeconds(2));
                    yield return null;
                    if(i<3)
                    {
                        Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.NextReady));
                        session.Tutorial.Tick(60,60);
                        deadline=Time.realtimeSinceStartup+10;
                        while(session.Engine.State!=SessionState.BlockRunning && Time.realtimeSinceStartup<deadline)
                            yield return null;
                    }
                }
                deadline=Time.realtimeSinceStartup+30;
                do{yield return null;}while((session.IsBusy || session.HasOpenDesktopRecording) && Time.realtimeSinceStartup<deadline);
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.Form));
                Assert.That(session.StationCanStartParticipant,Is.False,"new identity cannot replace an unconfirmed form");
                string before=File.ReadAllText(Path.Combine(run,"events.ndjson"));
                if(capture!=null)
                {session.Tutorial.Tick(3,60);for(int frame=0;frame<8;frame++)yield return null;SaveCapture(capture,Path.Combine(captureDirectory,"Form.png"));}
                Command(station,session,"tutorialForm");
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.ThankYou));
                Assert.That(session.StationCanStartParticipant,Is.True);
                Assert.That(File.ReadAllText(Path.Combine(run,"events.ndjson")),Is.EqualTo(before),"form receipt does not rewrite closed sensor data");
                var receipt=JObject.Parse(File.ReadAllText(Directory.GetFiles(run,"tutorial-completion-*.json").Single()));
                Assert.That((string)receipt["participantId"],Is.EqualTo("tutorial-integration"));
                Assert.That((bool)receipt["allFourScenariosCompleted"],Is.True);
                var events=before.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
                Assert.That(events.Count(e=>(string)e["eventType"]=="TutorialSimulatedEltsStart"),Is.EqualTo(1));
                Assert.That(events.Any(e=>(string)e["eventType"]=="TutorialShotFired"),Is.True);
                Assert.That(events.Any(e=>(string)e["eventType"]=="PracticeShotFired"),Is.True);
                Assert.That(events.Count(e=>(string)e["eventType"]=="ShotFired"),Is.Zero);
                Assert.That(events.Count(e=>(string)e["eventType"]=="ParticipantStartTrigger"),Is.EqualTo(1));
                Assert.That(events.Count(e=>(string)e["eventType"]=="AutomaticScenarioStart"),Is.EqualTo(3));
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics/tutorial-run.txt")),run);
                if(capture!=null)
                {session.Tutorial.Tick(3,60);for(int frame=0;frame<8;frame++)yield return null;SaveCapture(capture,Path.Combine(captureDirectory,"ThankYou.png"));}
            }
            finally
            {
                if(panel!=null)panel.targetTexture=null;
                if(capture!=null){capture.Release();UnityEngine.Object.DestroyImmediate(capture);}
                UnityEngine.Object.DestroyImmediate(host);
            }
        }
        private static void SaveCapture(RenderTexture target,string path)
        {
            var previous=RenderTexture.active;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try{RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();File.WriteAllBytes(path,pixels.EncodeToPNG());}
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(pixels);}
        }
    }
}
