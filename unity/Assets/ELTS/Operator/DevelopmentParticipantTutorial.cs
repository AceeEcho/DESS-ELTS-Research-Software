#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Calibration;
using Elts.Logging;
using Elts.Session;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Connects tutorial checks to the existing recording, calibration,
    /// input and session owners. Participant presentation cannot call these controls.</summary>
    public sealed partial class DevelopmentSessionPanel
    {
        private ParticipantTutorial tutorial=new ParticipantTutorial();
        private ParticipantTutorialSettings tutorialSettings=new ParticipantTutorialSettings();
        private TutorialInteraction? tutorialInteraction;
        private TutorialDemonstration? tutorialDemo;
        private ParticipantTutorialNarration? tutorialNarration;
        private readonly List<object> tutorialHistory=new List<object>();
        private int tutorialPracticeAttempt;
        private TutorialStage previousTutorialStage;
        private bool tutorialDotVisible;
        public ParticipantTutorial Tutorial=>tutorial;
        public ParticipantTutorialSettings TutorialSettings=>tutorialSettings;
        private bool TutorialHasNarration=>tutorialNarration?.HasClip(tutorial,stationBreakSeconds==120)==true;
        private double TutorialCueSeconds=>tutorialNarration?.Duration(tutorial,tutorialSettings.Duration(tutorial.Step),stationBreakSeconds==120)
            ?? tutorialSettings.Duration(tutorial.Step);
        public bool TutorialDotVisible=>tutorialDotVisible;
        public bool TutorialCanInteract=>SeparateAdministrator && !IsBusy && !closing && tutorial.CanFire &&
            tutorialInteraction!=null && (engine?.State!=SessionState.Practice || !engine.PracticePaused);
        public string ParticipantInputPhase=>tutorial.LiveScene?"tutorial-"+tutorial.Stage+"-"+tutorial.Revision:DesktopBlockId??"";
        public int ParticipantAmmo=>tutorial.LiveScene? tutorialInteraction?.Ammo??StationMagazineCapacity:StationAmmo;
        public Elts.Geometry.Vector3d? TutorialTargetPosition=>tutorialInteraction?.Positions.Count>0?tutorialInteraction.Positions.First().Value:(Elts.Geometry.Vector3d?)null;

        private void InitializeTutorial()
        {
            var source=Resources.Load<TextAsset>("ELTS/ParticipantTutorialSettings");
            if(source==null)throw new InvalidOperationException("Tutorial presentation settings are missing.");
            tutorialSettings=JsonConvert.DeserializeObject<ParticipantTutorialSettings>(source.text)
                ?? throw new InvalidOperationException("Tutorial presentation settings are invalid.");
            tutorialSettings.Validate();
            if(tutorialSettings.narrationEnabled)
            {
                tutorialNarration=new ParticipantTutorialNarration(gameObject,tutorialSettings.narrationVolume,tutorialSettings.narrationGapSeconds);
                if(FindFirstObjectByType<AudioListener>()==null)view.ParticipantCamera.gameObject.AddComponent<AudioListener>();
            }
        }
        private void ResetTutorial()
        {
            tutorialDemo?.Stop();tutorialDemo=null;tutorialInteraction=null;
            tutorialNarration?.Stop();
            tutorial=new ParticipantTutorial(RecordTutorialChange);
            tutorialHistory.Clear();tutorialPracticeAttempt=0;previousTutorialStage=TutorialStage.NotStarted;tutorialDotVisible=false;
        }
        private void RecordTutorialChange(string name,TutorialStage stage)
        {
            var entry=new {eventType=name,stage=stage.ToString(),utc=DateTimeOffset.UtcNow.ToString("O"),
                monotonicTicks=clock?.Now.Ticks??0,synthetic=true};
            // A completed sensor recording stays closed during the form. The
            // associated completion receipt below retains the post-closure steps.
            if(recording?.IsOpen==true && !closing)
                RecordTutorialEvent(new SessionEvent(sequence!.Next(),clock!.Now,name,new[]{
                    LogField.String("tutorialStage",stage.ToString()),LogField.Boolean("synthetic",true),
                    LogField.Boolean("audioEnabled",tutorialNarration!=null)}));
            tutorialHistory.Add(entry);
        }
        private void RecordTutorialEvent(SessionEvent value)
        {
            if(recording?.IsOpen!=true || !recording.TryRecord(value))
            {
                engine?.Abort("Tutorial event could not be recorded.");
                throw new IOException("The tutorial action could not be saved. The session has stopped.");
            }
        }

        private bool TutorialCalibrationStep=>tutorial.Stage==TutorialStage.HeadCalibration || tutorial.Stage==TutorialStage.GunCalibration;
        private bool TutorialCanBegin=>!IsBusy && !closing && engine?.State==SessionState.Calibration && !tutorial.Started;
        private bool TutorialCanCapture=>!IsBusy && !tutorial.Paused && TutorialCalibrationStep && tutorialDotVisible && calibration.State==CalibrationWizardState.Idle;
        private bool TutorialCanAccept=>!IsBusy && !tutorial.Paused && TutorialCalibrationStep && calibration.State==CalibrationWizardState.Review && calibrationWithinLimits;
        private string TutorialPrimaryAction => !tutorial.Started?"tutorialBegin":TutorialCalibrationStep?
            (calibration.State==CalibrationWizardState.Idle?"tutorialCapture":"tutorialAccept"):
            tutorial.Stage==TutorialStage.PracticeReady?"tutorialPractice":tutorial.Stage==TutorialStage.Form?"tutorialForm":"tutorialContinue";
        private string TutorialPrimaryLabel => !tutorial.Started?"Begin tutorial / walkthrough":TutorialCalibrationStep?
            (calibration.State==CalibrationWizardState.Idle?"Capture synthetic "+(tutorial.Stage==TutorialStage.HeadCalibration?"head":"gun")+" fixture":
                "Accept review · continue"):tutorial.Step.OperatorAction;
        private bool TutorialPrimaryEnabled => !IsBusy && (TutorialCanBegin || TutorialCanCapture || TutorialCanAccept ||
            tutorial.CanContinue || (!tutorial.Paused && tutorial.Stage==TutorialStage.PracticeReady) ||
            (tutorial.Stage==TutorialStage.Form && engine?.State==SessionState.SessionComplete && recording?.IsOpen!=true && !stationSaveStatus.StartsWith("SAVE FAILED",StringComparison.Ordinal)));
        public object TutorialState=>new {
            started=tutorial.Started,stage=tutorial.Stage.ToString(),step=tutorial.Step.Script,title=tutorial.Step.Title,
            instruction=tutorial.Step.Instruction,revision=tutorial.Revision,paused=tutorial.Paused,
            primaryAction=TutorialPrimaryAction,primaryLabel=TutorialPrimaryLabel,canPrimary=TutorialPrimaryEnabled,
            canPause=!IsBusy && tutorial.Started && tutorial.Stage<TutorialStage.FirstReady && !tutorial.Paused,
            canResume=!IsBusy && tutorial.Paused,canReplay=!IsBusy && tutorial.Started && tutorial.Stage!=TutorialStage.Running,
            canRedo=!IsBusy && !tutorial.Paused && TutorialCalibrationStep && calibration.State==CalibrationWizardState.Review,
            canRepeatDemo=!IsBusy && !tutorial.Paused && (tutorial.DemoFinished || tutorial.DemoFailed) &&
                (tutorial.Stage==TutorialStage.Demonstration || tutorial.Stage==TutorialStage.DemonstrationComplete),
            demoActive=tutorialDemo?.Active==true,demoFailed=tutorial.DemoFailed,
            calibrationReview=TutorialCalibrationStep && calibration.State==CalibrationWizardState.Review?root.Q<Label>("calibrationReview").text:"",
            headAccepted=tutorial.HeadAccepted,gunAccepted=tutorial.GunAccepted,fired=tutorial.Fired,reloaded=tutorial.Reloaded,
            canRestartPractice=!IsBusy && !tutorial.Paused && (tutorial.Stage==TutorialStage.PracticeRunning || tutorial.Stage==TutorialStage.PracticeComplete),
            canFinishPractice=!IsBusy && tutorial.Stage==TutorialStage.PracticeRunning,
            practicePaused=engine?.PracticePaused==true,audioEnabled=tutorialNarration!=null,
            audioAvailable=TutorialHasNarration,audioSlot=tutorial.Step.AudioSlot,
            status=TutorialOperatorStatus()
        };
        private string TutorialOperatorStatus()
        {
            if(!tutorial.Started)return "Start the walkthrough after the participant recording is ready.";
            if(tutorial.Paused)return engine?.State==SessionState.Practice?"Practice paused. Recording continues.":"Instruction paused. Resume when ready.";
            if(TutorialCalibrationStep)return "Synthetic fixture review only; this does not calibrate physical equipment.";
            if(tutorial.Stage==TutorialStage.Fire)return tutorial.Fired?"Firing observed. Confirm understanding to continue.":"Waiting for a participant shot. A hit is not required.";
            if(tutorial.Stage==TutorialStage.Reload)return tutorial.Reloaded?"Reload observed. Confirm understanding to continue.":"Waiting for a reload. If the magazine is full, fire once first.";
            if(tutorial.Stage==TutorialStage.Demonstration)return tutorial.DemoFailed?"Simulation interrupted. Explicitly repeat the demonstration when ready.":
                tutorialDemo?.Active==true?"Simulated lifecycle active. No hardware outputs.":"The arrow cue will request one simulated demonstration. No hardware outputs.";
            if(tutorial.Stage==TutorialStage.Form)return "Provide the approved form. Confirm only after completion; a separate receipt retains this participant/run association.";
            if(tutorial.Step.Automatic)return TutorialHasNarration?
                "Explanation advances after narration. Pause or repeat as needed.":"Text-only explanation advances automatically. Pause or repeat as needed.";
            return (TutorialHasNarration?"Selected narration connected. ":"Text-only instruction for the current session state. ")+
                (tutorial.CanContinue?"Confirm the participant's action before continuing.":"Waiting for the current session action.");
        }

        public void StationTutorialAction(string action,int revision)
        {
            if(IsBusy || engine==null)throw new InvalidOperationException("Wait for the participant recording to finish preparing or saving.");
            tutorial.RequireRevision(revision);
            if(engine.State==SessionState.Aborted || engine.State==SessionState.Failed)
                throw new InvalidOperationException("This session has stopped. Create a new participant recording to continue.");
            switch(action)
            {
                case "tutorialBegin":
                    if(!TutorialCanBegin)throw new InvalidOperationException("The participant recording is not ready for the walkthrough.");
                    tutorial.Begin(revision);break;
                case "tutorialContinue":tutorial.Continue(revision);break;
                case "tutorialCapture":
                    if(!TutorialCanCapture)throw new InvalidOperationException("Wait for the stationary dot, then capture this calibration fixture.");
                    CaptureCalibrationFixture();RecordTutorialChange("TutorialFixtureCaptured",tutorial.Stage);tutorial.InvalidateControls();break;
                case "tutorialRedo":
                    if(!TutorialCalibrationStep || tutorial.Paused || calibration.State!=CalibrationWizardState.Review)
                        throw new InvalidOperationException("Only the current unaccepted calibration review can be redone.");
                    ResetCalibration();RecordTutorialChange("TutorialFixtureDiscarded",tutorial.Stage);tutorial.InvalidateControls();break;
                case "tutorialAccept":
                    if(!TutorialCanAccept)throw new InvalidOperationException("Capture and review a fixture within the development limits first.");
                    AcceptCalibrationFixture();tutorial.AcceptCalibration(revision);break;
                case "tutorialPractice":
                    if(tutorial.Stage!=TutorialStage.PracticeReady || tutorial.Paused)throw new InvalidOperationException("Finish the controls walkthrough first.");
                    AdvanceWithCalibration();tutorial.PracticeStarted(revision);break;
                case "tutorialRestartPractice":
                    if(tutorial.Paused || (tutorial.Stage!=TutorialStage.PracticeRunning && tutorial.Stage!=TutorialStage.PracticeComplete))
                        throw new InvalidOperationException("Practice can restart from the practice steps only.");
                    EndTutorialInteraction();engine.RestartDevelopmentPractice();tutorial.PracticeStarted(revision);break;
                case "tutorialFinishPractice":
                    if(tutorial.Stage!=TutorialStage.PracticeRunning)throw new InvalidOperationException("Practice is not running.");
                    EndTutorialInteraction();engine.FinishDevelopmentPractice();tutorial.PracticeFinished();break;
                case "tutorialPause":
                    tutorial.SetPaused(true,revision);
                    if(engine.State==SessionState.Practice)engine.PauseDevelopmentPractice();
                    if(tutorialDemo?.Active==true){tutorialDemo.Stop();tutorial.DemonstrationEnded(false);}break;
                case "tutorialResume":
                    tutorial.SetPaused(false,revision);
                    if(engine.State==SessionState.Practice)engine.ResumeDevelopmentPractice();break;
                case "tutorialReplay":tutorial.Replay(revision);break;
                case "tutorialStopDemo":
                    if(tutorialDemo?.Active!=true)throw new InvalidOperationException("No simulated demonstration is running.");
                    tutorialDemo.Stop();tutorial.DemonstrationEnded(false);break;
                case "tutorialRepeatDemo":tutorial.RepeatDemonstration(revision);break;
                case "tutorialForm":ConfirmTutorialForm(revision);break;
                default:throw new ArgumentException("Unknown tutorial control.");
            }
            if(engine.State==SessionState.Failed)throw new IOException(engine.Failure);
            SynchronizeTutorial();
        }
        private void ConfirmTutorialForm(int revision)
        {
            if(tutorial.Stage!=TutorialStage.Form || engine?.State!=SessionState.SessionComplete || recording?.IsOpen==true || !TutorialPrimaryEnabled)
                throw new InvalidOperationException("Wait for the final break and recording closure, then confirm the completed form.");
            // Supplementary association, never a questionnaire result or a rewrite
            // of a closed raw sensor stream. Create-new preserves earlier receipts.
            var receipt=new {schemaVersion="elts.tutorial-completion.v1",participantId=stationParticipant,runId,
                recording=Path.GetFileName(recording!.RunDirectory),confirmedUtc=DateTimeOffset.UtcNow.ToString("O"),
                formCompletionSource="operator-confirmed",synthetic=true,audioEnabled=tutorialNarration!=null,
                allFourScenariosCompleted=tutorial.AllScenariosCompleted,history=tutorialHistory};
            string path=Path.Combine(recording.RunDirectory!,"tutorial-completion-"+Guid.NewGuid().ToString("N")+".json");
            PublishReviewFile(path,JsonConvert.SerializeObject(receipt,Formatting.Indented));
            tutorial.ConfirmForm(revision);
        }

        private void TickTutorial(double deltaSeconds)
        {
            if(!SeparateAdministrator || engine==null || IsBusy)return;
            try
            {
                SynchronizeTutorial();
                tutorial.Tick(deltaSeconds,TutorialCueSeconds);
                SynchronizeTutorial();
                if(TutorialCalibrationStep && tutorial.ElapsedSeconds>=tutorialSettings.calibrationIllustrationSeconds)tutorialDotVisible=true;
                if(tutorial.CanRequestDemo(TutorialCueSeconds))
                {
                    tutorial.MarkDemoRequested();
                    tutorialDemo??=new TutorialDemonstration(clock!,command=>RecordTutorialChange("TutorialSimulatedElts"+command,tutorial.Stage));
                    try { tutorialDemo.Start(stationParticipant,tutorial.DemoAttempt,tutorialSettings.simulatedDemoSeconds); }
                    catch { tutorialDemo.Stop();tutorial.DemonstrationEnded(false);throw; }
                }
                var result=tutorialDemo?.Tick();
                if(result.HasValue)tutorial.DemonstrationEnded(result.Value);
                AdvanceAutomaticStudy();
            }
            catch(Exception error)
            {
                lastMessage=error.Message;
                try{tutorialDemo?.Stop();}finally{engine.Abort("Tutorial failure: "+error.Message);}
            }
        }
        internal void SynchronizeTutorial()
        {
            if(engine==null)return;
            tutorial.ObserveSession(engine.State,TestArmed,engine.CurrentCondition??"",engine.CurrentBlockId??"",
                engine.CurrentBlockStoppedEarly,engine.CurrentBlockSkipped,engine.CurrentBlockIndex==engine.ScheduledConditions.Count-1);
            tutorialNarration?.Refresh(tutorial, stationBreakSeconds == 120);
            if(previousTutorialStage!=tutorial.Stage)
            {
                if(tutorial.Stage==TutorialStage.GunCalibration)ResetCalibration();
                if(TutorialCalibrationStep)tutorialDotVisible=false;
                previousTutorialStage=tutorial.Stage;
            }
            if((engine.State==SessionState.Aborted || engine.State==SessionState.Failed) && tutorialDemo?.Active==true)tutorialDemo.Stop();
            bool live=tutorial.LiveScene && !closing;
            view.SetTutorialPresentation(live,tutorialSettings.sceneBackgroundColor,tutorialSettings.sceneFloorColor);
            string scope=tutorial.Stage==TutorialStage.PracticeRunning?"Practice":"Tutorial";
            if(tutorialInteraction!=null && (!live || tutorialInteraction.Scope!=scope))EndTutorialInteraction();
            if(live && tutorialInteraction==null)
                tutorialInteraction=new TutorialInteraction(view.Configuration,clock!,sequence!,RecordTutorialEvent,
                    StationMagazineCapacity,scope,++tutorialPracticeAttempt,tutorialSettings.targetDistanceM);
        }
        private void EndTutorialInteraction()
        { try{tutorialInteraction?.End();}finally{tutorialInteraction=null;} }
        private void PresentTutorialInteraction()
        {
            if(!SeparateAdministrator || tutorialInteraction==null || !tutorial.LiveScene)return;
            var pair=acquisition?.Snapshot;
            view.SetSessionFrame(pair?.Head.Pose,pair?.Weapon.Pose,tutorialInteraction.Positions,0,tutorialInteraction.FixedPoses);
        }
        private bool SubmitTutorialTrigger()
        {
            if(!TutorialCanInteract || acquisition==null || !acquisition.TryCaptureForTrigger(out var pair) || !pair.IsValid)return false;
            RecordTutorialEvent(new SessionEvent(sequence!.Next(),pair.Timestamp,"TutorialInputTrigger",new[]{
                LogField.String("blockId",tutorialInteraction!.InteractionId),LogField.String("scope",tutorialInteraction.Scope),
                LogField.NumberValue("acquisitionSequence",pair.Sequence),LogField.Boolean("scored",false),LogField.String("edge","falling")}));
            bool fired=tutorialInteraction.Fire(pair.Weapon.Pose!.Value,pair.Head.Pose!.Value,pair.Timestamp);
            if(fired)tutorial.ObserveFire();
            PresentTutorialInteraction();return fired;
        }
    }
}
