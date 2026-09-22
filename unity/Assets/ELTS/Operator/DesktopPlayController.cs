#nullable enable
using System;
using Elts.Tracking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Presentation and focus policy for desktop pipeline rehearsal. All session
    /// actions still enter DevelopmentSessionPanel; this class cannot score or
    /// write recording files. The desktop source only receives latched poses.
    /// </summary>
    public sealed class DesktopPlayController : IDisposable
    {
        private readonly DevelopmentSessionPanel session;
        private readonly DevelopmentView view;
        private readonly VisualElement root, overlay, dashboard, reticle;
        private readonly Image image;
        private readonly Button primary;
        private DesktopTrackingSource? source;
        private bool pressed;
        private string? pressedBlock;
        private bool disposed;
        public DesktopInputModel Model { get; }
        public IDesktopControls Controls { get; set; } = new MouseKeyboardControls();
        public bool IsOpen { get; private set; }

        public DesktopPlayController(DevelopmentSessionPanel session, DevelopmentView view, VisualElement root)
        {
            this.session=session;this.view=view;this.root=root;
            Model=new DesktopInputModel(view.Configuration);
            Resources.Load<VisualTreeAsset>("ELTS/DesktopPlay").CloneTree(root);
            overlay=root.Q<VisualElement>("desktopPlay");dashboard=root.Q<VisualElement>("sessionPanel");
            image=root.Q<Image>("playImage");reticle=root.Q<VisualElement>("playReticle");
            image.image=root.Q<Image>("participantImage").image;image.scaleMode=ScaleMode.ScaleToFit;
            reticle.pickingMode=PickingMode.Ignore;
            primary=root.Q<Button>("playPrimary");primary.clicked+=PrimaryAction;
            root.Q<Button>("returnAdministrator").clicked+=()=>SetOpen(false);
            root.Q<Button>("openDesktopPlay").clicked+=()=>SetOpen(true);
            root.Q<Button>("openDesktopPlayRun").clicked+=()=>SetOpen(true);
        }

        public void Attach(DesktopTrackingSource value)
        { source=value;Model.Reset();source.Publish(Model.Head,Model.Weapon);CancelTrigger(); }
        public void Detach() { source=null;CancelTrigger(); }
        public void SetOpen(bool open)
        {
            if(disposed)return;
            CancelTrigger();IsOpen=open;
            overlay.EnableInClassList("hidden",!open);dashboard.EnableInClassList("hidden",open);
            if(open) image.Focus();
            else root.Q<Button>("goToCurrent").Focus();
        }
        private void CancelTrigger() { pressed=false;pressedBlock=null; }

        /// <summary>The fitted camera image in panel pixels; letterbox bars are not a firing surface.</summary>
        public Rect PlayImageBounds
        {
            get
            {
                var bounds=image.worldBound;
                float aspect=(float)(view.DisplayPlane.Width/view.DisplayPlane.Height);
                float width=Mathf.Min(bounds.width,bounds.height*aspect),height=width/aspect;
                return new Rect(bounds.center-new Vector2(width,height)*0.5f,new Vector2(width,height));
            }
        }

        public void Tick(double deltaSeconds)
        {
            if(disposed || root.panel==null)return;
            var input=Controls.Read(root.panel);
            if(!input.Focused)
            {
                source?.Publish(null,null);
                if(IsOpen)SetOpen(false);
                CancelTrigger();return;
            }
            if(IsOpen && input.ReturnToAdministrator)SetOpen(false);
            bool active=IsOpen && session.DesktopInputSelected && !session.IsBusy;
            Rect bounds=PlayImageBounds;
            bool inside=active && bounds.width>0 && bounds.height>0 && bounds.Contains(input.Pointer);
            if(active)
            {
                if(input.Reset)Model.Reset();
                if(input.Reload)session.StationReload();
                Model.Move(input.Movement,deltaSeconds);
                if(inside)Model.SetAim(new Vector2((input.Pointer.x-bounds.xMin)/bounds.width,
                    1-(input.Pointer.y-bounds.yMin)/bounds.height));
            }
            source?.Publish(Model.Head,Model.Weapon);
            if(view.SessionControlsVisible && !session.HasOpenDesktopRecording && session.DesktopInputSelected && !view.IsReplay)
                session.ShowDesktopPreview(Model.Head,Model.Weapon);

            // A complete click must begin and end inside this play surface in
            // the same running block. Menu clicks, focus loss and held buttons
            // across phase changes never manufacture a trigger edge.
            if(!inside || !session.CanDesktopFire || pressedBlock!=session.DesktopBlockId)CancelTrigger();
            if(inside && session.CanDesktopFire && input.Pressed)
            { pressed=true;pressedBlock=session.DesktopBlockId; }
            if(input.Released)
            {
                bool fire=pressed && inside && session.CanDesktopFire && pressedBlock==session.DesktopBlockId;
                CancelTrigger();if(fire)session.SubmitDesktopTrigger();
            }
            UpdateHud(bounds);
        }

        private void UpdateHud(Rect bounds)
        {
            if(!IsOpen)return;
            root.Q<Label>("playPhase").text=root.Q<Label>("state").text;
            root.Q<Label>("playTimer").text=root.Q<Label>("countdown").text;
            root.Q<Label>("playFeedback").text=session.DesktopFeedback;
            root.Q<Label>("playScore").text=session.DesktopScore;
            var hints=Controls as IDesktopControlHints;
            string reload=hints==null?"use reload control":hints.ReloadHint+" reload";
            string reset=hints==null?"use reset control":hints.ResetViewHint+" reset viewpoint";
            root.Q<Label>("playControlsHint").text="Move mouse to aim · click and release to fire · "+
                reload+" · WASD move · Q/E height · "+reset;
            root.Q<Label>("playGuidance").text=session.DesktopInputSelected
                ? root.Q<Label>("nextActionHint").text : "Automated synthetic input is selected. Change input source in Preparation before creating the next recording.";
            image.style.visibility=view.ParticipantCamera.enabled?Visibility.Visible:Visibility.Hidden;
            reticle.EnableInClassList("hidden",!session.DesktopInputSelected || !view.ParticipantCamera.enabled);
            Vector2 at=new Vector2(bounds.xMin+Model.Aim.x*bounds.width,bounds.yMax-Model.Aim.y*bounds.height);
            Vector2 local=overlay.WorldToLocal(at);
            reticle.style.left=local.x-8;reticle.style.top=local.y-8;
            primary.text=session.DesktopPrimaryLabel;
            primary.SetEnabled(session.DesktopPrimaryEnabled);
        }
        private void PrimaryAction() { CancelTrigger();session.DesktopPrimaryAction(); }
        public void Dispose() { if(disposed)return;Detach();disposed=true; }
    }
}
