#nullable enable
using System;
using System.Collections.Generic;

namespace Elts.Session
{
    // Each value is a real participant screen or an explicit interaction hold.
    // Audio is deliberately absent: reading time can advance explanations only.
    public enum TutorialStage
    {
        NotStarted, Welcome, Questions, ReadyForSetup, Equipment, Comfort, EquipmentCheck,
        HeadCalibration, Holding, GunCalibration, CalibrationComplete,
        Targets, Aim, Fire, Reload, PracticeReady, PracticeRunning, PracticeComplete,
        Demonstration, DemonstrationComplete, Scenarios, TimeLimits, BreakOverview,
        FirstReady, Armed, Running, ScenarioPaused, ScenarioEnded, BreakRunning,
        NextReady, FinalEnded, FinalBreak, Form, ThankYou, Interrupted
    }

    public sealed class TutorialStep
    {
        public readonly string Script, Title, Instruction, Visual, OperatorAction;
        public readonly bool Automatic;
        public TutorialStep(string script, string title, string instruction, string visual,
            string operatorAction = "", bool automatic = false)
        { Script=script;Title=title;Instruction=instruction;Visual=visual;OperatorAction=operatorAction;Automatic=automatic; }
        // Stable narration identifier; missing recordings remain optional in the adapter.
        public string AudioSlot => "participant-tutorial/"+Script;
    }

