#nullable enable
using System;
using System.Collections.Generic;
using Elts.Session;

namespace Elts.Operator
{
    /// <summary>Presentation settings only. None of these values is a physical exposure or calibration acceptance value.</summary>
    [Serializable]
    public sealed class ParticipantTutorialSettings
    {
        public float fadeSeconds=.35f, revealSeconds=1.1f, calibrationIllustrationSeconds=3;
        // Duration of one explanatory drawing; it settles and never loops.
        public float diagramSeconds=2.4f;
        public float headDotX=.5f, headDotY=.5f, gunDotX=.5f, gunDotY=.5f, dotDiameter=16;
        // Look-left/right/up dots: distance (window pixels) from the edge they sit beside.
        public float edgeDotMarginPx=72;
        // Step-left/right dots: horizontal offset from the centre dot, as a fraction of canvas width.
        public float headStepOffsetX=.25f;
        // Static outline ring around the reference dot (pixels); 0 hides it.
        public float dotRingDiameter=56;
        public string dotColor="#F5F3EE";
        public bool narrationEnabled=true;
        public float narrationVolume=.85f;
        public string sceneBackgroundColor="#23313F", sceneFloorColor="#68717B";
        public float arrowWidth=12, arrowHeadWidth=100, arrowEndY=.94f;
        public bool reducedMotion=false, showHoldingDiagram=true;
        public double explanationSeconds=7, narrationGapSeconds=.25, simulatedDemoSeconds=2, targetDistanceM=15;
        public double targetRespawnSeconds=1.1;
        public Dictionary<string,double> stepSeconds=new Dictionary<string,double>();
        public double Duration(TutorialStep step) => stepSeconds.TryGetValue(step.Script,out var value)?value:explanationSeconds;
        public void Validate()
        {
            void Range(double value,double min,double max,string name)
            { if(double.IsNaN(value) || double.IsInfinity(value) || value<min || value>max)throw new ArgumentException("Invalid tutorial setting: "+name); }
            Range(fadeSeconds,.1,1,nameof(fadeSeconds));Range(revealSeconds,.1,4,nameof(revealSeconds));
            Range(diagramSeconds,.5,6,nameof(diagramSeconds));
            Range(calibrationIllustrationSeconds,0,10,nameof(calibrationIllustrationSeconds));
            // Keep the reference dot inside the clear central field, away from text.
            Range(headDotX,.2,.8,nameof(headDotX));Range(gunDotX,.2,.8,nameof(gunDotX));
            Range(headDotY,.36,.64,nameof(headDotY));Range(gunDotY,.36,.64,nameof(gunDotY));
            Range(edgeDotMarginPx,24,300,nameof(edgeDotMarginPx));
            Range(headStepOffsetX,.05,.3,nameof(headStepOffsetX));
            Range(headDotX-headStepOffsetX,.05,.95,"head step-left dot");Range(headDotX+headStepOffsetX,.05,.95,"head step-right dot");
            Range(dotRingDiameter,0,120,nameof(dotRingDiameter));
            Range(dotDiameter,8,40,nameof(dotDiameter));Range(arrowWidth,4,30,nameof(arrowWidth));
            Range(arrowHeadWidth,40,180,nameof(arrowHeadWidth));Range(arrowEndY,.85,.96,nameof(arrowEndY));
            Range(explanationSeconds,2,60,nameof(explanationSeconds));Range(simulatedDemoSeconds,.1,30,nameof(simulatedDemoSeconds));
            Range(targetDistanceM,.5,25,nameof(targetDistanceM));
            Range(targetRespawnSeconds,1,5,nameof(targetRespawnSeconds));
            Range(narrationVolume,0,1,nameof(narrationVolume));
            Range(narrationGapSeconds,0,3,nameof(narrationGapSeconds));
            foreach(string color in new[]{dotColor,sceneBackgroundColor,sceneFloorColor})
                if(!System.Text.RegularExpressions.Regex.IsMatch(color??"","^#[0-9A-Fa-f]{6}$"))
                    throw new ArgumentException("Tutorial colors must use six-digit hexadecimal values.");
            foreach(var value in stepSeconds)Range(value.Value,2,60,"stepSeconds."+value.Key);
        }
    }
}
