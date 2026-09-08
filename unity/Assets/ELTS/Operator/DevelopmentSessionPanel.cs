#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Elts.Clock;
using Elts.Config;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using Elts.Tracking;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Operator-only development workflow. The panel consumes immutable config
    /// and calls the pure session engine; recording and acquisition own their
    /// background work. Every displayed readiness substitution is synthetic.
    /// </summary>
    public sealed class DevelopmentSessionPanel : MonoBehaviour
    {
        private const double ValidTrackingSeconds = 2;
        private const double MaximumSampleAgeSeconds = 0.25;
        private const long MinimumDiskBytes = 2L * 1024 * 1024 * 1024;
        private const string FixtureIdentity = "operator-synthetic-room-motion-v1";
        private DevelopmentView view = null!;
        private UIDocument document = null!;
        private PanelSettings panelSettings = null!;
        private VisualElement root = null!;
        private SessionEngine? engine;
        private SessionRecordingAdapter? recording;
        private DevelopmentSessionAcquisition? acquisition;
        private ISharedClock? clock;
        private SessionEventSequence? sequence;
        private DevelopmentSessionScenario? scenario;
        private Task? operation;
        private string lastMessage = "", runId = "";
        private long diskFreeBytes;
        private double firstValidAt = -1;
        private bool setupPending, closing, destroyed;
        private readonly Dictionary<string,Vector3d> visibleTargets = new Dictionary<string,Vector3d>();

        public SessionEngine? Engine => engine;
        public VisualElement PanelRoot => root;
        public bool IsBusy => operation != null;

        private void Awake()
        {
            view = GetComponent<DevelopmentView>();
            if(view == null || !view.Ready) { enabled=false; return; }
            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
            panelSettings.sortingOrder = 20;
            document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            var template = Resources.Load<VisualTreeAsset>("ELTS/SessionPanel");
            if(template == null) throw new InvalidOperationException("Session panel asset is missing.");
            root = document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.unityFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            template.CloneTree(root);
            Field("conditionOrder").value = String.Join(",", view.Configuration.Session.ConditionOrder);
            Bind("createSession", () => Begin(CreateSessionAsync));
            Bind("advance", () => Act(() => engine!.Advance()));
            Bind("startBlock", () => Act(() => engine!.StartBlock()));
            Bind("endBlock", () => Act(() => engine!.EndBlock()));
            Bind("fire", () => Act(() => scenario!.Fire(acquisition?.Snapshot?.Weapon.Pose,acquisition?.Snapshot?.Head.Pose)));
            Bind("abort", () => Act(() => engine!.Abort(Field("abortReason").value)));
            Bind("rerun", () => Begin(RerunAsync));
            Bind("addNote", () => Act(() => { engine!.AddNote(Field("note").value); Field("note").value=""; }));
            Bind("syncMarker", () => Act(() => engine!.EmitProvisionalSyncMarker("operator-button")));
            Bind("viewTools", () => SetVisible(false));
            SetVisible(true);
            RefreshLabels();
        }

        public void SetVisible(bool visible)
        {
            view.SessionControlsVisible=visible;
            if(root != null) root.style.display=visible?DisplayStyle.Flex:DisplayStyle.None;
        }
        private TextField Field(string name) => root.Q<TextField>(name);
        private void Bind(string name, Action action) => root.Q<Button>(name).clicked += action;
        private void Label(string name, string text) => root.Q<Label>(name).text=text;
        private void Act(Action action)
        {
            if(IsBusy || engine == null) return;
            try { action(); lastMessage=""; }
            catch(Exception exception) { lastMessage=exception.Message; }
            RefreshLabels();
        }
        private void Begin(Func<Task> action)
        {
            if(IsBusy) return;
            operation=RunOperation(action);
        }
        private async Task RunOperation(Func<Task> action)
        {
            try { await action(); }
            catch(Exception exception) { lastMessage=exception.Message; }
        }

        private async Task CreateSessionAsync()
        {
            if(engine != null && engine.State != SessionState.SessionComplete
                && engine.State != SessionState.Aborted && engine.State != SessionState.Failed)
                throw new InvalidOperationException("Finish or abort the current session first.");
            var config=view.Configuration;
            runId=LoggingRunDirectory.ValidateRunId(Field("participant").value.Trim());
            var plan=new SessionPlan(runId, Field("conditionOrder").value.Split(',').Select(value=>value.Trim()));
            await CloseRecordingAsync();
            clock=new SharedMonotonicClock();
            sequence=new SessionEventSequence();
            string dataRoot=Path.GetFullPath(Path.Combine(Application.dataPath,"..",config.Machine.DataRoot));
            diskFreeBytes=await Task.Run(()=>new DriveInfo(Path.GetPathRoot(dataRoot)!).AvailableFreeSpace);
            if(diskFreeBytes<MinimumDiskBytes) throw new IOException("At least 2 GB free disk space is required.");
            // Application.version is the packaged build identity. The release
            // manifest maps that token to its complete source revision.
            var provenance=new SessionProvenance(Application.version, "build-"+Application.version,
                config.EffectiveSha256,config.SourceRawSha256["scenario"],Hash(FixtureIdentity),true,clock.UtcStartupAnchor);
            var limits=new LoggingConfiguration(config.Runtime.SampleQueueCapacity,config.Runtime.EventQueueCapacity,
                TimeSpan.FromSeconds(config.Runtime.LogFlushSeconds));
            recording=new SessionRecordingAdapter(dataRoot,provenance,limits);
            if(!await recording.ReserveAndStartAsync(runId)) throw new IOException("The recording could not be started.");
            engine=new SessionEngine(clock,sequence,plan,
                new SessionTiming(config.Session.PracticeDurationSeconds,config.Session.BreakDurationSeconds,true),
                recording,new DevelopmentSessionLink());
            scenario=new DevelopmentSessionScenario(config,clock,engine,recording,sequence);
            StartAcquisition(config);
            firstValidAt=-1; setupPending=true; closing=false;
            lastMessage="Checking two seconds of synthetic tracking…";
        }

        private void StartAcquisition(DevelopmentConfiguration config)
        {
            var screen=config.Rig.Display;
            var head=screen.Origin+screen.U*(screen.Width*0.5)+screen.V*(screen.Height*0.5)-screen.Normal*2;
            var motion=new SyntheticMotionSettings(head,new Vector3d(0.02,0.01,0),0.3,
                head+new Vector3d(0.15,-0.18,0.1),new Vector3d(0.01,0.01,0),0.2,0.02,0.2);
            var settings=new SyntheticTrackingSettings(clock!,config.Scenario.Seed,config.Runtime.SampleRateHz,
                config.Rig.HeadSerial,config.Rig.WeaponSerial,TimeSpan.FromSeconds(config.Runtime.TriggerLockoutSeconds),motion:motion);
            acquisition=new DevelopmentSessionAcquisition(settings,recording!.TryLogSample);
            acquisition.Start();
        }

        private async Task RerunAsync()
        {
            if(engine == null || recording == null || !engine.CanRerun) return;
            await CloseRecordingAsync();
            if(!await recording.ReserveAndStartAsync(runId)) throw new IOException("The rerun recording could not be started.");
            engine.RerunCurrentBlock();
            scenario=new DevelopmentSessionScenario(view.Configuration,clock!,engine,recording,sequence!);
            StartAcquisition(view.Configuration);
            closing=false;
        }

        private async Task CloseRecordingAsync()
        {
            if(acquisition != null) { await acquisition.StopAsync(); acquisition=null; }
            if(recording != null && recording.IsOpen && !await recording.CloseAsync())
                throw new IOException("Recording closure failed; retained files require inspection.");
        }

        private void Update()
        {
            if(root == null) return;
            if(operation != null && operation.IsCompleted) operation=null;
            if(engine != null && !IsBusy)
            {
                var pair=acquisition?.Snapshot;
                if(setupPending && engine.State==SessionState.Idle && pair.HasValue)
                {
                    if(pair.Value.Head.HasValidPose && pair.Value.Weapon.HasValidPose
                        && clock!.Now.Ticks-pair.Value.Head.Acquisition.Timestamp.Ticks<=MaximumSampleAgeSeconds*TimeSpan.TicksPerSecond)
                    {
                        if(firstValidAt<0) firstValidAt=clock!.Now.Elapsed.TotalSeconds;
                        if(clock!.Now.Elapsed.TotalSeconds-firstValidAt>=ValidTrackingSeconds)
                        {
                            // These are explicitly emulated prerequisites for
                            // synthetic mode. The core can never grant StudyReady.
                            var report=engine.StartSetup(new SessionReadinessInput {
                                TwoDisplays=true,SteamVrRunning=true,TrackersBoundAndValidForTwoSeconds=true,
                                ConfigurationHashesComputed=true,DiskFreeBytes=diskFreeBytes,
                                LogWriterAlive=recording!.WriterHealthy,WeLinkReachable=true });
                            setupPending=false;
                            lastMessage=report.SetupAccepted?"Synthetic checks passed. Continue to calibration placeholders.":String.Join(" ",report.Failures);
                        }
                    }
                    else firstValidAt=-1;
                }
                if(acquisition?.Failure != null && engine.State != SessionState.Aborted && engine.State != SessionState.Failed)
                    engine.Abort("Synthetic acquisition failed: "+acquisition.Failure);
                try
                {
                    engine.Tick();
                    scenario?.Refresh(Time.unscaledDeltaTime,pair?.Head.Pose);
                }
                catch(Exception exception)
                {
                    lastMessage=exception.Message;
                    engine.Abort("Scenario or logging failure: "+exception.Message);
                }
                view.SetSessionFrame(pair?.Head.Pose,pair?.Weapon.Pose,scenario?.Positions??visibleTargets);
                if(!closing && (engine.State==SessionState.SessionComplete || engine.State==SessionState.Aborted || engine.State==SessionState.Failed))
                { setupPending=false; closing=true; Begin(CloseRecordingAsync); }
            }
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            var config=view.Configuration;
            Label("configuration",config.Session.SessionId+" / "+config.Scenario.ScenarioId+"\nConfig "+config.EffectiveSha256.Substring(0,12)+" • 300 s blocks");
            Label("selfCheck","Emulated: displays, SteamVR and link. Observed: synthetic poses, config hash, disk and writer. Study remains unavailable.");
            Label("recording",recording?.RunDirectory??"No recording open");
            Label("state",engine?.State.ToString()??"Idle");
            Label("countdown",engine==null?"No block running":(engine.CurrentCondition??"All conditions finished")+" • "+engine.RemainingSeconds.ToString("F1")+" s");
            Label("message",IsBusy?"Recording operation in progress…":(engine?.Failure??lastMessage));
            Label("tracking",view.TrackingStatus);
            root.Q<Button>("createSession").SetEnabled(!IsBusy && (engine==null || closing));
            root.Q<Button>("advance").SetEnabled(!IsBusy && engine?.CanAdvance==true);
            root.Q<Button>("startBlock").SetEnabled(!IsBusy && engine?.State==SessionState.BlockReady);
            root.Q<Button>("endBlock").SetEnabled(!IsBusy && engine?.State==SessionState.BlockRunning && engine.RemainingSeconds<=0);
            root.Q<Button>("fire").SetEnabled(!IsBusy && engine?.State==SessionState.BlockRunning);
            root.Q<Button>("abort").SetEnabled(!IsBusy && engine!=null && !closing);
            root.Q<Button>("rerun").SetEnabled(!IsBusy && engine?.CanRerun==true);
            root.Q<Button>("addNote").SetEnabled(!IsBusy && engine!=null && !closing);
            root.Q<Button>("syncMarker").SetEnabled(!IsBusy && engine!=null && !closing);
        }

        private static string Hash(string text)
        {
            using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-","").ToLowerInvariant();
        }
        private async void OnDestroy()
        {
            if(destroyed) return;
            destroyed=true;
            try { if(operation!=null) await operation; await CloseRecordingAsync(); }
            catch(Exception exception) { Debug.LogError("ELTS_SESSION_CLOSE_FAIL "+exception.Message); }
            if(panelSettings!=null) Destroy(panelSettings);
        }
        /// <summary>DEV-08 lifecycle fixture; DEV-11 supplies the protocol adapter.</summary>
        private sealed class DevelopmentSessionLink : ISessionLink
        {
            public bool TrySetEnabled(bool enabled) => true;
        }
    }
}