    /// <summary>
    /// Tutorial progression, independent of Unity rendering and audio. The adapter
    /// supplies observed actions; this class never controls a device or session.
    /// Revision checks make double clicks and stale browser commands harmless.
    /// </summary>
    public sealed class ParticipantTutorial
    {
        private readonly Action<string, TutorialStage>? record;
        private readonly HashSet<string> completedConditions = new HashSet<string>(StringComparer.Ordinal);
        private string lastEndedAttempt = "";
        private bool sessionStarted, breakSkipped, abnormalAttempt;
        public TutorialStage Stage { get; private set; }
        public int Revision { get; private set; }
        public int PresentationVersion { get; private set; }
        public bool Paused { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public bool HeadAccepted { get; private set; }
        public bool GunAccepted { get; private set; }
        public bool Fired { get; private set; }
        public bool Reloaded { get; private set; }
        public bool DemoRequested { get; private set; }
        public bool DemoFinished { get; private set; }
        public bool DemoFailed { get; private set; }
        public int DemoAttempt { get; private set; }
        public bool Started => Stage != TutorialStage.NotStarted;
        public bool AllScenariosCompleted => completedConditions.Count == 4;
        public bool LastBreakSkipped => breakSkipped;
        public bool LastAttemptAbnormal => abnormalAttempt;
        public bool CanArm => !Paused && (Stage==TutorialStage.FirstReady || Stage==TutorialStage.NextReady);
        public bool LiveScene => Stage==TutorialStage.Targets || Stage==TutorialStage.Aim || Stage==TutorialStage.Fire ||
            Stage==TutorialStage.Reload || Stage==TutorialStage.PracticeReady || Stage==TutorialStage.PracticeRunning;
        public bool CanFire => !Paused && (Stage==TutorialStage.Fire || Stage==TutorialStage.Reload || Stage==TutorialStage.PracticeRunning);
        public TutorialStep Step => Steps[Stage];

        public ParticipantTutorial(Action<string,TutorialStage>? record = null) { this.record=record; }
        public void RequireRevision(int revision)
        { if(revision!=Revision)throw new InvalidOperationException("The tutorial step changed. Review the current instruction and try again."); }
        public void InvalidateControls() { Revision++; }
        public void Begin(int revision)
        {
            RequireRevision(revision);
            if(Started)throw new InvalidOperationException("This participant's walkthrough has already started.");
            Move(TutorialStage.Welcome,"TutorialStarted");
        }
        public void Tick(double seconds, double explanationSeconds)
        {
            if(seconds<0 || double.IsNaN(seconds) || double.IsInfinity(seconds))throw new ArgumentOutOfRangeException(nameof(seconds));
            if(!Started || Paused)return;
            ElapsedSeconds+=seconds;
            if(Step.Automatic && ElapsedSeconds>=explanationSeconds)Move(Next(Stage),"TutorialExplanationFinished");
        }
        public bool CanContinue => !Paused && (Stage==TutorialStage.ReadyForSetup || Stage==TutorialStage.Equipment ||
            Stage==TutorialStage.EquipmentCheck || Stage==TutorialStage.Holding || Stage==TutorialStage.Aim ||
            (Stage==TutorialStage.Fire && Fired) || (Stage==TutorialStage.Reload && Reloaded) ||
            Stage==TutorialStage.PracticeComplete || Stage==TutorialStage.DemonstrationComplete);
        public void Continue(int revision)
        {
            RequireRevision(revision);
            if(!CanContinue)throw new InvalidOperationException("Complete the current action or check before continuing.");
            Move(Next(Stage),"TutorialOperatorConfirmed");
        }
        public void AcceptCalibration(int revision)
        {
            RequireRevision(revision);
            if(Paused || (Stage!=TutorialStage.HeadCalibration && Stage!=TutorialStage.GunCalibration))
                throw new InvalidOperationException("No tutorial calibration review is waiting.");
            record?.Invoke("TutorialCalibrationAccepted",Stage);
            if(Stage==TutorialStage.HeadCalibration)HeadAccepted=true;
            else { if(!HeadAccepted)throw new InvalidOperationException("Review the head step first.");GunAccepted=true; }
            Move(Next(Stage),"TutorialStepChanged");
        }
        public void ObserveFire()
        {
            if(!CanFire)return;
            if(Stage==TutorialStage.Fire && !Fired) { record?.Invoke("TutorialFiringObserved",Stage);Fired=true; }
        }
        public void ObserveReload()
        {
            if(!CanFire)return;
            // Reloads before the reload instruction cannot satisfy that instruction.
            if(Stage==TutorialStage.Reload && !Reloaded) { record?.Invoke("TutorialReloadObserved",Stage);Reloaded=true; }
        }
        public void PracticeStarted(int revision)
        {
            RequireRevision(revision);
            if(Paused || (Stage!=TutorialStage.PracticeReady && Stage!=TutorialStage.PracticeRunning && Stage!=TutorialStage.PracticeComplete))
                throw new InvalidOperationException("Practice is not available at this step.");
            if(!HeadAccepted || !GunAccepted || !Fired || !Reloaded)throw new InvalidOperationException("Complete setup and the controls demonstration first.");
            Move(TutorialStage.PracticeRunning,"TutorialPracticeStarted",true);
        }
        public void PracticeFinished()
        { if(Stage==TutorialStage.PracticeRunning)Move(TutorialStage.PracticeComplete,"TutorialPracticeFinished"); }
        public bool CanRequestDemo(double explanationSeconds) => Stage==TutorialStage.Demonstration && !Paused &&
            !DemoRequested && ElapsedSeconds>=explanationSeconds;
        public void MarkDemoRequested()
        {
            if(Stage!=TutorialStage.Demonstration || Paused || DemoRequested)throw new InvalidOperationException("The demonstration cue is not available.");
            record?.Invoke("TutorialDemoRequested",Stage);
            DemoAttempt++;DemoRequested=true;DemoFinished=false;DemoFailed=false;
        }
        public void DemonstrationEnded(bool success)
        {
            if(Stage!=TutorialStage.Demonstration || !DemoRequested || DemoFinished || DemoFailed)return;
            record?.Invoke(success?"TutorialDemoEnded":"TutorialDemoFailed",Stage);
            DemoFinished=success;DemoFailed=!success;
            if(success)Move(TutorialStage.DemonstrationComplete,"TutorialStepChanged");
        }
        public void RepeatDemonstration(int revision)
        {
            RequireRevision(revision);
            if(Paused || !DemoRequested || (!DemoFinished && !DemoFailed) ||
                (Stage!=TutorialStage.Demonstration && Stage!=TutorialStage.DemonstrationComplete))
                throw new InvalidOperationException("End the current demonstration before explicitly repeating it.");
            record?.Invoke("TutorialDemoRepeatConfirmed",Stage);
            DemoRequested=false;DemoFinished=false;DemoFailed=false;
            Move(TutorialStage.Demonstration,"TutorialStepChanged",true);
        }
        public void SetPaused(bool paused, int revision)
        {
            RequireRevision(revision);
            if(!Started || Stage>=TutorialStage.FirstReady)throw new InvalidOperationException("Use the current session controls at this step.");
            if(paused==Paused)return;
            record?.Invoke(paused?"TutorialPaused":"TutorialResumed",Stage);
            Paused=paused;Revision++;
        }
        public void Replay(int revision)
        {
            RequireRevision(revision);
            if(!Started || Stage==TutorialStage.Running)throw new InvalidOperationException("There is no instruction to repeat right now.");
            record?.Invoke("TutorialVisualRepeated",Stage);
            // Restart visual pacing only. Evidence, timers, arm state and the
            // one-shot demonstration latch deliberately survive this operation.
            ElapsedSeconds=0;PresentationVersion++;Revision++;
        }
        public void MarkBreakSkipped() { breakSkipped=true; }

        public void ObserveSession(SessionState state, bool armed, string condition, string attempt,
            bool stoppedEarly, bool skipped, bool finalScheduledAttempt)
        {
            if(!Started)return;
            if(state==SessionState.Aborted || state==SessionState.Failed)
            { Move(TutorialStage.Interrupted,"TutorialInterrupted");return; }
            if(Stage==TutorialStage.PracticeRunning && state==SessionState.BlockReady)PracticeFinished();
            if(Stage<TutorialStage.FirstReady || Stage==TutorialStage.Interrupted)return;
            if(state==SessionState.BlockReady)
            {
                if(armed)Move(TutorialStage.Armed,"TutorialScenarioArmed");
                else if(Stage==TutorialStage.Armed || Stage==TutorialStage.BreakRunning || Stage==TutorialStage.FinalBreak ||
                    Stage==TutorialStage.ScenarioEnded || Stage==TutorialStage.FinalEnded || Stage==TutorialStage.Form || Stage==TutorialStage.ThankYou)
                    Move(sessionStarted?TutorialStage.NextReady:TutorialStage.FirstReady,"TutorialWaitingForReadiness");
            }
            else if(state==SessionState.BlockRunning)
            { sessionStarted=true;Move(TutorialStage.Running,"TutorialScenarioStarted"); }
            else if(state==SessionState.BlockPaused)Move(TutorialStage.ScenarioPaused,"TutorialScenarioPaused");
            else if(state==SessionState.BlockEnded)
            {
                if(lastEndedAttempt!=attempt)
                {
                    abnormalAttempt=stoppedEarly || skipped;breakSkipped=false;
                    if(!abnormalAttempt)completedConditions.Add(condition);
                    lastEndedAttempt=attempt;
                }
                Move(finalScheduledAttempt?TutorialStage.FinalEnded:TutorialStage.ScenarioEnded,"TutorialScenarioEnded");
            }
            else if(state==SessionState.Break)
                Move(finalScheduledAttempt?TutorialStage.FinalBreak:TutorialStage.BreakRunning,"TutorialBreakStarted");
            else if(state==SessionState.SessionComplete && Stage!=TutorialStage.ThankYou)
                Move(TutorialStage.Form,"TutorialFormReady");
        }
        public void ConfirmForm(int revision)
        {
            RequireRevision(revision);
            if(Stage!=TutorialStage.Form)throw new InvalidOperationException("The form is not waiting for confirmation.");
            Move(TutorialStage.ThankYou,"TutorialFormConfirmed");
        }
        private void Move(TutorialStage stage, string reason, bool restart=false)
        {
            if(stage==Stage && !restart)return;
            record?.Invoke(reason,stage);
            Stage=stage;ElapsedSeconds=0;Revision++;PresentationVersion++;
            // Session events supersede an explanation pause; they must never be
            // hidden behind a stale practice or break-completion screen.
            if(stage>=TutorialStage.FirstReady || stage==TutorialStage.PracticeComplete)Paused=false;
        }
        private static TutorialStage Next(TutorialStage stage) => stage switch {
            TutorialStage.Welcome=>TutorialStage.Questions, TutorialStage.Questions=>TutorialStage.ReadyForSetup,
            TutorialStage.ReadyForSetup=>TutorialStage.Equipment, TutorialStage.Equipment=>TutorialStage.Comfort,
            TutorialStage.Comfort=>TutorialStage.EquipmentCheck, TutorialStage.EquipmentCheck=>TutorialStage.HeadCalibration,
            TutorialStage.HeadCalibration=>TutorialStage.Holding, TutorialStage.Holding=>TutorialStage.GunCalibration,
            TutorialStage.GunCalibration=>TutorialStage.CalibrationComplete, TutorialStage.CalibrationComplete=>TutorialStage.Targets,
            TutorialStage.Targets=>TutorialStage.Aim, TutorialStage.Aim=>TutorialStage.Fire, TutorialStage.Fire=>TutorialStage.Reload,
            TutorialStage.Reload=>TutorialStage.PracticeReady, TutorialStage.PracticeComplete=>TutorialStage.Demonstration,
            TutorialStage.DemonstrationComplete=>TutorialStage.Scenarios, TutorialStage.Scenarios=>TutorialStage.TimeLimits,
            TutorialStage.TimeLimits=>TutorialStage.BreakOverview, TutorialStage.BreakOverview=>TutorialStage.FirstReady,
            _=>throw new InvalidOperationException("This step waits for its actual completion event.") };

        public static readonly IReadOnlyDictionary<TutorialStage,TutorialStep> Steps = new Dictionary<TutorialStage,TutorialStep> {
            [TutorialStage.NotStarted]=new TutorialStep("", "Welcome", "Please wait while the operator prepares your walkthrough.","welcome"),
            [TutorialStage.Welcome]=new TutorialStep("01", "Welcome to the ELTS Project\nResearch Study", "Thank you for being a participant.","welcome",automatic:true),
            [TutorialStage.Questions]=new TutorialStep("01-01", "One step at a time", "Questions or need a repeat? Ask the operator.","welcome",automatic:true),
            [TutorialStage.ReadyForSetup]=new TutorialStep("01-02", "Ready to begin?", "Let the operator know when you're ready.","welcome","Participant is ready"),
            [TutorialStage.Equipment]=new TutorialStep("02", "Equipment setup", "Please wait while the operator fits the equipment.","equipment","Equipment fitting finished"),
            [TutorialStage.Comfort]=new TutorialStep("02-01", "Comfort comes first", "Let the operator know if you need an adjustment.","equipment",automatic:true),
            [TutorialStage.EquipmentCheck]=new TutorialStep("02-02", "Equipment check", "Please wait while the operator checks the equipment.","equipment","Equipment checked · continue"),
            [TutorialStage.HeadCalibration]=new TutorialStep("03", "Head-tracker calibration", "Stand upright. Look straight ahead at the dot.","head","Review and accept head fixture"),
            [TutorialStage.Holding]=new TutorialStep("03-01", "Holding the study replica", "Try the position with the operator's help. Ask for any adjustment.","holding","Holding position tried · comfortable"),
            [TutorialStage.GunCalibration]=new TutorialStep("03-02", "Gun-tracker calibration", "Look down the sights and aim at the dot. Hold the position.","gun","Review and accept gun fixture"),
            [TutorialStage.CalibrationComplete]=new TutorialStep("03-03", "Calibration walkthrough complete", "Next: Study interface", "complete",automatic:true),
            [TutorialStage.Targets]=new TutorialStep("04", "Meet the practice target", "This is the target you'll use during practice.","target",automatic:true),
            [TutorialStage.Aim]=new TutorialStep("04-01", "Try aiming", "Hold the replica as shown. Aim at the practice target.","target","Aiming tried and understood"),
            [TutorialStage.Fire]=new TutorialStep("04-02", "Try firing", "Follow the operator's demonstration, then try firing.","target","Firing understood · try reloading"),
            [TutorialStage.Reload]=new TutorialStep("04-02", "Try reloading", "Follow the operator's demonstration, then try reloading.","reload","Reloading understood · continue"),
            [TutorialStage.PracticeReady]=new TutorialStep("05", "Ready for practice", "Let the operator know when you're ready. Practice is not scored.","practice","Start practice"),
            [TutorialStage.PracticeRunning]=new TutorialStep("05-01", "Practice", "Aim, fire and reload. Ask the operator if you need help.","practice"),
            [TutorialStage.PracticeComplete]=new TutorialStep("05-02", "Practice complete", "Questions? Ask the operator before the ELTS introduction.","complete","Ready for the simulated ELTS demonstration"),
            [TutorialStage.Demonstration]=new TutorialStep("06", "ELTS introduction", "ELTS is directly below the middle of the screen.","arrow"),
            [TutorialStage.DemonstrationComplete]=new TutorialStep("07", "Demonstration complete", "Questions? Ask the operator.","questions","Questions answered · continue"),
            [TutorialStage.Scenarios]=new TutorialStep("08", "Study overview", "Four scenarios. Scenario order varies.","scenarios",automatic:true),
            [TutorialStage.TimeLimits]=new TutorialStep("08-01", "Study overview", "Each scenario has a time limit.","scenarios",automatic:true),
            [TutorialStage.BreakOverview]=new TutorialStep("08-02", "Study overview", "A break follows every scenario, including the last.","scenarios",automatic:true),
            [TutorialStage.FirstReady]=new TutorialStep("09", "Ready for the first scenario?", "Let the operator know when you're ready.","ready"),
            [TutorialStage.Armed]=new TutorialStep("09-01", "Shoot to begin", "Click and release when you are ready.","ready"),
            [TutorialStage.Running]=new TutorialStep("", "", "", "live"),
            [TutorialStage.ScenarioPaused]=new TutorialStep("", "Scenario paused", "Please wait for the operator to resume.","ready"),
            [TutorialStage.ScenarioEnded]=new TutorialStep("10", "Scenario complete", "Your break will begin shortly.","complete"),
            [TutorialStage.BreakRunning]=new TutorialStep("10-01", "Break", "Take a moment to rest.","timer"),
            [TutorialStage.NextReady]=new TutorialStep("10-02", "Next scenario", "The next scenario will start shortly.","ready"),
            [TutorialStage.FinalEnded]=new TutorialStep("11", "All four scenarios complete", "Your final break will begin shortly.","complete"),
            [TutorialStage.FinalBreak]=new TutorialStep("11-01", "Final break", "Next: Post-study form", "timer"),
            [TutorialStage.Form]=new TutorialStep("11-02", "Post-study form", "Please complete the form provided by the operator.","form","Confirm form completed"),
            [TutorialStage.ThankYou]=new TutorialStep("11-03", "Thank you", "ELTS Project Research Study", "thanks"),
            [TutorialStage.Interrupted]=new TutorialStep("", "Session stopped", "Please wait for the operator.","ready")
        };
    }
}
