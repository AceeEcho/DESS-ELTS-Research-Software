#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Geometry;
using Elts.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Process=System.Diagnostics.Process;
using ProcessStartInfo=System.Diagnostics.ProcessStartInfo;

namespace Elts.Operator
{
    /// <summary>
    /// Two-window desktop station: Unity owns participant input/rendering and the
    /// recorded experiment; a resizable loopback browser owns administrator UI.
    /// No browser action can manufacture a participant trigger.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class DevelopmentTestStation : MonoBehaviour
    {
        // Capture enough detail for a large dashboard pane instead of enlarging a thumbnail.
        // Height preserves the outside view's 16:10 aspect ratio; cadence stays independent.
        [SerializeField, Range(320,3840)] private int previewWidth=1920;
        [SerializeField, Range(1,30)] private int previewFramesPerSecond=10;
        [SerializeField, Range(40,95)] private int previewJpegQuality=90;
        [SerializeField, Range(15,120)] private int participantFramesPerSecond=60;
        private int previousFrameRate,previousVSync;
        private bool ownsFramePacing;
        private int previewHeight;
        private bool capturePending;
        private int windowWidth=1100,windowHeight=700;
        private DevelopmentSessionPanel session=null!;
        private DevelopmentView view=null!;
        private AdministratorServer? server;
        private GameObject uiHost=null!;
        private PanelSettings settings=null!;
        private VisualElement ui=null!,prompt=null!;
        private Label title=null!,subtitle=null!,reticle=null!,ammo=null!;
        private RenderTexture outsideTexture=null!;
        private Texture2D capture=null!;
        private bool held,wasArmed;
        private string? heldBlock;
        private long imageVersion;
        private float orbitYaw=145,orbitPitch=20,orbitDistance=4,nextState;
        private readonly List<RunRow> blocks=new List<RunRow>();
        private string knownRun="";
        private sealed class RunRow
        {
            public string condition="",attempt="",status="Running";
            public int shots,hits;
            public double activeSeconds;
        }
        public IDesktopControls Controls {get;set;}=new MouseKeyboardControls();
        public string AdministratorUrl=>server?.Url??"";
        public VisualElement ParticipantRoot=>ui;
        public bool OpenAdministratorAutomatically {get;set;}=true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0)return;
            if(SceneManager.GetActiveScene().name!="ELTSDesktop")return;
            var panel=FindFirstObjectByType<DevelopmentSessionPanel>();
            if(panel!=null && panel.GetComponent<DevelopmentTestStation>()==null)
                panel.gameObject.AddComponent<DevelopmentTestStation>();
        }
        private void Awake()
        {
            session=GetComponent<DevelopmentSessionPanel>();view=GetComponent<DevelopmentView>();
            if(session==null || !view.Ready){enabled=false;return;}
            Application.runInBackground=true;
            if(!Application.isEditor)
            {
                // Leave GPU time for Windows wireless-display encoding. This
                // limits presentation only; the shared recording clock is unchanged.
                previousFrameRate=Application.targetFrameRate;
                previousVSync=QualitySettings.vSyncCount;
                QualitySettings.vSyncCount=0;
                Application.targetFrameRate=Mathf.Clamp(participantFramesPerSecond,15,120);
                ownsFramePacing=true;
            }
            Elts.Development.DevelopmentBanner.ShowInPlayer=false;
            session.EnableSeparateAdministrator();session.InitializeCollection();view.ParticipantOnly=true;
            view.ParticipantCamera.targetTexture=null;
            previewHeight=Mathf.RoundToInt(previewWidth*0.625f);
            outsideTexture=new RenderTexture(previewWidth,previewHeight,24,RenderTextureFormat.ARGB32){name="Administrator outside view"};outsideTexture.Create();
            view.OperatorCamera.targetTexture=outsideTexture;
            // Render the administrator camera only when producing a preview frame.
            view.OperatorCamera.enabled=false;
            capture=new Texture2D(previewWidth,previewHeight,TextureFormat.RGB24,false);
            // UIDocuments beneath another UIDocument must share its panel. Keep
            // this independent root on its own participant-only panel instead.
            uiHost=new GameObject("Participant interface — no administrator controls");
            settings=ScriptableObject.CreateInstance<PanelSettings>();settings.scaleMode=PanelScaleMode.ConstantPixelSize;settings.sortingOrder=50;
            settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("ELTS/SessionTheme");
            var document=uiHost.AddComponent<UIDocument>();document.panelSettings=settings;
            ui=document.rootVisualElement;ui.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");ui.pickingMode=PickingMode.Ignore;
            // A resize invalidates the pixel coordinates of a held trigger.
            ui.RegisterCallback<GeometryChangedEvent>(_=>CancelClick());
            Resources.Load<VisualTreeAsset>("ELTS/ParticipantView").CloneTree(ui);
            prompt=ui.Q("participantPrompt");title=ui.Q<Label>("participantTitle");subtitle=ui.Q<Label>("participantSubtitle");reticle=ui.Q<Label>("participantReticle");ammo=ui.Q<Label>("participantAmmo");
            session.StationPublishPose(session.Desktop.Model.Head,session.Desktop.Model.Weapon);
            server=new AdministratorServer(Path.Combine(Application.streamingAssetsPath,"administrator"));
            // A local discovery file lets test tools locate this instance without
            // hardcoding ports. Its capability is rotated on each launch.
            string discovery=Path.Combine(Application.temporaryCachePath,"elts-administrator.json");
            File.WriteAllText(discovery,JsonConvert.SerializeObject(new{url=server.Url,pid=Process.GetCurrentProcess().Id}));
            Debug.Log("ELTS_ADMINISTRATOR_READY "+server.Url);
        }
        private IEnumerator Start()
        {
            if(!enabled)yield break;
            yield return null;
            if(!Application.isEditor)
            {
                // Preserve Windows display placement by default, including an
                // existing Miracast connection. Moving displays and maximizing
                // the render surface at startup are now explicit opt-ins.
                var arguments=Environment.GetCommandLineArgs();
                bool windowed=Array.IndexOf(arguments,"-eltsWindowed")>=0;
                if(!windowed && Array.IndexOf(arguments,"-eltsSecondaryDisplay")>=0)
                {
                    var displays=new List<DisplayInfo>();Screen.GetDisplayLayout(displays);
                    if(displays.Count>1)
                        yield return Screen.MoveMainWindowTo(displays[1],Vector2Int.zero);
                }
                if(!windowed && Array.IndexOf(arguments,"-eltsFullscreen")>=0)
                    SetFullscreen(true);
                else SetFullscreen(false);
            }
            bool suppress=Array.IndexOf(Environment.GetCommandLineArgs(),"-eltsNoAdminWindow")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(),"-eltsSmokeTest")>=0;
            if(OpenAdministratorAutomatically && !suppress)OpenAdministrator();
            StartCoroutine(CaptureOutsideView());
        }
        public void OpenAdministrator()
        {
            if(server==null)return;
            string[] browsers={
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Microsoft/Edge/Application/msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Google/Chrome/Application/chrome.exe")};
            string? browser=browsers.FirstOrDefault(File.Exists);
            if(browser!=null)Process.Start(new ProcessStartInfo(browser,"--app=\""+server.Url+"\" --window-size=1280,900 --new-window"){UseShellExecute=true});
            else Process.Start(new ProcessStartInfo(server.Url){UseShellExecute=true});
        }
        private void Update()
        {
            if(server==null || ui.panel==null)return;
            var keyboard=Keyboard.current;
            if(keyboard!=null && (keyboard.f11Key.wasPressedThisFrame ||
                (keyboard.enterKey.wasPressedThisFrame && (keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed))))
                SetFullscreen(!Screen.fullScreen);
            for(int i=0;i<8 && server.TryTakeCommand(out var command);i++)
            {
                if(!command.TryBegin())continue;
                try
                {
                    if(!IsDataAction((string?)command.Payload["action"]??"") && (string?)command.Payload["runId"]!=session.StationRunId)
                        throw new InvalidOperationException("The participant changed. Review the current session and try again.");
                    ApplyCommand(command.Payload);command.Complete("{\"ok\":true,\"message\":\"Action accepted.\"}");
                }
                catch(Exception exception){command.Complete(JsonConvert.SerializeObject(new{ok=false,message=exception.Message}));}
            }
            session.StationTick();
            ReadParticipantInput();
            // DevelopmentView.Update renders the latest pose later in this frame.
            // Avoid rebuilding scene presentation twice per participant frame.
            UpdateParticipantPrompt();UpdateRunRows();
            if(Time.unscaledTime>=nextState)
            {nextState=Time.unscaledTime+1f/Mathf.Max(10,previewFramesPerSecond);server.PublishState(StateJson());}
        }
        private static bool IsDataAction(string action)=>new[]{"refreshData","viewParticipant","viewDataRows","exportDatabase","exportWorkbook","exportParticipant","exportSessionRaw","openDataFolder","listRecordings","reviewRecording","exportRecording"}.Contains(action);
        public void ApplyCommand(JObject command)
        {
            string action=(string?)command["action"]??"";
            if((action=="pause" || action=="resume" || action=="stopTest" || action=="skipTest" || action=="skipBreak" || action=="startBreak" || action=="repeatTest") &&
                (string?)command["blockId"]!=(session.Engine?.CurrentBlockId??""))
                throw new InvalidOperationException("The test changed. Review the current test before trying again.");
            switch(action)
            {
                case "startParticipant":
                    session.StationStartParticipant((string?)command["participant"]??"",command["conditionOrder"]?.ToObject<string[]>()??session.StationOrder,
                        (string?)command["participantName"]??"",(string?)command["initialNotes"]??"");break;
                case "arm": session.StationArm();CancelClick();break;
                case "disarm": session.StationDisarm();CancelClick();break;
                case "pause": session.StationPause();CancelClick();break;
                case "resume": session.StationResume();CancelClick();break;
                case "stopTest": session.StationStop();CancelClick();break;
                case "setDuration":
                    session.StationSetDuration((string?)command["condition"]??"",(double?)command["seconds"]??Double.NaN);break;
                case "applySetup":
                    session.StationApplySetup(command["conditionOrder"]?.ToObject<string[]>()??Array.Empty<string>(),
                        command["durations"]?.ToObject<Dictionary<string,double>>()??new Dictionary<string,double>(),
                        (double?)command["practiceSeconds"]??Double.NaN,(double?)command["breakSeconds"]??Double.NaN);break;
                case "setPhaseDurations":
                    session.StationSetPhaseDurations((double?)command["practiceSeconds"]??Double.NaN,(double?)command["breakSeconds"]??Double.NaN);break;
                case "startBreak":session.StationStartBreak();CancelClick();break;
                case "skipBreak":session.StationSkipBreak();CancelClick();break;
                case "skipTest":session.StationSkipTest((string?)command["reason"]??"");CancelClick();break;
                case "repeatTest":session.StationRepeat((string?)command["condition"]??"",(string?)command["reason"]??"");CancelClick();break;
                case "generateCalibration":case "redoCalibration":case "acceptCalibration":
                case "startPractice":case "restartPractice":case "finishPractice":
                    session.StationPreparationAction(action);CancelClick();break;
                case "listRecordings":case "reviewRecording":case "exportRecording":
                    session.StationArchiveAction(action,(string?)command["id"]??"");break;
                case "refreshData":case "viewParticipant":case "viewDataRows":case "exportDatabase":case "exportWorkbook":case "exportParticipant":case "exportSessionRaw":
                    session.CollectionAction(command);break;
                case "openDataFolder":session.StationOpenDataFolder();break;
                case "next": session.StationNext();CancelClick();break;
                case "abort": session.StationAbort((string?)command["reason"]??"");CancelClick();break;
                case "note": session.StationNote((string?)command["text"]??"");break;
                case "viewOrbit":
                    orbitYaw=Finite(command,"yaw",orbitYaw);orbitPitch=Mathf.Clamp(Finite(command,"pitch",orbitPitch),-10,80);orbitDistance=Mathf.Clamp(Finite(command,"distance",orbitDistance),1.5f,12);
                    view.SetOperatorOrbit(orbitYaw,orbitPitch,orbitDistance);break;
                case "windowed": SetFullscreen(false);break;
                case "fullscreen": SetFullscreen(true);break;
                case "previewRate": previewFramesPerSecond=Mathf.Clamp((int)Finite(command,"fps",10),1,30);break;
                default:throw new ArgumentException("Unknown administrator action.");
            }
        }
        private void SetFullscreen(bool fullscreen)
        {
            CancelClick();
            if(Application.isEditor)return;
            if(fullscreen)
            {
                if(!Screen.fullScreen){windowWidth=Screen.width;windowHeight=Screen.height;}
                Screen.fullScreenMode=FullScreenMode.FullScreenWindow;
            }
            else if(Screen.fullScreen)Screen.SetResolution(windowWidth,windowHeight,FullScreenMode.Windowed);
        }
        private static float Finite(JObject command,string key,float fallback)
        {float value=(float?)command[key]??fallback;if(!float.IsFinite(value))throw new ArgumentException("Camera values must be finite.");return value;}
        private void ReadParticipantInput()
        {
            var input=Controls.Read(ui.panel);var bounds=ui.worldBound;
            bool inside=input.Focused && bounds.width>0 && bounds.height>0 && bounds.Contains(input.Pointer);
            bool armed=session.TestArmed;
            if(!input.Focused || armed!=wasArmed || heldBlock!=session.DesktopBlockId)CancelClick();
            if(input.Focused)
            {
                if(input.Reset)session.Desktop.Model.Reset();
                if(input.Reload)session.StationReload();
                session.Desktop.Model.Move(input.Movement,Time.unscaledDeltaTime);
                if(inside)session.Desktop.Model.SetAim(new Vector2((input.Pointer.x-bounds.xMin)/bounds.width,1-(input.Pointer.y-bounds.yMin)/bounds.height));
                if(input.ReturnToAdministrator)SetFullscreen(false);
            }
            // The virtual tracker holds its last pose while the administrator
            // has focus. Mouse edges are canceled; synthetic tracking continues.
            session.StationPublishPose(session.Desktop.Model.Head,session.Desktop.Model.Weapon);
            bool allowed=armed || session.CanDesktopFire;
            if(!inside || !allowed)CancelClick();
            if(inside && allowed && input.Pressed){held=true;heldBlock=session.DesktopBlockId;}
            if(input.Released)
            {bool fire=held && inside && allowed;CancelClick();if(fire)session.StationParticipantTrigger();}
            wasArmed=session.TestArmed;
            Vector2 at=new Vector2(session.Desktop.Model.Aim.x*bounds.width,(1-session.Desktop.Model.Aim.y)*bounds.height);
            reticle.style.left=at.x-9;reticle.style.top=at.y-9;
            if(!Application.isEditor)UnityEngine.Cursor.visible=!input.Focused;
        }
        private void CancelClick(){held=false;heldBlock=null;}
        private void UpdateParticipantPrompt()
        {
            bool running=session.Engine?.State==SessionState.BlockRunning;
            prompt.EnableInClassList("participant-hidden",running);
            reticle.EnableInClassList("participant-hidden",!running);
            ammo.EnableInClassList("participant-hidden",!running);
            string? reload=(Controls as IDesktopControlHints)?.ReloadHint;
            ammo.text="AMMO  "+session.StationAmmo+" / "+session.StationMagazineCapacity+"   ·   "+
                (reload==null?"RELOAD":reload.ToUpperInvariant()+" RELOAD");
            if(session.TestArmed){title.text="Shoot to begin";subtitle.text="Click and release when you are ready.";return;}
            switch(session.Engine?.State)
            {
                case SessionState.BlockPaused:title.text="Test paused";subtitle.text="Please wait for the administrator to resume.";break;
                case SessionState.SessionComplete:title.text="Test complete";subtitle.text="Thank you. Please wait for the administrator.";break;
                case SessionState.BlockEnded:title.text="Test finished";
                    subtitle.text="Please wait for the administrator to start your break.";break;
                case SessionState.Break:title.text="Take a break";
                    subtitle.text="Rest time remaining: "+FormatStationCountdown(session.Engine.RemainingSeconds);break;
                case SessionState.Practice:title.text="Practice · move and aim";subtitle.text="Familiarize yourself with the controls. Shots are not scored. "+
                    FormatStationCountdown(session.Engine.RemainingSeconds);break;
                case SessionState.Aborted:case SessionState.Failed:title.text="Test stopped";subtitle.text="Please wait for the administrator.";break;
                default:title.text="Please wait";subtitle.text="The administrator is preparing your test.";break;
            }
        }
        private static string FormatStationCountdown(double seconds)
        {
            int total=(int)Math.Max(0,Math.Ceiling(seconds));
            return (total/60).ToString("D2")+":"+(total%60).ToString("D2");
        }
        private void UpdateRunRows()
        {
            if(knownRun!=session.StationRunId){knownRun=session.StationRunId;blocks.Clear();}
            var engine=session.Engine;if(engine==null)return;
            var row=blocks.LastOrDefault();
            if(engine.CurrentBlockSkipped && row?.attempt!=engine.CurrentBlockId &&
                (engine.State==SessionState.BlockEnded || engine.State==SessionState.Break))
            { row=new RunRow {condition=engine.CurrentCondition!,attempt=engine.CurrentBlockId!,status="Skipped"};blocks.Add(row); }
            if(engine.State==SessionState.BlockRunning && row?.attempt!=engine.CurrentBlockId)
            {row=new RunRow{condition=engine.CurrentCondition!,attempt=engine.CurrentBlockId!};blocks.Add(row);}
            if(row!=null && (row.status=="Running" || row.status=="Paused"))
            {
                row.shots=session.StationShots;row.hits=session.StationHits;
                row.activeSeconds=engine.CurrentActiveSeconds;
                row.status=engine.State==SessionState.BlockPaused?"Paused":"Running";
                if(engine.State==SessionState.BlockEnded || engine.State==SessionState.Break || engine.State==SessionState.SessionComplete)
                    row.status=engine.CurrentBlockStoppedEarly?"Stopped early":"Completed";
                if(engine.State==SessionState.Aborted || engine.State==SessionState.Failed)row.status="Stopped";
            }
        }
        public string StateJson()
        {
            var engine=session.Engine;
            return JsonConvert.SerializeObject(new{
                participant=session.StationParticipant,runId=session.StationRunId,state=engine?.State.ToString()??"Idle",
                phase=session.TestArmed?"Armed — waiting for participant":engine?.State.ToString()??"No participant",
                remainingSeconds=engine?.RemainingSeconds??0,elapsedSeconds=session.StationElapsed,
                armed=session.TestArmed,canArm=session.StationCanArm,canStartParticipant=session.StationCanStartParticipant,
                canPause=session.StationCanPause,canResume=session.StationCanResume,canStop=session.StationCanStop,
                canAbort=!session.IsBusy && engine!=null && engine.State!=SessionState.SessionComplete && engine.State!=SessionState.Aborted && engine.State!=SessionState.Failed,
                activeSeconds=engine?.CurrentActiveSeconds??0,durationSeconds=engine?.CurrentDurationSeconds??0,
                testDurations=session.StationDurations,
                editableDurations=session.StationOrder.Where(session.StationCanSetDuration).ToArray(),
                busy=session.IsBusy,message=session.StationMessage,recordingPath=session.StationRecordingPath,
                condition=engine?.CurrentCondition??"",blockId=engine?.CurrentBlockId??"",conditionOrder=session.StationOrder,blocks,
                shots=session.StationShots,hits=session.StationHits,
                headHits=session.StationHeadHits,bodyHits=session.StationBodyHits,limbHits=session.StationLimbHits,
                coverHits=session.StationCoverHits,misses=session.StationMisses,targetKills=session.StationTargetKills,
                damageDealt=session.StationDamageDealt,reloads=session.StationReloads,dryFires=session.StationDryFires,
                ammo=session.StationAmmo,magazineCapacity=session.StationMagazineCapacity,
                tracking=new{head=Pose(view.RawHead),weapon=Pose(view.RawWeapon)},
                camera=new{yaw=orbitYaw,pitch=orbitPitch,distance=orbitDistance},viewVersion=imageVersion,
                fullscreen=Screen.fullScreen,previewFps=previewFramesPerSecond,
                synthetic=true,canNext=!session.IsBusy && (engine?.State==SessionState.BlockEnded || (engine?.State==SessionState.Break && engine.RemainingSeconds<=0)),
                tools=session.StationToolsState, data=session.CollectionState, downloadId=server?.RegisterExport(session.CollectionExportPath)??""
            });
        }
        private static object Pose(RigidPose? pose)=>new{
            valid=pose.HasValue,position=pose.HasValue?new[]{pose.Value.Position.X,pose.Value.Position.Y,pose.Value.Position.Z}:null,
            rotation=pose.HasValue?new[]{pose.Value.Orientation.X,pose.Value.Orientation.Y,pose.Value.Orientation.Z,pose.Value.Orientation.W}:null};
        private IEnumerator CaptureOutsideView()
        {
            while(server!=null)
            {
                yield return new WaitForEndOfFrame();
                if(capturePending){yield return null;continue;}
                var request=new RenderPipeline.StandardRequest{destination=outsideTexture};
                if(RenderPipeline.SupportsRenderRequest(view.OperatorCamera,request))RenderPipeline.SubmitRenderRequest(view.OperatorCamera,request);
                if(SystemInfo.supportsAsyncGPUReadback)
                {
                    capturePending=true;
                    AsyncGPUReadback.Request(outsideTexture,0,TextureFormat.RGB24,result=> {
                        capturePending=false;
                        if(server==null || capture==null || result.hasError)return;
                        capture.LoadRawTextureData(result.GetData<byte>());
                        server.PublishImage(capture.EncodeToJPG(previewJpegQuality));imageVersion++;
                    });
                }
                else
                {
                    var previous=RenderTexture.active;
                    try {
                        RenderTexture.active=outsideTexture;
                        capture.ReadPixels(new Rect(0,0,previewWidth,previewHeight),0,0,false);
                        server.PublishImage(capture.EncodeToJPG(previewJpegQuality));imageVersion++;
                    } finally {RenderTexture.active=previous;}
                }
                yield return new WaitForSecondsRealtime(1f/previewFramesPerSecond);
            }
        }
        private void OnDestroy()
        {
            if(ownsFramePacing)
            {
                Application.targetFrameRate=previousFrameRate;
                QualitySettings.vSyncCount=previousVSync;
            }
            StopAllCoroutines();server?.Dispose();server=null;
            UnityEngine.Cursor.visible=true;
            Elts.Development.DevelopmentBanner.ShowInPlayer=true;
            if(uiHost!=null)Destroy(uiHost);if(settings!=null)Destroy(settings);
            if(outsideTexture!=null){outsideTexture.Release();Destroy(outsideTexture);}if(capture!=null)Destroy(capture);
        }
    }
}
