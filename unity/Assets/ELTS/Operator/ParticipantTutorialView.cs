#nullable enable
using System;
using System.Collections.Generic;
using Elts.Rendering;
using Elts.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Participant-only slides and cues, scaled from a 1920x1080 canvas.
    /// All checks live in the controller; rebuilding a screen has no side effects.</summary>
    public sealed partial class ParticipantTutorialView
    {
        private readonly DevelopmentSessionPanel session;
        private readonly DevelopmentView view;
        private readonly VisualElement host,layer,canvas,content;
        private readonly Label eyebrow,heading,instruction,detail,timer,footer,paused;
        private readonly List<VisualElement> extras=new List<VisualElement>();
        private readonly List<(VisualElement element, float delay)> reveals=new List<(VisualElement, float)>();
        private readonly List<ParticipantTutorialArt> drawings=new List<ParticipantTutorialArt>();
        // Persistent chrome: where you are in the walkthrough, and what to do now.
        private static readonly string[] Chapters={"Welcome","Equipment","Calibration","Controls","Practice","ELTS","Overview"};
        private readonly VisualElement tracker,cue,dock;
        private readonly List<VisualElement> chapterSteps=new List<VisualElement>();
        private readonly ParticipantTutorialArt cueIcon;
        private readonly Label cueText;
        private string cueMode="";
        private Label? actionStatus,saveStatus;
        private VisualElement? illustration,dot,targetOutline,targetLabel,ammoHint;
        private int version=-1;
        private float width=-1,height=-1;
        public ParticipantTutorialView(VisualElement host,DevelopmentSessionPanel session,DevelopmentView view)
        {
            this.host=host;this.session=session;this.view=view;
            Resources.Load<VisualTreeAsset>("ELTS/ParticipantTutorial").CloneTree(host);
            layer=host.Q("tutorialLayer");canvas=host.Q("tutorialCanvas");content=host.Q("tutorialContent");
            eyebrow=host.Q<Label>("tutorialEyebrow");heading=host.Q<Label>("tutorialHeading");
            instruction=host.Q<Label>("tutorialInstruction");detail=host.Q<Label>("tutorialDetail");
            timer=host.Q<Label>("tutorialTimer");footer=host.Q<Label>("tutorialFooter");paused=host.Q<Label>("tutorialPaused");
            tracker=host.Q("tutorialTracker");cue=host.Q("tutorialCue");dock=host.Q("tutorialDock");
            for(int i=0;i<Chapters.Length;i++)
            {
                var step=Element(tracker,"tutorial-tracker-step");
                if(i<Chapters.Length-1)Element(step,"tutorial-tracker-line");
                Element(step,"tutorial-tracker-dot");Text(step,Chapters[i],"tutorial-tracker-label");
                chapterSteps.Add(step);
            }
            var pill=Element(cue,"tutorial-cue-pill");
            cueIcon=new ParticipantTutorialArt("speaker",session.TutorialSettings);cueIcon.AddToClassList("tutorial-cue-icon");pill.Add(cueIcon);
            cueText=Text(pill,"","tutorial-cue-text");
        }
        public void Refresh()
        {
            var flow=session.Tutorial;var settings=session.TutorialSettings;var engine=session.Engine;
            layer.EnableInClassList("tutorial-hidden",!flow.Started);
            if(!flow.Started)return;
            Resize();
            if(version!=flow.PresentationVersion){version=flow.PresentationVersion;Build();}
            bool running=flow.Stage==TutorialStage.Running;
            bool live=flow.LiveScene || running;
            layer.EnableInClassList("tutorial-live",live);
            canvas.EnableInClassList("tutorial-arrow-mode",flow.Stage==TutorialStage.Demonstration);
            paused.EnableInClassList("tutorial-hidden",!flow.Paused);
            heading.EnableInClassList("tutorial-hidden",running);
            dock.EnableInClassList("tutorial-hidden",running);
            footer.EnableInClassList("tutorial-hidden",running);
            // A pause during the entrance must still leave the instruction
            // readable; reduced motion also skips the text entrance entirely.
            float fade=settings.reducedMotion || flow.Paused?1:Mathf.Clamp01((float)(flow.ElapsedSeconds/settings.fadeSeconds));
            // Fade the content, never the opaque backdrop (which could expose a
            // previous scenario). Reference dots remain steady through replay.
            heading.style.opacity=fade;instruction.style.opacity=fade;
            foreach(var reveal in reveals)
                reveal.element.style.opacity=settings.reducedMotion?1:Mathf.Clamp01((float)((flow.ElapsedSeconds-reveal.delay)/settings.fadeSeconds));
            foreach(var drawing in drawings)drawing.SetElapsed(flow.ElapsedSeconds);
            host.Q("participantAmmo")?.EnableInClassList("participant-ammo-teaching",
                flow.Stage==TutorialStage.Fire || flow.Stage==TutorialStage.Reload);
            if(actionStatus!=null)
                actionStatus.text=flow.Stage==TutorialStage.Fire
                    ?(flow.Fired?"✓ Shot fired":"Fire once · a hit is not needed")
                    :(flow.Reloaded?"✓ Reloaded":"Watch the ammo refill");
            if(saveStatus!=null)saveStatus.text=session.IsBusy?"Saving…":"Break next";
            if(illustration!=null && dot!=null)
            {
                bool showDot=flow.Stage==TutorialStage.HeadCalibration || session.TutorialDotVisible;
                illustration.EnableInClassList("tutorial-hidden",showDot);dot.EnableInClassList("tutorial-hidden",!showDot);
                instruction.text=showDot && flow.Stage==TutorialStage.GunCalibration?"Keep aiming at the dot.":flow.Step.Instruction;
            }
            if(flow.Stage==TutorialStage.HeadCalibration && flow.HeadSequenceComplete)detail.text="All 7 steps captured · the operator is checking";
            if(flow.Stage==TutorialStage.Demonstration)
                detail.text=flow.DemoFailed?"Demonstration interrupted · please wait":flow.DemoRequested?"Simulated ELTS demonstration":"";
            if(flow.Stage==TutorialStage.ScenarioEnded || flow.Stage==TutorialStage.FinalEnded)
                heading.text=flow.LastAttemptAbnormal?"Attempt ended":flow.Stage==TutorialStage.FinalEnded && !flow.AllScenariosCompleted?"Scheduled attempts finished":flow.Step.Title;
            if(flow.Stage==TutorialStage.NextReady && flow.LastBreakSkipped)detail.text="Break skipped";
            if(flow.Stage==TutorialStage.Form && flow.LastBreakSkipped)detail.text="Final break skipped · questions? Ask the operator.";
            bool hasTimer=running || flow.Stage==TutorialStage.PracticeRunning || flow.Stage==TutorialStage.BreakRunning || flow.Stage==TutorialStage.FinalBreak;
            timer.EnableInClassList("tutorial-hidden",!hasTimer);
            if(hasTimer)timer.text=FormatTime(engine?.RemainingSeconds??0);
            if(flow.Stage==TutorialStage.PracticeRunning && engine?.PracticePaused==true)
            {heading.text="Practice paused";instruction.text="Please wait for the operator.";}
            else if(flow.Stage==TutorialStage.PracticeRunning){heading.text=flow.Step.Title;instruction.text=flow.Step.Instruction;}
            if(flow.Stage==TutorialStage.Armed)
            {
                int number=Math.Min(4,Array.IndexOf(session.StationOrder,engine?.CurrentCondition)+1);
                eyebrow.text=engine?.RerunIndex>0?"REPEAT SCENARIO":"SCENARIO "+number+" OF 4";
                detail.text="Time limit  "+FormatTime(engine?.CurrentDurationSeconds??0);
            }
            if(running)eyebrow.text="SCENARIO";
            eyebrow.EnableInClassList("tutorial-hidden",String.IsNullOrEmpty(eyebrow.text));
            detail.EnableInClassList("tutorial-hidden",running || String.IsNullOrWhiteSpace(detail.text));
            RefreshCue(flow,engine?.PracticePaused==true);
            PositionTargetCue();
            PositionAmmoHint();
        }
        private void PositionAmmoHint()
        {
            if(ammoHint==null)return;
            // Measure the real ammunition display each frame and centre the
            // callout above it, so it lines up at any window size or text width.
            var ammo=host.Q("participantAmmo");
            bool visible=ammo!=null && ammo.resolvedStyle.display==DisplayStyle.Flex && ammo.worldBound.width>0 && layer.layout.height>0;
            ammoHint.EnableInClassList("tutorial-hidden",!visible);
            if(!visible)return;
            var bounds=layer.WorldToLocal(ammo!.worldBound);
            ammoHint.style.left=bounds.xMin;ammoHint.style.width=bounds.width;
            ammoHint.style.bottom=layer.layout.height-bounds.yMin+6;
        }
        private void RefreshCue(ParticipantTutorial flow,bool practicePaused)
        {
            var (mode,label,icon)=CueFor(flow,practicePaused);
            cue.EnableInClassList("tutorial-hidden",mode.Length==0);
            if(label==cueText.text && mode==cueMode)return;
            cue.RemoveFromClassList("tutorial-cue-"+cueMode);cueMode=mode;cue.AddToClassList("tutorial-cue-"+mode);
            cueText.text=label;cueIcon.Kind=icon;
            cueIcon.Ink=mode=="act"?Confirmed:mode=="tell" || mode=="watch"?new Color(.59f,.70f,.79f):new Color(.75f,.79f,.83f);
        }
        /// <summary>One badge says whether to listen, act, speak up or wait. It
        /// follows the cue clock only, so a replay returns it to "Listen" and an
        /// early action can never be invited before the instruction ends.</summary>
        private static (string mode,string label,string icon) CueFor(ParticipantTutorial flow,bool practicePaused)
        {
            var stage=flow.Stage;
            if(stage==TutorialStage.Running || stage==TutorialStage.ThankYou)return ("","","");
            if(stage==TutorialStage.BreakRunning || stage==TutorialStage.FinalBreak)return ("rest","REST","rest");
            if(!flow.CueFinished && flow.Step.Script.Length>0)return ("listen","LISTEN","speaker");
            switch(stage)
            {
                case TutorialStage.ReadyForSetup:case TutorialStage.PracticeReady:case TutorialStage.PracticeComplete:
                case TutorialStage.DemonstrationComplete:case TutorialStage.FirstReady:
                    return ("tell","TELL THE OPERATOR","bubble");
                case TutorialStage.HeadCalibration:case TutorialStage.GunCalibration:
                    return ("act","HOLD STILL","look");
                case TutorialStage.PracticeRunning when practicePaused:
                    return ("wait","PLEASE WAIT","hourglass");
                case TutorialStage.Holding:case TutorialStage.Aim:case TutorialStage.Fire:case TutorialStage.Reload:
                case TutorialStage.PracticeRunning:case TutorialStage.Armed:case TutorialStage.Form:
                    return ("act","YOUR TURN","hand");
                case TutorialStage.Demonstration:
                    return ("watch","WATCH BELOW THE SCREEN","watch");
                case TutorialStage.Equipment:case TutorialStage.EquipmentCheck:case TutorialStage.ScenarioPaused:
                case TutorialStage.Interrupted:case TutorialStage.ScenarioEnded:case TutorialStage.FinalEnded:case TutorialStage.NextReady:
                    return ("wait","PLEASE WAIT","hourglass");
                default: return ("listen","LISTEN","speaker");
            }
        }
        private static int ChapterOf(TutorialStage stage) =>
            stage<TutorialStage.Equipment?0:stage<TutorialStage.HeadCalibration?1:stage<TutorialStage.Targets?2:
            stage<TutorialStage.PracticeReady?3:stage<TutorialStage.Demonstration?4:stage<TutorialStage.Scenarios?5:
            stage<TutorialStage.FirstReady?6:-1;
        private void Resize()
        {
            float w=host.resolvedStyle.width,h=host.resolvedStyle.height;
            if(w==width && h==height)return;width=w;height=h;
            if(w<=0 || h<=0)return;
            // Keep typography and margins proportional on any desktop window.
            float scale=Mathf.Min(w/1920,h/1080);
            canvas.style.left=(w-1920)/2;canvas.style.top=(h-1080)/2;
            canvas.style.scale=new Scale(new Vector3(scale,scale,1));
        }
        private void Build()
        {
            foreach(var extra in extras)extra.RemoveFromHierarchy();
            extras.Clear();reveals.Clear();drawings.Clear();content.Clear();
            illustration=null;dot=null;ammoHint=null;targetOutline=null;targetLabel=null;actionStatus=null;saveStatus=null;
            var flow=session.Tutorial;var stage=flow.Stage;
            heading.text=flow.Step.Title;instruction.text=flow.Step.Instruction;detail.text="";
            eyebrow.text=stage==TutorialStage.PracticeReady || stage==TutorialStage.PracticeIntroduction || stage==TutorialStage.PracticeRunning?"NOT SCORED":
                stage>=TutorialStage.FirstReady && stage!=TutorialStage.ThankYou?"ELTS PROJECT RESEARCH STUDY":"";
            footer.text="";
            // The tracker replaces the eyebrow during the walkthrough. Calibration
            // hides it so no second dot appears near the reference dots.
            int chapter=ChapterOf(stage);
            tracker.EnableInClassList("tutorial-hidden",chapter<0 || stage==TutorialStage.HeadCalibration || stage==TutorialStage.GunCalibration);
            for(int i=0;i<chapterSteps.Count;i++)
            {
                chapterSteps[i].EnableInClassList("tutorial-tracker-past",i<chapter);
                chapterSteps[i].EnableInClassList("tutorial-tracker-current",i==chapter);
            }

            // Choose by lesson, not a shared decorative icon. Related overview
            // steps retain their geometry and reveal only the new information.
            switch(stage)
            {
                case TutorialStage.Welcome: BuildWelcome();break;
                case TutorialStage.Questions: BuildQuestions();break;
                case TutorialStage.ReadyForSetup: BuildReadiness();break;
                case TutorialStage.Equipment: BuildEquipment();break;
                case TutorialStage.Comfort: BuildComfort();break;
                case TutorialStage.EquipmentCheck: BuildEquipmentReview();break;
                case TutorialStage.HeadCalibration: BuildCalibration(false);break;
                case TutorialStage.Holding: BuildHolding();break;
                case TutorialStage.GunCalibration: BuildCalibration(true);break;
                case TutorialStage.CalibrationComplete: BuildCalibrationSummary();break;
                case TutorialStage.Targets:case TutorialStage.Aim:
                    targetOutline=Extra(new VisualElement(),"tutorial-target-outline");
                    targetLabel=Extra(new Label("Practice target"),"tutorial-target-label");
                    if(stage==TutorialStage.Aim)detail.text="Desktop rehearsal · move the mouse";
                    break;
                case TutorialStage.Fire:case TutorialStage.Reload: BuildControlLesson(stage);break;
                case TutorialStage.FireConfirmed:case TutorialStage.ReloadConfirmed:BuildControlConfirmation();break;
                case TutorialStage.PracticeReady:case TutorialStage.PracticeIntroduction: BuildPracticeGuide();break;
                case TutorialStage.PracticeComplete: BuildPracticeSummary();break;
                case TutorialStage.Demonstration: BuildDemonstration();break;
                case TutorialStage.DemonstrationComplete: BuildDebrief();break;
                case TutorialStage.Scenarios:case TutorialStage.TimeLimits:case TutorialStage.BreakOverview:
                    BuildScenarioSequence(stage);break;
                case TutorialStage.FirstReady: BuildReadiness();break;
                case TutorialStage.Armed: BuildStartCue();break;
                case TutorialStage.ScenarioPaused: BuildHold(false);break;
                case TutorialStage.ScenarioEnded:case TutorialStage.FinalEnded: BuildCheckpoint();break;
                case TutorialStage.BreakRunning:case TutorialStage.FinalBreak: BuildRest(stage==TutorialStage.FinalBreak);break;
                case TutorialStage.NextReady: BuildNextScenario();break;
                case TutorialStage.Form: BuildForm();break;
                case TutorialStage.ThankYou: BuildThanks();footer.text="";break;
                case TutorialStage.Interrupted: BuildHold(true);break;
            }
        }
        private T Extra<T>(T element,string className) where T:VisualElement
        {
            foreach(string name in className.Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries))element.AddToClassList(name);
            element.pickingMode=PickingMode.Ignore;canvas.Add(element);extras.Add(element);return element;
        }
        private void PositionTargetCue()
        {
            if(targetOutline==null || targetLabel==null)return;
            // Highlight the rendered target, not an invented aiming point. Remove
            // the cue after the initial explanation/aiming reveal.
            bool visible=session.Tutorial.Stage==TutorialStage.Targets || session.Tutorial.ElapsedSeconds<2;
            var position=session.TutorialTargetPosition;
            if(!position.HasValue)visible=false;
            targetOutline.EnableInClassList("tutorial-hidden",!visible);targetLabel.EnableInClassList("tutorial-hidden",!visible);
            if(!visible)return;
            var center=OffAxisCamera.ToUnity(position!.Value);
            Vector3 feet=view.ParticipantCamera.WorldToViewportPoint(center+Vector3.down*.85f);
            Vector3 head=view.ParticipantCamera.WorldToViewportPoint(center+Vector3.up*.85f);
            if(feet.z<=0 || head.z<=0){targetOutline.AddToClassList("tutorial-hidden");targetLabel.AddToClassList("tutorial-hidden");return;}
            // Camera fills the window; convert to the fitted reference canvas.
            float scale=Mathf.Min(width/1920,height/1080),ox=(width-1920*scale)/2,oy=(height-1080*scale)/2;
            float targetHeight=Mathf.Abs(head.y-feet.y)*height/scale;
            float x=(feet.x*width-ox)/scale,y=((1-head.y)*height-oy)/scale;
            targetOutline.style.left=x-targetHeight*.24f;targetOutline.style.top=y-12;
            targetOutline.style.width=targetHeight*.48f;targetOutline.style.height=targetHeight+24;
            targetLabel.style.left=Mathf.Clamp(x+targetHeight*.3f,192,1420);targetLabel.style.top=Mathf.Clamp(y+30,260,650);
        }
        private static string FormatTime(double seconds)
        {int value=(int)Math.Max(0,Math.Ceiling(seconds));return (value/60).ToString("D2")+":"+(value%60).ToString("D2");}
    }
}
