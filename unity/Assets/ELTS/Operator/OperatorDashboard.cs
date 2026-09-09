#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Presentation coordinator for the operator workbench. Module definitions,
    /// workspace persistence, camera presentation and session actions remain
    /// separate so new administrator tasks do not change the session engine.
    /// </summary>
    public sealed partial class OperatorDashboard : IDisposable
    {
        public sealed class Module
        {
            public readonly string Id, Name, Title, Description;
            public Module(string id,string name,string title,string description)
            {Id=id;Name=name;Title=title;Description=description;}
        }
        public static readonly Module[] Modules = {
            new Module("preparation","Preparation","Prepare a test session","Choose the test identity and condition order. Preflight observes the synthetic recording path before you continue."),
            new Module("calibration","Calibration","Review the calibration fixture","Capture, inspect and accept one development fixture. Physical calibration remains a separate validation step."),
            new Module("run","Practice and blocks","Carry out the session","Keep the live view in sight. The session engine controls timing and tells you when the next action is available."),
            new Module("notes","Notes and markers","Record an observation","Capture what the administrator saw, without leaving the session or losing the current task."),
            new Module("recordings","Recordings and replay","Keep the test record","Inspect the current destination and writer state. Completed attempts remain available for later analysis."),
            new Module("devices","Devices and diagnostics","Inspect the test connections","These sources are simulated. View bindings and exercise the mock controller without connecting to hardware."),
            new Module("checkpoints","Administrator checkpoints","Make the procedure yours","Keep your local reminders in the order that makes sense to you. Add, edit, reorder and undo removals here.")
        };

        private readonly VisualElement root, surface;
        private readonly DevelopmentView view;
        private readonly Func<DashboardSessionSnapshot> read;
        private readonly Dictionary<string,Label> moduleSummaries=new Dictionary<string,Label>();
        private readonly Dictionary<string,VisualElement> moduleRows=new Dictionary<string,VisualElement>();
        private readonly Dictionary<string,Vector2> scrollPositions=new Dictionary<string,Vector2>();
        private readonly Dictionary<string,DashboardReorder> reorders=new Dictionary<string,DashboardReorder>();
        private readonly List<DashboardResize> resizeManipulators=new List<DashboardResize>();
        private DashboardOrbit? orbitManipulator;
        private readonly RenderTexture participantTexture,operatorTexture;
        private IVisualElementScheduledItem? pendingSave;
        private string currentModule="", workspaceFailure="";
        private bool disposed, overlayOpen;
        private VisualElement? previousFocus;
        public DashboardWorkspace Workspace { get; private set; }
        public string WorkspaceFile { get; }
        public IReadOnlyDictionary<string,DashboardReorder> Reorders => reorders;
        public string SelectedModule => currentModule;

        public OperatorDashboard(VisualElement root,DevelopmentView view,Func<DashboardSessionSnapshot> read,string workspaceFile)
        {
            this.root=root;this.view=view;this.read=read;WorkspaceFile=workspaceFile;
            surface=Q<VisualElement>("sessionPanel");
            Workspace=new DashboardWorkspace();
            if(File.Exists(workspaceFile))
            {
                try {Workspace=DashboardWorkspace.Load(workspaceFile);}
                catch(Exception exception) {workspaceFailure=exception.Message;}
            }
            BuildModuleRail();InitializeOrders();InitializeCheckpoints();InitializeSettings();
            // Shared by the small dashboard preview and full desktop play view.
            int participantWidth=1920;
            int participantHeight=Math.Max(128,(int)Math.Round(participantWidth*view.DisplayPlane.Height/view.DisplayPlane.Width));
            participantTexture=Texture("Dashboard participant",participantWidth,participantHeight);
            operatorTexture=Texture("Dashboard apparatus",640,400);
            Q<Image>("participantImage").image=participantTexture;Q<Image>("participantImage").scaleMode=ScaleMode.ScaleToFit;
            Q<Image>("operatorImage").image=operatorTexture;Q<Image>("operatorImage").scaleMode=ScaleMode.ScaleToFit;
            InitializeLiveViews();
            On("goToCurrent",FocusCurrentTask);
            On("newCheckpointShortcut",()=> {SelectModule("checkpoints");AddCheckpoint();});
            Q<TextField>("participant").RegisterValueChangedCallback(_=>Refresh(true));
            surface.RegisterCallback<GeometryChangedEvent>(_=>ApplyWidths());
            SelectModule(Workspace.SelectedModule,false);
            ApplyWidths();ApplyMotion();SetVisible(true);Refresh(true);
            if(workspaceFailure.Length>0)ShowWorkspaceError();
        }
        private T Q<T>(string name) where T:VisualElement
            => root.Q<T>(name)??throw new InvalidOperationException("Dashboard element missing: "+name);
        private void On(string name,Action action)=>Q<Button>(name).clicked+=action;
        private void Text(string name,string text)
        {var label=Q<Label>(name);if(label.text!=text)label.text=text;}
        private static void Show(VisualElement element,bool visible)=>element.EnableInClassList("hidden",!visible);
        private static RenderTexture Texture(string name,int width,int height)
        {var texture=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32){name=name};texture.Create();return texture;}
        public void SetVisible(bool visible)
        {
            if(disposed)return;
            view.ParticipantCamera.targetTexture=visible?participantTexture:null;
            view.OperatorCamera.targetTexture=visible?operatorTexture:null;
        }
        private void BuildModuleRail()
        {
            var list=Q<VisualElement>("moduleList");var scroll=Q<ScrollView>("moduleScroll");
            foreach(string id in Workspace.ModuleOrder)
            {
                var module=Modules.First(item=>item.Id==id);
                var row=new VisualElement {name="module-"+id,userData=id};row.AddToClassList("module-row");
                var handle=Handle("Arrange "+module.Name);row.Add(handle);
                var button=new Button(()=>SelectModule(id)){name="select-"+id};button.AddToClassList("module-select");
                var title=new Label(module.Name);title.AddToClassList("module-name");button.Add(title);
                var value=new Label();value.AddToClassList("module-value");button.Add(value);row.Add(button);list.Add(row);
                moduleRows[id]=row;moduleSummaries[id]=value;
                RegisterReorder("module:"+id,row,list,scroll,handle,()=> {
                    Workspace.ModuleOrder=list.Children().Select(item=>(string)item.userData).ToList();QueueSave();
                });
            }
        }
        public void SelectModule(string id,bool save=true)
        {
            var module=Modules.FirstOrDefault(item=>item.Id==id);
            if(module==null)throw new ArgumentException("Unknown dashboard module: "+id);
            if(currentModule==id)return;
            var scroll=Q<ScrollView>("stageScroll");
            if(currentModule.Length>0)scrollPositions[currentModule]=scroll.scrollOffset;
            foreach(var definition in Modules)
            {Show(Q<VisualElement>(definition.Id+"Module"),definition.Id==id);moduleRows[definition.Id].EnableInClassList("selected",definition.Id==id);}
            currentModule=id;Workspace.SelectedModule=id;
            Text("stageKicker",module.Name);Text("stageTitle",module.Title);Text("stageDescription",module.Description);
            Vector2 saved=scrollPositions.TryGetValue(id,out var at)?at:Vector2.zero;
            // Restore only this module's scroll after layout, never the whole viewport.
            scroll.schedule.Execute(()=> {if(currentModule==id)scroll.scrollOffset=saved;}).ExecuteLater(1);
            if(save)QueueSave();
        }
        public void FocusCurrentTask()=>SelectModule(CurrentTask(read()));
        private static string CurrentTask(DashboardSessionSnapshot state)
        {
            if(!state.State.HasValue || state.State==SessionState.Idle || state.State==SessionState.SessionSetup)return "preparation";
            if(state.State==SessionState.Calibration)return "calibration";
            if(state.State==SessionState.SessionComplete)return "recordings";
            return "run";
        }
        public void Refresh(bool immediate=false)
        {
            if(disposed)return;
            var state=read();
            string current=CurrentTask(state);
            foreach(var row in moduleRows)row.Value.EnableInClassList("current-task",row.Key==current);
            string phase=PhaseName(state.State);
            Show(Q<Button>("createSession"),!state.State.HasValue || state.Closed);
            Show(Q<Button>("advance"),state.State.HasValue && !state.Closed && state.State!=SessionState.BlockReady && state.State!=SessionState.BlockRunning);
            Show(Q<Button>("startBlock"),state.State==SessionState.BlockReady || state.State==SessionState.BlockRunning);
            Show(Q<Button>("endBlock"),state.State==SessionState.BlockRunning);
            moduleSummaries["preparation"].text=Q<TextField>("participant").value;
            moduleSummaries["calibration"].text=state.Calibrated?"Development fixture accepted":"Fixture review pending";
            moduleSummaries["run"].text=phase+(state.State.HasValue?" · "+Math.Min(4,state.BlockIndex+1)+" of 4":"");
            moduleSummaries["notes"].text=String.IsNullOrWhiteSpace(Q<TextField>("note").value)?"Timestamped observations":"Draft kept here";
            moduleSummaries["recordings"].text=state.WriterOpen?"Recording open":state.RunDirectory.Length>0?"Recording closed":"No recording yet";
            moduleSummaries["devices"].text="Synthetic sources · mock link";
            moduleSummaries["checkpoints"].text=Workspace.Checkpoints.Count==0?"Add local reminders":Workspace.Checkpoints.Count(item=>item.Completed)+" of "+Workspace.Checkpoints.Count+" marked";
            Text("state",phase);
            Text("countdown",state.State.HasValue && state.RemainingSeconds>0?ClockText(state.RemainingSeconds)+" remaining":state.State==SessionState.BlockReady?"05:00 block":"No timer running");
            Text("nextActionHint",Guidance(state));Text("runGuidance",Guidance(state));
            double duration=state.Running?300:state.State==SessionState.Practice?state.PracticeSeconds:state.State==SessionState.Break?state.BreakSeconds:0;
            Q<ProgressBar>("sessionProgress").value=duration>0?(float)Math.Max(0,Math.Min(100,100*(1-state.RemainingSeconds/duration))):0;
            Q<Button>("advance").text=state.State==SessionState.SessionSetup?"Continue to calibration":state.State==SessionState.Calibration?"Continue to practice":state.State==SessionState.BlockEnded?"Continue to break":"Continue";
            Q<Button>("viewTools").SetEnabled(!state.Busy && (!state.State.HasValue || (state.Closed && !state.WriterOpen)));
            Q<Button>("openRecordingFolder").SetEnabled(state.RunDirectory.Length>0);
            Text("recordingHealth",state.WriterOpen?(state.WriterHealthy?"Writer active. Files close after completion or abort.":"Writer not healthy. Inspect the session error before continuing."):state.RunDirectory.Length>0?"Writer closed. The recorded attempt is retained.":"Create a session to reserve a new recording folder.");
            Text("liveMode",state.WriterOpen?"Synthetic recording":"Synthetic preview");
            bool previewReady=view.ParticipantCamera.enabled;
            Q<Image>("participantImage").style.visibility=previewReady?Visibility.Visible:Visibility.Hidden;
            Text("participantStatus",previewReady?"Stimulus only · synthetic geometry":"Preview unavailable. A valid synthetic head pose is required.");
            RefreshPreflight(state);RefreshBlocks(state);SetPreparationEditable(state.PreparationEditable);
        }
        private static string ClockText(double seconds)=>TimeSpan.FromSeconds(Math.Max(0,seconds)).ToString(@"mm\:ss");
        private static string PhaseName(SessionState? state)
        {
            switch(state)
            {
                case SessionState.Idle:return "Checking preflight";
                case SessionState.SessionSetup:return "Preflight complete";
                case SessionState.Calibration:return "Calibration";
                case SessionState.Practice:return "Practice";
                case SessionState.BlockReady:return "Ready for block";
                case SessionState.BlockRunning:return "Block running";
                case SessionState.BlockEnded:return "Block complete";
                case SessionState.Break:return "Break";
                case SessionState.SessionComplete:return "Session complete";
                case SessionState.Aborted:return "Session aborted";
                case SessionState.Failed:return "Session failed";
                default:return "Ready to prepare";
            }
        }
        private static string Guidance(DashboardSessionSnapshot state)
        {
            if(state.Busy)return "Finishing a recording operation. Session actions return when it completes.";
            switch(state.State)
            {
                case SessionState.Idle:return "Waiting for two seconds of fresh synthetic tracking and a healthy writer.";
                case SessionState.SessionSetup:return "Preflight is complete for synthetic use. Continue to the calibration fixture.";
                case SessionState.Calibration:return state.Calibrated?"Fixture accepted for development. Continue to practice.":"Generate a fixture, review its residuals, then accept it before continuing.";
                case SessionState.Practice:return "Practice is running. The first block becomes available when the practice timer ends.";
                case SessionState.BlockReady:return "Confirm the administrator is ready, then start the next 5-minute block.";
                case SessionState.BlockRunning:return "The block ends automatically at its recorded deadline. Use Abort session to stop early.";
                case SessionState.BlockEnded:return "The block has ended. Continue to the break, or explicitly rerun this block in a new recording.";
                case SessionState.Break:return "Continue becomes available when the configured break has elapsed.";
                case SessionState.SessionComplete:return "The session is complete. Wait for recording closure before opening replay tools.";
                case SessionState.Aborted:return "The session is stopped. A rerun requires a new recording; it will not resume automatically.";
                case SessionState.Failed:return "Inspect the error and recording before starting another session.";
                default:return "Prepare the test identifier and condition order, then create a synthetic recording.";
            }
        }

        private void InitializeSettings()
        {
            On("workspaceSettings",()=>SetSettings(true));On("closeSettings",()=>SetSettings(false));
            Q<VisualElement>("settingsBackdrop").RegisterCallback<PointerDownEvent>(evt=> {if(evt.target==evt.currentTarget)SetSettings(false);});
            surface.RegisterCallback<KeyDownEvent>(evt=> {
                if(overlayOpen && evt.keyCode==KeyCode.Escape){SetSettings(false);evt.StopImmediatePropagation();}
            },TrickleDown.TrickleDown);
            Q<Toggle>("reducedMotion").SetValueWithoutNotify(Workspace.ReducedMotion);
            Q<Toggle>("reducedMotion").RegisterValueChangedCallback(evt=>{Workspace.ReducedMotion=evt.newValue;ApplyMotion();QueueSave();});
            Q<Slider>("railWidth").SetValueWithoutNotify(Workspace.RailWidth);
            Q<Slider>("liveWidth").SetValueWithoutNotify(Workspace.LiveWidth);
            Q<Slider>("railWidth").RegisterValueChangedCallback(evt=>{SetRailWidth(evt.newValue);QueueSave();});
            Q<Slider>("liveWidth").RegisterValueChangedCallback(evt=>{SetLiveWidth(evt.newValue);QueueSave();});
            var railResize=new DashboardResize(()=>Workspace.RailWidth,SetRailWidth,QueueSave,1);
            var liveResize=new DashboardResize(()=>Workspace.LiveWidth,SetLiveWidth,QueueSave,-1);
            resizeManipulators.Add(railResize);resizeManipulators.Add(liveResize);
            Q<VisualElement>("railSplitter").AddManipulator(railResize);
            Q<VisualElement>("liveSplitter").AddManipulator(liveResize);
            On("resetWorkspaceLayout",()=> {
                Workspace.ModuleOrder=new List<string>(DashboardWorkspace.BuiltinModules);Workspace.LiveOrder=new List<string>(DashboardWorkspace.BuiltinLiveModules);
                foreach(string id in Workspace.ModuleOrder)Q<VisualElement>("moduleList").Add(moduleRows[id]);
                foreach(string id in Workspace.LiveOrder)Q<VisualElement>("liveList").Add(Q<VisualElement>(id+"Card"));
                SetRailWidth(240);SetLiveWidth(380);QueueSave();
            });
            On("recoverWorkspace",()=> {
                try {
                    if(File.Exists(WorkspaceFile))File.Move(WorkspaceFile,WorkspaceFile+".unreadable-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
                    workspaceFailure="";FlushWorkspace();Show(Q<Button>("recoverWorkspace"),false);Text("workspaceError","");
                } catch(Exception exception){workspaceFailure=exception.Message;ShowWorkspaceError();}
            });
        }
        private void SetSettings(bool open)
        {
            if(open)previousFocus=root.panel?.focusController.focusedElement as VisualElement;
            overlayOpen=open;Show(Q<VisualElement>("settingsBackdrop"),open);
            if(open)Q<Toggle>("reducedMotion").Focus();
            else if(previousFocus?.panel==root.panel && previousFocus.enabledInHierarchy)previousFocus.Focus();
            else Q<Button>("workspaceSettings").Focus();
        }
        private void SetRailWidth(float width){Workspace.RailWidth=Mathf.Clamp(width,210,330);Q<Slider>("railWidth").SetValueWithoutNotify(Workspace.RailWidth);ApplyWidths();}
        private void SetLiveWidth(float width){Workspace.LiveWidth=Mathf.Clamp(width,280,500);Q<Slider>("liveWidth").SetValueWithoutNotify(Workspace.LiveWidth);ApplyWidths();}
        private void ApplyWidths()
        {
            float width=surface.resolvedStyle.width;if(!Single.IsFinite(width) || width<1)return;
            float rail=Workspace.RailWidth,live=Workspace.LiveWidth;
            float room=width-340-14;
            if(rail+live>room){float ratio=Mathf.Max(0.65f,room/(rail+live));rail*=ratio;live*=ratio;}
            Q<VisualElement>("workspaceRail").style.width=rail;Q<VisualElement>("livePanel").style.width=live;
        }
        private void ApplyMotion()=>surface.EnableInClassList("reduced-motion",Workspace.ReducedMotion);
        private void QueueSave()
        {
            if(workspaceFailure.Length>0){ShowWorkspaceError();return;}
            Text("workspaceSaveStatus","Saving workspace locally");pendingSave?.Pause();
            pendingSave=surface.schedule.Execute(FlushWorkspace).StartingIn(300);
        }
        public void FlushWorkspace()
        {
            pendingSave?.Pause();
            if(workspaceFailure.Length>0)return;
            try {Workspace.Save(WorkspaceFile);Text("workspaceSaveStatus","Workspace saved locally");Text("workspaceError","");}
            catch(Exception exception){Text("workspaceSaveStatus","Workspace not saved · open settings");Text("workspaceError",exception.Message);}
        }
        private void ShowWorkspaceError()
        {Text("workspaceSaveStatus","Settings need attention · open settings");Text("workspaceError",workspaceFailure+" Use recovery to retain the unreadable file before saving fresh settings.");Show(Q<Button>("recoverWorkspace"),true);}
        public void Dispose()
        {
            if(disposed)return;
            foreach(var manipulator in reorders.Values.ToList())manipulator.Dispose();
            foreach(var manipulator in resizeManipulators)manipulator.Dispose();
            orbitManipulator?.Dispose();pendingSave?.Pause();FlushWorkspace();SetVisible(false);disposed=true;
            participantTexture.Release();operatorTexture.Release();
            UnityEngine.Object.Destroy(participantTexture);UnityEngine.Object.Destroy(operatorTexture);
        }
    }
}
