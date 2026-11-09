using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
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
        public static void AdvanceWalkthrough(DevelopmentSessionPanel session,DevelopmentTestStation station,bool finishPractice=true,bool oneStep=false)
        {
            if(session.IsBusy || session.Engine==null || session.Engine.State==SessionState.Idle)return;
            var flow=session.Tutorial;
            var previousStage=flow.Stage;
            if(flow.Started && flow.Stage!=TutorialStage.FirstReady)
                FinishInstruction(session);
            // Visual captures need a frame at manual stages reached by a timed
            // explanation, before the helper performs the next operator action.
            if(oneStep && flow.Stage!=previousStage)return;
            if(flow.Stage==TutorialStage.Fire)session.StationParticipantTrigger();
            if(flow.Stage==TutorialStage.Reload)session.StationReload();
            var state=JObject.Parse(station.StateJson())["tutorial"];
            if((bool)state["canPrimary"] && flow.Stage!=TutorialStage.Form)
                Command(station,session,(string)state["primaryAction"]);
            else if(finishPractice && flow.Stage==TutorialStage.PracticeRunning)Command(station,session,"tutorialFinishPractice");
        }
        public static void Command(DevelopmentTestStation station,DevelopmentSessionPanel session,string action)
            =>station.ApplyCommand(new JObject { ["action"]=action,["tutorialRevision"]=session.Tutorial.Revision });
        public static void FinishInstruction(DevelopmentSessionPanel session)=>session.Tutorial.Tick(60,60);
        public static void ArmAfterInstructions(DevelopmentSessionPanel session,DevelopmentTestStation station)
        {
            FinishInstruction(session);
            station.ApplyCommand(new JObject{["action"]="arm"});
            session.SynchronizeTutorial();FinishInstruction(session);
        }

        [Test]
        public void SelectedNarrationLoadsAndPacesOnlyExplanations()
        {
            var host=new GameObject("Muted narration integration fixture");
            var narration=new ParticipantTutorialNarration(host,.85f);
            try
            {
                var slots=JObject.Parse(Resources.Load<TextAsset>("ELTS/ParticipantTutorialAudio").text);
                var steps=ParticipantTutorial.Steps.Values.Concat(ParticipantTutorial.HeadCalibrationSteps).Where(step=>step.Script!="");
                Assert.That(narration.ClipCount,Is.EqualTo(39));
                Assert.That(narration.ClipCount,Is.EqualTo(steps.Select(step=>step.Script).Distinct().Count()));
                foreach(var step in steps)
                {
                    Assert.That(slots.Property(step.Script),Is.Not.Null,"Unmapped script node "+step.Script);
                    Assert.That(narration.HasClip(step),Is.True,"Missing selected narration "+step.Script);
                }
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
        public void NarrationReplacesOldClipsAcrossShortBreaksSkipsAndReplays()
        {
            var host=new GameObject("Muted later narration fixture");
            var narration=new ParticipantTutorialNarration(host,.85f,.5);
            try
            {
                var flow=new ParticipantTutorial();flow.Begin(0);
                // Advance through actual tutorial evidence, never assign a stage.
                while(flow.Stage!=TutorialStage.FirstReady)
                {
                    narration.Refresh(flow);
                    if(flow.Step.Automatic){flow.Tick(60,60);continue;}
                    switch(flow.Stage)
                    {
                        case TutorialStage.HeadCalibration:
                            flow.Tick(60,60);
                            if(!flow.HeadSequenceComplete)flow.ConfirmHeadPose(flow.Revision);
                            else flow.AcceptCalibration(flow.Revision);break;
                        case TutorialStage.GunCalibration:
                            flow.Tick(60,60);
                            flow.AcceptCalibration(flow.Revision);break;
                        case TutorialStage.Fire:
                            flow.Tick(60,60);flow.ObserveFire();narration.Refresh(flow);
                            Assert.That(flow.Stage,Is.EqualTo(TutorialStage.FireConfirmed));
                            AssertCurrentSlot(narration,flow);
                            break;
                        case TutorialStage.Reload:flow.Tick(60,60);flow.ObserveReload();break;
                        case TutorialStage.PracticeReady:flow.Tick(60,60);flow.PreparePractice(flow.Revision);break;
                        case TutorialStage.PracticeIntroduction:
                            Assert.That(narration.CurrentClip.name,Is.EqualTo("05-01-practice-running"));
                            Assert.Throws<InvalidOperationException>(()=>flow.PracticeStarted(flow.Revision));
                            flow.Tick(60,60);flow.PracticeStarted(flow.Revision);narration.Refresh(flow);
                            Assert.That(narration.CurrentClip,Is.Null,"running practice must not replay its introduction");
                            flow.PracticeFinished();break;
                        case TutorialStage.Demonstration:
                            double cue=narration.Duration(flow,7);
                            flow.Tick(cue-.01,cue);Assert.That(flow.CanRequestDemo(cue),Is.False);
                            flow.Tick(.02,cue);Assert.That(flow.CanRequestDemo(cue),Is.True);
                            flow.MarkDemoRequested();flow.DemonstrationEnded(true);break;
                        default:flow.Tick(60,60);flow.Continue(flow.Revision);break;
                    }
                }
                narration.Refresh(flow);Assert.That(narration.CurrentClip.name,Is.EqualTo("09-ready-for-first-scenario"));
                flow.ObserveSession(SessionState.BlockReady,true,"A","A1",false,false,false);
                narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                foreach(string condition in new[]{"A","B","C","D"})
                {
                    bool final=condition=="D";
                    flow.ObserveSession(SessionState.BlockRunning,false,condition,condition+"1",false,false,final);
                    narration.Refresh(flow);Assert.That(narration.CurrentClip,Is.Null,"no stale instruction during a scenario");
                    flow.ObserveSession(SessionState.BlockEnded,false,condition,condition+"1",false,false,final);
                    narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                    Assert.That(narration.Duration(flow,7),Is.EqualTo(narration.CurrentClip.length+.5).Within(.001));
                    flow.ObserveSession(SessionState.Break,false,condition,condition+"1",false,false,final);
                    narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                    var breakClip=narration.CurrentClip;
                    narration.Refresh(flow);Assert.That(narration.CurrentClip,Is.SameAs(breakClip),"refresh must not restart the neutral break cue");
                    Assert.That(narration.HasClip(flow),Is.True);
                    flow.ObserveSession(final?SessionState.SessionComplete:SessionState.BlockReady,false,condition,condition+"1",false,false,final);
                    narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                }
                flow.Tick(60,60);flow.ConfirmForm(flow.Revision);narration.Refresh(flow);
                Assert.That(narration.CurrentClip.name,Is.EqualTo("11-03-thank-you"));
                // Reuse the completed flow for stopped intermediate attempts and skipped breaks.
                flow.ObserveSession(SessionState.BlockEnded,false,"A","A2",true,false,false);
                narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                flow.MarkBreakSkipped();flow.ObserveSession(SessionState.BlockReady,false,"B","B2",false,false,false);
                narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                flow.ObserveSession(SessionState.SessionComplete,false,"D","D2",false,false,true);
                narration.Refresh(flow);AssertCurrentSlot(narration,flow);
                flow.ObserveSession(SessionState.Aborted,false,"","",false,false,true);
                narration.Refresh(flow);Assert.That(narration.CurrentClip,Is.Null);
            }
            finally{narration.Dispose();UnityEngine.Object.DestroyImmediate(host);}
        }
        private static void AssertCurrentSlot(ParticipantTutorialNarration narration,ParticipantTutorial flow)
        {
            var slots=JObject.Parse(Resources.Load<TextAsset>("ELTS/ParticipantTutorialAudio").text);
            string path=(string)slots[flow.Step.Script];
            Assert.That(narration.CurrentClip,Is.SameAs(path==null?null:Resources.Load<AudioClip>(path)),flow.Step.Script);
        }

        [UnityTest]
        public IEnumerator PracticeTargetShowsHitsFallsAndRespawnsAfterItsDelay()
        {
            var host=new GameObject("Practice target response fixture");
            var view=host.AddComponent<DevelopmentView>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            var events=new List<SessionEvent>();
            TutorialInteraction interaction=null;
            RenderTexture capture=null;
            string directory=Environment.GetEnvironmentVariable("ELTS_TUTORIAL_CAPTURE_DIR");
            try
            {
                yield return null;Assert.That(view.Ready,Is.True,view.Error);view.ParticipantOnly=true;view.enabled=false;
                var config=view.Configuration;var screen=config.Rig.Display;
                var eye=screen.Origin+screen.U*(screen.Width*.5)+screen.V*(screen.Height*.5)-screen.Normal*2;
                var head=new RigidPose(eye-config.Rig.HeadEyeOffsetM,Quaterniond.Identity);
                interaction=new TutorialInteraction(config,clock,new SessionEventSequence(),events.Add,20,"Practice",1,15,hitGeometry:view);
                string originalId=interaction.Positions.Keys.Single();
                var center=interaction.Positions.Values.Single();
                var bodyPoint=HumanoidGeometry.Parts(center,config.Rig.VirtualFloorY).First(part=>part.Region=="body").Center;
                Vector3 Unity(Vector3d value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
                var aiming=Quaternion.FromToRotation(Unity(config.Rig.WeaponZero.Rotate(config.Rig.WeaponBoreLocalDirection)),Unity(bodyPoint-eye));
                var rotation=new Quaterniond(aiming.x,aiming.y,aiming.z,aiming.w);
                var weapon=new RigidPose(eye-rotation.Rotate(config.Rig.WeaponMuzzleOffsetM),rotation);
                view.SetTutorialPresentation(true,"#23313F","#68717B",15);
                if(!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);capture=new RenderTexture(1920,1080,24);capture.Create();
                    view.ParticipantCamera.targetTexture=capture;
                }
                void Present(){view.SetSessionFrame(head,weapon,interaction.Positions,0,interaction.FixedPoses);view.Refresh(0);}
                Present();yield return null;
                var original=host.transform.Find(originalId);
                Assert.That(original,Is.Not.Null);
                var rootPosition=original.position;var initialRotation=original.GetChild(0).localRotation;
                if(capture!=null)SaveCapture(capture,Path.Combine(directory,"Practice-room.png"));
                Assert.That(interaction.Fire(weapon,head,clock.Now),Is.True);
                Assert.That(interaction.LastImpact.HasValue,Is.True);
                Assert.That(interaction.LastImpact.Value.Killed,Is.False,"one body hit should retain the original target");
                view.ShowTargetImpact(interaction.LastImpact.Value);Present();
                yield return new WaitForSecondsRealtime(.08f);
                var spark=original.GetChild(0).Find("Confirmed target hit");
                Assert.That(spark,Is.Not.Null,"impact must follow the target model");
                var streak=spark.GetComponentInChildren<LineRenderer>();
                Assert.That(Vector3.Distance(streak.GetPosition(0),streak.GetPosition(1)),Is.LessThan(.13f));
                Assert.That(Quaternion.Angle(initialRotation,original.GetChild(0).localRotation),Is.GreaterThan(1),"body hit should visibly recoil");
                Assert.That(original.position,Is.EqualTo(rootPosition),"visual recoil must not move the authoritative root");
                if(capture!=null)SaveCapture(capture,Path.Combine(directory,"Practice-hit.png"));
                clock.Advance(TimeSpan.FromSeconds(1));
                Assert.That(interaction.Fire(weapon,head,clock.Now),Is.True);
                Assert.That(interaction.LastImpact.Value.Killed,Is.True);
                Assert.That(interaction.Positions,Is.Empty,"dead targets must immediately leave hit testing");
                view.ShowTargetImpact(interaction.LastImpact.Value);Present();
                interaction.Tick(.5);Assert.That(interaction.Positions,Is.Empty,"practice must not instantly replace the dead target");
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(original,Is.Not.Null,"death animation should remain visible briefly");
                Assert.That(Quaternion.Angle(initialRotation,original.GetChild(0).localRotation),Is.GreaterThan(30),"the target must visibly fall");
                if(capture!=null)SaveCapture(capture,Path.Combine(directory,"Practice-defeated.png"));
                yield return new WaitForSecondsRealtime(.7f);
                Assert.That(original==null,Is.True,"completed corpse presentation must be cleaned up");
                interaction.Tick(.61);Present();yield return null;
                Assert.That(interaction.Positions.Count,Is.EqualTo(1));
                Assert.That(interaction.Positions.Keys.Single(),Is.Not.EqualTo(originalId));
                Assert.That(events.Count(item=>item.EventType=="PracticeTargetHit"),Is.EqualTo(2));
                Assert.That(events.Count(item=>item.EventType=="PracticeTargetDestroyed"),Is.EqualTo(1));
                Assert.That(events.Where(item=>item.EventType=="PracticeShotFired")
                    .All(item=>item.Fields.Any(field=>field.Name=="hitGeometry" && field.Text==view.Version)),Is.True);
                Assert.That(events.Any(item=>item.EventType=="ShotFired" || item.EventType=="TargetHit"),Is.False,"practice cannot write scored hit events");
            }
            finally
            {
                interaction?.End();
                if(capture!=null){view.ParticipantCamera.targetTexture=null;capture.Release();UnityEngine.Object.DestroyImmediate(capture);}
                UnityEngine.Object.DestroyImmediate(host);
            }
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
            var capturedHead=new HashSet<int>();
            try
            {
                if(!string.IsNullOrEmpty(captureDirectory))
                {
                    Directory.CreateDirectory(captureDirectory);
                    bool compact=Environment.GetEnvironmentVariable("ELTS_TUTORIAL_CAPTURE_COMPACT")=="1";
                    capture=new RenderTexture(compact?1280:1920,compact?720:1080,24);capture.Create();
                    // Render the real scene beneath transparent teaching cues.
                    // A UI-only texture otherwise retains the previous slide.
                    view.ParticipantCamera.targetTexture=capture;panel.targetTexture=capture;
                }
                for(int i=0;i<5;i++)yield return null;
                // Captures pause the tutorial to freeze each screen; hide the
                // resulting "Paused" chip so the images show what participants see.
                if(capture!=null)
                    foreach(var document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
                    { var chip=document.rootVisualElement.Q("tutorialPaused");if(chip!=null)chip.style.visibility=Visibility.Hidden; }
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
                bool testedHead=false,testedFire=false,testedReload=false,testedPractice=false,testedDemo=false,testedPracticeIntro=false;
                deadline=Time.realtimeSinceStartup+100;
                while(session.Tutorial.Stage!=TutorialStage.FirstReady && Time.realtimeSinceStartup<deadline)
                {
                    var stage=session.Tutorial.Stage;
                    if(capture!=null && stage==TutorialStage.HeadCalibration && !session.Tutorial.HeadSequenceComplete &&
                        capturedHead.Add(session.Tutorial.HeadCalibrationIndex))
                    {
                        // Each of the seven directions has its own dot and pictogram.
                        session.Tutorial.Tick(5,60);
                        for(int frame=0;frame<6;frame++)yield return null;
                        SaveCapture(capture,Path.Combine(captureDirectory,"HeadDirection-"+(session.Tutorial.HeadCalibrationIndex+1)+".png"));
                    }
                    if(capture!=null && !captured.Contains(stage))
                    {
                        bool wasPaused=session.Tutorial.Paused;
                        // Preserve preparation cues as well as the settled dot.
                        // Capture every lesson so repeated artwork is reviewable.
                        if(stage==TutorialStage.HeadCalibration || stage==TutorialStage.GunCalibration)
                        {
                            for(int frame=0;frame<3;frame++)yield return null;
                            SaveCapture(capture,Path.Combine(captureDirectory,stage+"-preparation.png"));
                        }
                        if(stage==TutorialStage.Holding)
                        {
                            Command(station,session,"tutorialPause");
                            for(int frame=0;frame<4;frame++)yield return null;
                            SaveCapture(capture,Path.Combine(captureDirectory,"Holding-start.png"));
                            session.TutorialSettings.reducedMotion=true;
                            for(int frame=0;frame<4;frame++)yield return null;
                            SaveCapture(capture,Path.Combine(captureDirectory,"Holding-reduced-motion.png"));
                            session.TutorialSettings.reducedMotion=false;
                            if(!wasPaused)Command(station,session,"tutorialResume");
                        }
                        // Freeze progression only after the artwork has settled.
                        session.Tutorial.Tick(5,60);
                        Command(station,session,"tutorialPause");
                        for(int frame=0;frame<8;frame++)yield return null;
                        SaveCapture(capture,Path.Combine(captureDirectory,stage+".png"));captured.Add(stage);
                        if(!session.Tutorial.Step.Automatic && session.Tutorial.Step.Script.Length>0)
                        {
                            // Also review the held screen once its cue badge
                            // changes from Listen to the participant's action.
                            Command(station,session,"tutorialResume");session.Tutorial.Tick(0,0);
                            Command(station,session,"tutorialPause");
                            for(int frame=0;frame<8;frame++)yield return null;
                            SaveCapture(capture,Path.Combine(captureDirectory,stage+"-action.png"));
                        }
                        if(!wasPaused)Command(station,session,"tutorialResume");
                    }
                    if(stage==TutorialStage.HeadCalibration && session.Tutorial.HeadSequenceComplete && !testedHead)
                    {
                        int old=session.Tutorial.Revision;Command(station,session,"tutorialRedo");
                        Assert.Throws<InvalidOperationException>(()=>station.ApplyCommand(new JObject{["action"]="tutorialAccept",["tutorialRevision"]=old}));
                        testedHead=true;
                    }
                    if(stage==TutorialStage.Fire && !testedFire)
                    {
                        Assert.Throws<InvalidOperationException>(()=>Command(station,session,"tutorialContinue"));
                        if(!session.Tutorial.CueFinished)Assert.That(session.StationParticipantTrigger(),Is.False,"early firing cannot bypass the instruction");
                        FinishInstruction(session);
                        Assert.That(session.StationParticipantTrigger(),Is.True,"a real recorded synthetic trigger is accepted for teaching");
                        Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.FireConfirmed));
                        Assert.That(session.StationShots,Is.Zero,"tutorial shots never change scored counters");
                        testedFire=true;
                    }
                    if(stage==TutorialStage.PracticeIntroduction && !testedPracticeIntro)
                    {
                        Assert.That(session.Engine.State,Is.EqualTo(SessionState.Calibration),"practice clock must wait for the running instruction");
                        Assert.That(session.StationParticipantTrigger(),Is.False);
                        clock.Advance(TimeSpan.FromSeconds(20));yield return null;
                        Assert.That(session.Engine.State,Is.EqualTo(SessionState.Calibration));
                        testedPracticeIntro=true;
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
                        Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.PracticeIntroduction));
                        Assert.That(session.Engine.PracticePaused,Is.True);
                        Command(station,session,"tutorialPause");Command(station,session,"tutorialResume");
                        Assert.That(session.Engine.PracticePaused,Is.True,"resuming the introduction must not resume practice time");
                        FinishInstruction(session);yield return null;
                        Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(10));
                        Assert.That(session.Tutorial.HeadAccepted && session.Tutorial.GunAccepted,Is.True);
                        testedPractice=true;
                    }
                    if(stage==TutorialStage.Reload && !testedReload)
                    {
                        FinishInstruction(session);
                        input.ReloadOnce=true;yield return null;
                        Assert.That(session.Tutorial.Reloaded,Is.True,"the participant input frame must satisfy the reload check");
                        Assert.That(session.ParticipantAmmo,Is.EqualTo(session.StationMagazineCapacity));
                        testedReload=true;
                    }
                    if(stage==TutorialStage.Demonstration && session.Tutorial.DemoRequested && !testedDemo)
                    {
                        Command(station,session,"tutorialReplay");Assert.That(session.Tutorial.DemoAttempt,Is.EqualTo(1));testedDemo=true;
                    }
                    if(stage==session.Tutorial.Stage)AdvanceWalkthrough(session,station,oneStep:capture!=null);
                    clock.Advance(TimeSpan.FromSeconds(.1));yield return null;
                }
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.FirstReady),session.StationMessage);
                Assert.That(session.StationCanArm,Is.False,"the full readiness cue must finish before arming");
                Assert.That(view.TutorialSceneVisible,Is.False,"the scenario scenery must return before arming");
                Assert.That(testedHead && testedFire && testedReload && testedPractice && testedPracticeIntro && testedDemo,Is.True);
                Assert.That(Directory.GetFiles(session.StationRecordingPath,"synthetic-calibration-*.json").Length,Is.EqualTo(2));
                Assert.That(session.Tutorial.DemoAttempt,Is.EqualTo(1));
                string run=session.StationRecordingPath;
                yield return null;
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.FirstReady),"first readiness announcement must finish before arming");
                session.Tutorial.Tick(60,60);
                yield return null;
                Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.Armed));
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                Assert.That(session.StationParticipantTrigger(),Is.False,"an early start trigger cannot cut off the armed instruction");
                FinishInstruction(session);
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
                    yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded),"completion narration must finish before starting the break");
                    session.Tutorial.Tick(60,60);
                    yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break));
                    Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(2));
                    if(capture!=null && i==3)
                    {for(int frame=0;frame<8;frame++)yield return null;SaveCapture(capture,Path.Combine(captureDirectory,"FinalBreak.png"));}
                    clock.Advance(TimeSpan.FromSeconds(2));
                    yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.Break),"a short break must let its narration finish");
                    FinishInstruction(session);yield return null;
                    if(i<3)
                    {
                        Assert.That(session.Tutorial.Stage,Is.EqualTo(TutorialStage.NextReady));
                        yield return null;
                        Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"next-scenario announcement must finish before starting");
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
                FinishInstruction(session);
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
                if(view!=null && view.ParticipantCamera!=null)view.ParticipantCamera.targetTexture=null;
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
