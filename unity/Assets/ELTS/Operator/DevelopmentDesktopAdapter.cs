#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Logging;
using Elts.Session;
using Elts.Tracking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    public sealed partial class DevelopmentSessionPanel
    {
        public const string DesktopInputOption = "Mouse and keyboard";
        public const string AutomatedInputOption = "Automated synthetic motion";
        private DesktopPlayController? desktop;
        private DesktopTrackingSource? desktopSource;
        private bool usingDesktopInput;
        private string desktopMessage = "Create a synthetic recording to test the complete pipeline.";
        private static readonly IReadOnlyDictionary<string,Vector3d> EmptyDesktopTargets = new Dictionary<string,Vector3d>();
        public DesktopPlayController Desktop => desktop!;
        public bool HasOpenDesktopRecording => desktopSource!=null && !closing;
        public bool DesktopInputSelected => engine!=null && !closing ? usingDesktopInput : root.Q<DropdownField>("inputSource").value==DesktopInputOption;
        public bool CanDesktopFire => usingDesktopInput && desktopSource!=null && !IsBusy && !closing && engine?.State==SessionState.BlockRunning;
        public string? DesktopBlockId => engine?.CurrentBlockId;
        public string DesktopFeedback => CanDesktopFire ? (scenario?.LastShot ?? desktopMessage) : desktopMessage;
        public string DesktopScore => "Hits "+(scenario?.HitCount??0)+"   Shots "+(scenario?.ShotCount??0)+
            "   Ammo "+(scenario?.AmmoRemaining??view.GameSettings.magazineCapacity)+" / "+(scenario?.MagazineCapacity??view.GameSettings.magazineCapacity);

        private void InitializeDesktop()
        {
            var selector=root.Q<DropdownField>("inputSource");
            selector.choices=new List<string>{DesktopInputOption,AutomatedInputOption};
            selector.value=DesktopInputOption;
            desktop=new DesktopPlayController(this,view,root);
        }

        public void ShowDesktopPreview(RigidPose head, RigidPose weapon)
        { view.SetSessionFrame(head,weapon,EmptyDesktopTargets); }

        private void RecordInputSource()
        {
            string identity=usingDesktopInput?DesktopTrackingSource.SourceIdentity:FixtureIdentity;
            if(!recording!.TryRecord(new SessionEvent(sequence!.Next(),clock!.Now,"DevelopmentInputSource",new[] {
                LogField.String("source",identity),LogField.Boolean("synthetic",true),
                LogField.String("coordinateFrame","room-metres"),LogField.String("triggerEdge","falling"),
                LogField.String("desktopMuzzlePolicy",usingDesktopInput?"coincident-with-virtual-eye":"automated-fixture")})))
                throw new InvalidOperationException("Input-source provenance could not be recorded.");
        }

        public void SubmitDesktopTrigger()
        {
            if(!CanDesktopFire || (!SeparateAdministrator && desktop?.IsOpen!=true))return;
            try
            {
                // Input and critical events are observed on the main thread.
                // The acquisition lock admits this exact pair to raw logging
                // before we record its trigger identity or use it for hitscan.
                if(!acquisition!.TryCaptureForTrigger(out var pair))
                { desktopMessage="Shot ignored: raw pose sample was not admitted to the recording.";return; }
                if(!pair.Head.HasValidPose || !pair.Weapon.HasValidPose)
                { desktopMessage="Shot ignored: desktop tracking is unavailable.";return; }
                if(!recording!.TryRecord(new SessionEvent(sequence!.Next(),pair.Timestamp,"InputTrigger",new[] {
                    LogField.String("source",DesktopTrackingSource.SourceIdentity),
                    LogField.String("blockId",engine!.CurrentBlockId!),
                    LogField.NumberValue("acquisitionSequence",pair.Sequence),LogField.String("edge","falling")})))
                { engine!.Abort("Desktop trigger provenance could not be recorded.");return; }
                scenario!.Fire(pair.Weapon.Pose,pair.Head.Pose,pair.Timestamp);
                desktopMessage=scenario.LastShot;
                view.SetSessionFrame(pair.Head.Pose,pair.Weapon.Pose,scenario.Positions);
            }
            catch(Exception exception)
            {
                desktopMessage=exception.Message;
                // Expiry may legitimately finish the block between observation
                // and dispatch. Other failures retain the existing abort path.
                if(engine?.State==SessionState.BlockRunning)engine.Abort("Desktop input failure: "+exception.Message);
            }
        }

        public string DesktopPrimaryLabel => engine?.State switch
        {
            SessionState.SessionSetup => "Continue to calibration",
            SessionState.Calibration => "Review calibration fixture",
            SessionState.Practice => "Practice timer running",
            SessionState.BlockReady => "Start block",
            SessionState.BlockEnded => "Continue to break",
            SessionState.Break => "Continue to next block",
            _ => "Administrator controls"
        };
        public bool DesktopPrimaryEnabled => !IsBusy && engine?.State!=SessionState.Practice
            && (engine?.State!=SessionState.Break || engine.RemainingSeconds<=0);
        public void DesktopPrimaryAction()
        {
            if(!DesktopPrimaryEnabled)return;
            string? action=engine?.State switch
            {
                SessionState.SessionSetup => "advance",SessionState.BlockReady => "startBlock",
                SessionState.BlockEnded => "advance",SessionState.Break => "advance",_ => null
            };
            if(action!=null)
            {
                var button=root.Q<Button>(action);
                if(button.enabledSelf)using(var submit=NavigationSubmitEvent.GetPooled()) {submit.target=button;button.SendEvent(submit);}
            }
            if(engine==null || engine.State==SessionState.Calibration || action==null)
            { desktop!.SetOpen(false);dashboard!.FocusCurrentTask(); }
        }
    }
}
