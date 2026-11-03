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
    public sealed class ParticipantTutorialView
    {
        private readonly DevelopmentSessionPanel session;
        private readonly DevelopmentView view;
        private readonly VisualElement host,layer,canvas,content;
        private readonly Label eyebrow,heading,instruction,detail,timer,footer,paused;
        private readonly List<VisualElement> extras=new List<VisualElement>();
        private readonly List<VisualElement> reveals=new List<VisualElement>();
        private VisualElement? illustration,dot,targetOutline,targetLabel;
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
            instruction.EnableInClassList("tutorial-hidden",running);
            detail.EnableInClassList("tutorial-hidden",running);
            footer.EnableInClassList("tutorial-hidden",running);
            float fade=Mathf.Clamp01((float)(flow.ElapsedSeconds/settings.fadeSeconds));
            // Fade the content, never the opaque backdrop (which could expose a
            // previous scenario). Reference dots remain steady through replay.
            heading.style.opacity=fade;instruction.style.opacity=fade;
            for(int i=0;i<reveals.Count;i++)
                reveals[i].style.opacity=settings.reducedMotion?fade:Mathf.Clamp01((float)((flow.ElapsedSeconds-i*settings.revealSeconds)/settings.fadeSeconds));
            if(illustration!=null && dot!=null)
            {
                bool showDot=session.TutorialDotVisible;
                illustration.EnableInClassList("tutorial-hidden",showDot);dot.EnableInClassList("tutorial-hidden",!showDot);
                instruction.text=showDot?(flow.Stage==TutorialStage.HeadCalibration?"Keep looking at the dot while the operator checks the head tracker.":
                    "Keep aiming at the dot while the operator checks the gun tracker."):flow.Step.Instruction;
            }
            if(flow.Stage==TutorialStage.Demonstration)
                detail.text=flow.DemoFailed?"Demonstration interrupted · please wait":flow.DemoRequested?"Simulated ELTS demonstration":"The operator will demonstrate the sequence briefly.";
            if(flow.Stage==TutorialStage.ScenarioEnded || flow.Stage==TutorialStage.FinalEnded)
            {
                heading.text=flow.LastAttemptAbnormal?"Attempt ended":flow.Stage==TutorialStage.FinalEnded && !flow.AllScenariosCompleted?"Scheduled attempts finished":flow.Step.Title;
                detail.text=session.IsBusy?"Saving your recording…":"Your break will begin shortly.";
            }
            if(flow.Stage==TutorialStage.NextReady && flow.LastBreakSkipped)detail.text="Break skipped. The next scenario will start shortly.";
            if(flow.Stage==TutorialStage.Form && flow.LastBreakSkipped)detail.text="The final break was skipped. Questions? Ask the operator.";
            bool hasTimer=running || flow.Stage==TutorialStage.PracticeRunning || flow.Stage==TutorialStage.BreakRunning || flow.Stage==TutorialStage.FinalBreak;
            timer.EnableInClassList("tutorial-hidden",!hasTimer);
            if(hasTimer)timer.text=FormatTime(engine?.RemainingSeconds??0);
            if(flow.Stage==TutorialStage.PracticeRunning && engine?.PracticePaused==true)heading.text="Practice paused";
            else if(flow.Stage==TutorialStage.PracticeRunning)heading.text=flow.Step.Title;
            if(flow.Stage==TutorialStage.Armed)
            {
                int number=Math.Min(4,Array.IndexOf(session.StationOrder,engine?.CurrentCondition)+1);
                eyebrow.text=engine?.RerunIndex>0?"REPEAT SCENARIO":"SCENARIO "+number+" OF 4";
                detail.text="Time limit  "+FormatTime(engine?.CurrentDurationSeconds??0)+"   ·   Timer starts with your first click";
            }
            if(running)eyebrow.text="SCENARIO";
            detail.EnableInClassList("tutorial-hidden",running || String.IsNullOrWhiteSpace(detail.text));
            PositionTargetCue();
        }
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
            foreach(var extra in extras)extra.RemoveFromHierarchy();extras.Clear();reveals.Clear();content.Clear();
            illustration=null;dot=null;targetOutline=null;targetLabel=null;
            var flow=session.Tutorial;var settings=session.TutorialSettings;var stage=flow.Stage;
            heading.text=flow.Step.Title;instruction.text=flow.Step.Instruction;detail.text="";
            eyebrow.text=stage==TutorialStage.PracticeReady || stage==TutorialStage.PracticeRunning?"PRACTICE · NOT SCORED":
                stage>=TutorialStage.FirstReady?"ELTS PROJECT RESEARCH STUDY":"PARTICIPANT TUTORIAL";
            if(flow.LiveScene)eyebrow.text+=" · SYNTHETIC";
            footer.text="SYNTHETIC WALKTHROUGH";
            switch(flow.Step.Visual)
            {
                case "welcome":
                    Card("Setup","setup",true);Card("Practice","practice");Card("Study","study");break;
                case "equipment":
                    Card("Tracking\nequipment","tracking");Card("Eye tracker","eye");Card("Heart-rate\nmonitor","heart");
                    detail.text=stage==TutorialStage.EquipmentCheck?"Next: Calibration":"The operator handles fitting and adjustments.";break;
                case "head":case "gun":
                    illustration=Extra(new ParticipantTutorialArt(flow.Step.Visual=="head"?"posture":"holding",settings),"tutorial-illustration");
                    if(flow.Step.Visual=="gun" && !settings.showHoldingDiagram)illustration.visible=false;
                    dot=Extra(new VisualElement(),"tutorial-dot");
                    float x=flow.Step.Visual=="head"?settings.headDotX:settings.gunDotX;
                    float y=flow.Step.Visual=="head"?settings.headDotY:settings.gunDotY;
                    dot.style.left=x*1920-settings.dotDiameter/2;dot.style.top=y*1080-settings.dotDiameter/2;
                    dot.style.width=settings.dotDiameter;dot.style.height=settings.dotDiameter;
                    if(ColorUtility.TryParseHtmlString(settings.dotColor,out var dotColor))dot.style.backgroundColor=dotColor;
                    detail.text="Wait for the operator's review.";break;
                case "holding":
                    if(settings.showHoldingDiagram)
                    {
                        Extra(new ParticipantTutorialArt("holding",settings),"tutorial-illustration");
                        PlacedLabel("Stock",1000,392,230);PlacedLabel("Against your shoulder",275,588,480);
                        detail.text="Contact diagram · follow the operator's demonstration.";
                    }
                    else { LargeSymbol("questions");detail.text="Follow the operator's demonstration."; }
                    break;
                case "target":case "reload":
                    if(stage==TutorialStage.Targets || stage==TutorialStage.Aim)
                    {
                        targetOutline=Extra(new VisualElement(),"tutorial-target-outline");
                        targetLabel=Extra(new Label("Practice target"),"tutorial-target-label");
                    }
                    if(stage==TutorialStage.Fire)detail.text="Desktop rehearsal: click and release to fire.";
                    if(stage==TutorialStage.Reload)detail.text="Desktop rehearsal: press R to reload.";
                    if(stage==TutorialStage.Aim)detail.text="Desktop rehearsal: move the mouse to aim.";
                    break;
                case "practice":break;
                case "arrow":
                    reveals.Add(Extra(new ParticipantTutorialArt("arrow",settings),"tutorial-arrow"));
                    Extra(new Label("ELTS below\nthe screen"),"tutorial-arrow-label");
                    footer.text="SIMULATED DEMONSTRATION · NO HARDWARE OUTPUTS";break;
                case "scenarios":
                    for(int i=0;i<4;i++)
                    {
                        var card=Card("Scenario "+(i+1),stage==TutorialStage.Scenarios?"study":"clock");card.AddToClassList("tutorial-small-card");
                        var marker=new Label(i==3?"Final\nbreak":"Break");marker.AddToClassList("tutorial-break-marker");content.Add(marker);
                        marker.style.opacity=stage==TutorialStage.BreakOverview?1:0;
                    }
                    if(stage==TutorialStage.BreakOverview)detail.text="Example: Break — "+FormatTime(session.Engine?.BreakDurationSeconds??0)+"   ·   Not counting down";
                    break;
                case "complete":LargeSymbol("complete");break;
                case "questions":LargeSymbol("questions");break;
                case "ready":LargeSymbol("ready");break;
                case "form":LargeSymbol("form");detail.text="Questions? Ask the operator.";break;
                case "thanks":LargeSymbol("complete");footer.text="";break;
            }
        }
        private VisualElement Card(string label,string icon,bool emphasized=false)
        {
            var card=new VisualElement();card.AddToClassList("tutorial-card");
            if(emphasized)card.AddToClassList("tutorial-card-emphasis");
            var art=new ParticipantTutorialArt(icon,session.TutorialSettings);art.AddToClassList("tutorial-card-icon");card.Add(art);
            var text=new Label(label);text.AddToClassList("tutorial-card-label");card.Add(text);content.Add(card);reveals.Add(card);return card;
        }
        private void LargeSymbol(string kind)
        {
            var art=new ParticipantTutorialArt(kind,session.TutorialSettings);art.style.width=210;art.style.height=210;content.Add(art);reveals.Add(art);
        }
        private T Extra<T>(T element,string className) where T:VisualElement
        {element.AddToClassList(className);element.pickingMode=PickingMode.Ignore;canvas.Add(element);extras.Add(element);return element;}
        private void PlacedLabel(string text,float x,float y,float w)
        {
            var label=Extra(new Label(text),"tutorial-card-note");label.style.position=Position.Absolute;
            label.style.left=x;label.style.top=y;label.style.width=w;
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
