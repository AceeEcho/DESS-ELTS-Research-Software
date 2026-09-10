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
        private const int ViewWidth=960,ViewHeight=600;
        private const float ImageIntervalSeconds=0.2f;
        private DevelopmentSessionPanel session=null!;
        private DevelopmentView view=null!;
        private AdministratorServer? server;
        private GameObject uiHost=null!;
        private PanelSettings settings=null!;
        private VisualElement ui=null!,prompt=null!;
        private Label title=null!,subtitle=null!,reticle=null!;
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
            Elts.Development.DevelopmentBanner.ShowInPlayer=false;
            session.EnableSeparateAdministrator();view.ParticipantOnly=true;
            view.ParticipantCamera.targetTexture=null;
            outsideTexture=new RenderTexture(ViewWidth,ViewHeight,24,RenderTextureFormat.ARGB32){name="Administrator outside view"};outsideTexture.Create();
            view.OperatorCamera.targetTexture=outsideTexture;
            capture=new Texture2D(ViewWidth,ViewHeight,TextureFormat.RGB24,false);
            // UIDocuments beneath another UIDocument must share its panel. Keep
            // this independent root on its own participant-only panel instead.
            uiHost=new GameObject("Participant interface — no administrator controls");
            settings=ScriptableObject.CreateInstance<PanelSettings>();settings.scaleMode=PanelScaleMode.ConstantPixelSize;settings.sortingOrder=50;
            settings.themeStyleSheet=Resources.Load<ThemeStyleSheet>("ELTS/SessionTheme");
            var document=uiHost.AddComponent<UIDocument>();document.panelSettings=settings;
            ui=document.rootVisualElement;ui.style.unityFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");ui.pickingMode=PickingMode.Ignore;
            Resources.Load<VisualTreeAsset>("ELTS/ParticipantView").CloneTree(ui);
            prompt=ui.Q("participantPrompt");title=ui.Q<Label>("participantTitle");subtitle=ui.Q<Label>("participantSubtitle");reticle=ui.Q<Label>("participantReticle");
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
                // Prefer a secondary monitor for the participant. With one
                // monitor, the admin offers Windowed for side-by-side rehearsal.
                var displays=new List<DisplayInfo>();Screen.GetDisplayLayout(displays);
                if(displays.Count>1)
                {var participantDisplay=displays[1];yield return Screen.MoveMainWindowTo(participantDisplay,Vector2Int.zero);}
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-eltsWindowed")<0)Screen.fullScreenMode=FullScreenMode.FullScreenWindow;
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
            for(int i=0;i<8 && server.TryTakeCommand(out var command);i++)
            {
                if(!command.TryBegin())continue;
                try
                {
                    if((string?)command.Payload["runId"]!=session.StationRunId)
                        throw new InvalidOperationException("The participant changed. Review the current session and try again.");
                    ApplyCommand(command.Payload);command.Complete("{\"ok\":true,\"message\":\"Action accepted.\"}");
                }
                catch(Exception exception){command.Complete(JsonConvert.SerializeObject(new{ok=false,message=exception.Message}));}
            }
            session.StationTick();
            ReadParticipantInput();
            view.Refresh(0);
            UpdateParticipantPrompt();UpdateRunRows();
            if(Time.unscaledTime>=nextState){nextState=Time.unscaledTime+0.1f;server.PublishState(StateJson());}
        }
        public void ApplyCommand(JObject command)
        {
            string action=(string?)command["action"]??"";
            switch(action)
            {
                case "startParticipant":
                    session.StationStartParticipant((string?)command["participant"]??"",command["conditionOrder"]?.ToObject<string[]>()??session.StationOrder);break;
                case "arm": session.StationArm();CancelClick();break;
                case "disarm": session.StationDisarm();CancelClick();break;
                case "next": session.StationNext();CancelClick();break;
                case "abort": session.StationAbort((string?)command["reason"]??"");CancelClick();break;
                case "note": session.StationNote((string?)command["text"]??"");break;
                case "viewOrbit":
                    orbitYaw=Finite(command,"yaw",orbitYaw);orbitPitch=Mathf.Clamp(Finite(command,"pitch",orbitPitch),-10,80);orbitDistance=Mathf.Clamp(Finite(command,"distance",orbitDistance),1.5f,12);
                    view.SetOperatorOrbit(orbitYaw,orbitPitch,orbitDistance);break;
                case "windowed": if(!Application.isEditor)Screen.SetResolution(1100,700,FullScreenMode.Windowed);break;
                case "fullscreen": if(!Application.isEditor)Screen.fullScreenMode=FullScreenMode.FullScreenWindow;break;
                default:throw new ArgumentException("Unknown administrator action.");
            }
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
                session.Desktop.Model.Move(input.Movement,Time.unscaledDeltaTime);
                if(inside)session.Desktop.Model.SetAim(new Vector2((input.Pointer.x-bounds.xMin)/bounds.width,1-(input.Pointer.y-bounds.yMin)/bounds.height));
                if(input.ReturnToAdministrator && !Application.isEditor)Screen.SetResolution(1100,700,FullScreenMode.Windowed);
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
            if(session.TestArmed){title.text="Shoot to begin";subtitle.text="Click and release when you are ready.";return;}
            switch(session.Engine?.State)
            {
                case SessionState.SessionComplete:title.text="Test complete";subtitle.text="Thank you. Please wait for the administrator.";break;
                case SessionState.BlockEnded:case SessionState.Break:title.text="Take a break";subtitle.text="The administrator will prepare the next test.";break;
                case SessionState.Aborted:case SessionState.Failed:title.text="Test stopped";subtitle.text="Please wait for the administrator.";break;
                default:title.text="Please wait";subtitle.text="The administrator is preparing your test.";break;
            }
        }
        private void UpdateRunRows()
        {
            if(knownRun!=session.StationRunId){knownRun=session.StationRunId;blocks.Clear();}
            var engine=session.Engine;if(engine==null)return;
            var row=blocks.LastOrDefault();
            if(engine.State==SessionState.BlockRunning && row?.attempt!=engine.CurrentBlockId)
            {row=new RunRow{condition=engine.CurrentCondition!,attempt=engine.CurrentBlockId!};blocks.Add(row);}
            if(row!=null && row.status=="Running")
            {
                row.shots=session.StationShots;row.hits=session.StationHits;
                if(engine.State==SessionState.BlockEnded || engine.State==SessionState.Break || engine.State==SessionState.SessionComplete)row.status="Completed";
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
                busy=session.IsBusy,message=session.StationMessage,recordingPath=session.StationRecordingPath,
                condition=engine?.CurrentCondition??"",conditionOrder=session.StationOrder,blocks,
                shots=session.StationShots,hits=session.StationHits,
                tracking=new{head=Pose(view.RawHead),weapon=Pose(view.RawWeapon)},
                camera=new{yaw=orbitYaw,pitch=orbitPitch,distance=orbitDistance},viewVersion=imageVersion,
                synthetic=true,canNext=!session.IsBusy && (engine?.State==SessionState.BlockEnded || (engine?.State==SessionState.Break && engine.RemainingSeconds<=0))
            });
        }
        private static object Pose(RigidPose? pose)=>new{
            valid=pose.HasValue,position=pose.HasValue?new[]{pose.Value.Position.X,pose.Value.Position.Y,pose.Value.Position.Z}:null,
            rotation=pose.HasValue?new[]{pose.Value.Orientation.X,pose.Value.Orientation.Y,pose.Value.Orientation.Z,pose.Value.Orientation.W}:null};
        private IEnumerator CaptureOutsideView()
        {
            var delay=new WaitForSecondsRealtime(ImageIntervalSeconds);
            while(server!=null)
            {
                yield return new WaitForEndOfFrame();
                var request=new RenderPipeline.StandardRequest{destination=outsideTexture};
                if(RenderPipeline.SupportsRenderRequest(view.OperatorCamera,request))RenderPipeline.SubmitRenderRequest(view.OperatorCamera,request);
                var previous=RenderTexture.active;RenderTexture.active=outsideTexture;
                capture.ReadPixels(new Rect(0,0,ViewWidth,ViewHeight),0,0);capture.Apply();RenderTexture.active=previous;
                server?.PublishImage(capture.EncodeToJPG(82));imageVersion++;
                yield return delay;
            }
        }
        private void OnDestroy()
        {
            StopAllCoroutines();server?.Dispose();server=null;
            UnityEngine.Cursor.visible=true;
            Elts.Development.DevelopmentBanner.ShowInPlayer=true;
            if(uiHost!=null)Destroy(uiHost);if(settings!=null)Destroy(settings);
            if(outsideTexture!=null){outsideTexture.Release();Destroy(outsideTexture);}if(capture!=null)Destroy(capture);
        }
    }
}
