#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Elts.Config;
using Elts.Geometry;
using Elts.Rendering;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>
    /// Explicit single-display development emulation. It cannot start a study.
    /// Visual demo motion is frame-driven; this view is not a tracking acquisition loop.
    /// The same immutable observation path drives demo and recorded replay diagnostics.
    /// </summary>
    public sealed class DevelopmentView : MonoBehaviour
    {
        // View-only choices are centralized here; physical layout comes from the staged rig configuration.
        public const int StimulusLayer = 30, OperatorLayer = 31;
        public const double NearMarginMeters = 0.05, FarDistanceMeters = 100;
        private const float DiagnosticControlsHeight = 220, SessionControlsHeight = 340, BannerHeight = 76, MinimumWindowWidth = 900;
        private float ControlsHeight => SessionControlsVisible ? SessionControlsHeight : DiagnosticControlsHeight;
        private const float RodLengthMeters = 0.6f, LineWidthMeters = 0.007f;
        private const double MaximumFrameDeltaSeconds = 0.1;
        private static readonly Color Cyan = new Color(0.1f,0.85f,0.95f), Amber = new Color(1,0.65f,0.15f);
        private DevelopmentConfiguration configuration = null!;
        private ScreenPlane screen;
        private Camera participant = null!, operatorCamera = null!;
        private GameObject head = null!, weapon = null!, eyeMarker = null!;
        private readonly List<LineRenderer> screenLines = new List<LineRenderer>(), frustumLines = new List<LineRenderer>(), rods = new List<LineRenderer>();
        private readonly Dictionary<string,GameObject> targetObjects = new Dictionary<string,GameObject>();
        private LineRenderer bore = null!, aim = null!, viewRay = null!;
        private Material stimulusMaterial = null!, cyanMaterial = null!, amberMaterial = null!, redMaterial = null!;
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly List<Camera> disabledCameras = new List<Camera>();
        private RecordedReplay? replay;
        private ReplayTransport? transport;
        private Task<RecordedReplay>? loading;
        private bool sessionAttached;
        private RigidPose? sessionHead, sessionWeapon;
        private readonly Dictionary<string,Vector3d> sessionTargets = new Dictionary<string,Vector3d>();
        private string replayPath = "", status = "", error = "";
        private bool simulateHeadDropout, simulateWeaponDropout, animate = true;
        private double demoTime, predictionSeconds, layoutOffsetX, layoutDepthOffset;
        private float orbitYaw = 145, orbitPitch = 20, orbitDistance = 4;
        private Vector3 orbitFocus = new Vector3(0,0,1.7f);
        private Vector2 controlScroll;
        private RigidPose? rawHead, rawWeapon, renderedHead;
        public bool Ready { get; private set; }
        public string Error => error;
        public bool IsReplay => replay != null;
        public Camera ParticipantCamera => participant;
        public Camera OperatorCamera => operatorCamera;
        public RigidPose? RawHead => rawHead;
        public RigidPose? RenderedHead => renderedHead;
        public double ReplayPosition => transport?.PositionSeconds ?? 0;
        public bool ReplayPlaying => transport?.IsPlaying ?? false;
        public ScreenPlane DisplayPlane => screen;
        public DevelopmentConfiguration Configuration => configuration;
        public bool SessionControlsVisible { get; set; }
        public string TrackingStatus => status;
        public RigidPose? RawWeapon => rawWeapon;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // Explicit tests create their own instances; avoid unsolicited scene changes in the test runner.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-runTests") >= 0) return;
            if (FindFirstObjectByType<DevelopmentView>() != null) return;
            var host = new GameObject("ELTS development views");
            host.AddComponent<DevelopmentView>();
            host.AddComponent<DevelopmentSessionPanel>();
        }
        private void Awake()
        {
            try
            {
                configuration=DevelopmentConfiguration.LoadDirectory(Path.Combine(Application.streamingAssetsPath,"config-generated"));
                if(configuration.StudyReady || configuration.Mode!="synthetic")throw new InvalidOperationException("This view requires synthetic development configuration.");
                screen=configuration.Rig.Display;
                predictionSeconds=configuration.Runtime.RenderHeadPredictionSeconds;
                foreach(var camera in Camera.allCameras){disabledCameras.Add(camera);camera.enabled=false;}
                stimulusMaterial=Material(new Color(0.65f,0.8f,0.9f));cyanMaterial=Material(Cyan);amberMaterial=Material(Amber);redMaterial=Material(Color.red);
                participant=MakeCamera("Participant preview",1<<StimulusLayer,new Color(0.035f,0.05f,0.09f));
                operatorCamera=MakeCamera("Operator diagnostics",(1<<StimulusLayer)|(1<<OperatorLayer),new Color(0.07f,0.075f,0.1f));
                head=Sphere("Head tracker",OperatorLayer,0.06f,cyanMaterial);
                weapon=Sphere("Weapon tracker",OperatorLayer,0.045f,amberMaterial);
                eyeMarker=Sphere("Rendering eye",OperatorLayer,0.025f,Material(Color.white));
                bore=Line("Zero-corrected bore",OperatorLayer,amberMaterial);aim=Line("Muzzle to target",OperatorLayer,Material(Color.magenta));viewRay=Line("Eye view ray",OperatorLayer,cyanMaterial);
                for(int i=0;i<4;i++){screenLines.Add(Line("Display edge "+i,OperatorLayer,cyanMaterial));frustumLines.Add(Line("Off-axis frustum "+i,OperatorLayer,cyanMaterial));rods.Add(Line("Synthetic corner rod "+i,StimulusLayer,cyanMaterial));}
                for(int x=-3;x<=3;x++)Segment("Floor longitudinal",new Vector3(x,-0.8f,1.5f),new Vector3(x,-0.8f,8),StimulusLayer,stimulusMaterial);
                for(int z=2;z<=8;z++)Segment("Floor transverse",new Vector3(-3,-0.8f,z),new Vector3(3,-0.8f,z),StimulusLayer,stimulusMaterial);
                for(int x=-2;x<=2;x++)Segment("Reference post",new Vector3(x,-0.8f,6),new Vector3(x,1.5f,6),StimulusLayer,stimulusMaterial);
                replayPath=Path.GetFullPath(Path.Combine(Application.dataPath,"..",configuration.Machine.DataRoot));
                Ready=true;Refresh(0);
            }
            catch(Exception exception){error=exception.Message;Debug.LogError("ELTS_VIEW_INIT_FAIL "+exception);}
        }
        private Camera MakeCamera(string name,int mask,Color background)
        {
            var go=new GameObject(name);go.transform.SetParent(transform);var camera=go.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=background;camera.cullingMask=mask;camera.depth=10;
            camera.nearClipPlane=0.03f;camera.farClipPlane=(float)FarDistanceMeters;return camera;
        }
        private Material Material(Color color)
        {
            var template=Resources.Load<Material>("ELTS/DevelopmentUnlit");
            var shader=template!=null?template.shader:Shader.Find("Universal Render Pipeline/Unlit");
            if(shader==null)throw new InvalidOperationException("Pinned URP Unlit shader is unavailable.");
            var result=new Material(shader);result.SetColor("_BaseColor",color);ownedMaterials.Add(result);return result;
        }
        private GameObject Sphere(string name,int layer,float radius,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);go.name=name;go.layer=layer;go.transform.SetParent(transform);go.transform.localScale=Vector3.one*(radius*2);
            Destroy(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go;
        }
        private LineRenderer Line(string name,int layer,Material material)
        {
            var go=new GameObject(name);go.layer=layer;go.transform.SetParent(transform);var line=go.AddComponent<LineRenderer>();line.positionCount=2;line.useWorldSpace=true;
            line.startWidth=line.endWidth=LineWidthMeters;line.sharedMaterial=material;return line;
        }
        private void Segment(string name,Vector3 start,Vector3 end,int layer,Material material){var line=Line(name,layer,material);SetLine(line,start,end);}
        private static void SetLine(LineRenderer line,Vector3 start,Vector3 end){line.SetPosition(0,start);line.SetPosition(1,end);}
        private void Update()
        {
            if(!Ready)return;
            if(loading!=null && loading.IsCompleted)
            {
                if(loading.IsFaulted)error=loading.Exception!.GetBaseException().Message;
                else AttachReplay(loading.Result);
                loading=null;
            }
            Refresh(Math.Min(MaximumFrameDeltaSeconds,Time.unscaledDeltaTime));
        }
        public void SetPrediction(double seconds)
        {
            RenderHeadPrediction.Predict(null,Vector3d.Zero,seconds);predictionSeconds=seconds;
        }
        public void SetDropout(bool headUnavailable,bool weaponUnavailable){simulateHeadDropout=headUnavailable;simulateWeaponDropout=weaponUnavailable;}
        public void SetAnimation(bool enabled){animate=enabled;}
        public void SetLayoutOffsets(double xMeters,double depthMeters)
        {
            if(double.IsNaN(xMeters)||double.IsInfinity(xMeters)||double.IsNaN(depthMeters)||double.IsInfinity(depthMeters)||Math.Abs(xMeters)>1||Math.Abs(depthMeters)>0.5)throw new ArgumentOutOfRangeException(nameof(xMeters));
            layoutOffsetX=xMeters;layoutDepthOffset=depthMeters;
            var original=configuration.Rig.Display;screen=new ScreenPlane(original.Origin+new Vector3d(xMeters,0,depthMeters),original.U,original.V,original.Width,original.Height);
        }
        public void LoadReplay(string directory)
        {
            if(loading!=null)throw new InvalidOperationException("Replay load is already in progress.");
            error="";loading=Task.Run(()=>RecordedReplay.Load(directory));
        }
        public void AttachReplay(RecordedReplay recording){replay=recording??throw new ArgumentNullException(nameof(recording));transport=new ReplayTransport(recording.DurationSeconds);animate=false;error="";}
        public void SetReplayPlaying(bool playing){if(transport==null)throw new InvalidOperationException("Load a recording first.");if(playing)transport.Play();else transport.Pause();}
        public void Scrub(double seconds){if(transport==null)throw new InvalidOperationException("Load a recording first.");transport.Scrub(seconds);}
        public void UseDemo(){replay=null;transport=null;animate=true;error="";}
        /// <summary>Display immutable raw acquisition snapshots; session targets are supplied only while a block runs.</summary>
        public void SetSessionFrame(RigidPose? headPose, RigidPose? weaponPose, IReadOnlyDictionary<string,Vector3d> targets)
        {
            sessionAttached=true; sessionHead=headPose; sessionWeapon=weaponPose;
            sessionTargets.Clear();
            foreach(var item in targets) sessionTargets.Add(item.Key,item.Value);
        }
        public void DetachSession(){sessionAttached=false;sessionTargets.Clear();}
        public void Refresh(double deltaSeconds)
        {
            if(!Ready)return;
            if(deltaSeconds<0||double.IsNaN(deltaSeconds)||double.IsInfinity(deltaSeconds))throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            var visibleTargets=new Dictionary<string,Vector3d>();Vector3d headVelocity=Vector3d.Zero;
            if(sessionAttached)
            {
                rawHead=sessionHead;rawWeapon=sessionWeapon;
                foreach(var item in sessionTargets)visibleTargets.Add(item.Key,item.Value);
                status="SYNTHETIC SESSION | Head "+(rawHead.HasValue?"valid":"UNAVAILABLE")+" | Weapon "+(rawWeapon.HasValue?"valid":"UNAVAILABLE");
            }
            else if(replay!=null)
            {
                transport!.Advance(deltaSeconds);var frame=replay.FrameAt(transport.PositionSeconds);rawHead=frame.Head;rawWeapon=frame.Weapon;
                foreach(var target in replay.TargetsAt(transport.PositionSeconds))visibleTargets.Add(target.Key,target.Value);
                status="REPLAY | Head "+frame.HeadStatus+" | Weapon "+frame.WeaponStatus+" | raw sample t="+(frame.Ticks/10000000.0).ToString("F3")+" s";
                if(replay.ConfigurationHash!=configuration.EffectiveSha256)status+=" | GEOMETRY USES CURRENT CONFIG, NOT RECORDED CONFIG";
            }
            else
            {
                if(animate)demoTime+=deltaSeconds;
                rawHead=simulateHeadDropout?(RigidPose?)null:new RigidPose(new Vector3d(Math.Sin(demoTime*0.7)*0.25,Math.Sin(demoTime*0.4)*0.1,0),Quaterniond.Identity);
                rawWeapon=simulateWeaponDropout?(RigidPose?)null:new RigidPose(new Vector3d(0.15,-0.18,0.1),Quaterniond.Identity);
                if(animate)headVelocity=new Vector3d(Math.Cos(demoTime*0.7)*0.175,Math.Cos(demoTime*0.4)*0.04,0);
                visibleTargets.Add("demo-target",new Vector3d(Math.Sin(demoTime*0.5)*0.4,0.1,configuration.Scenario.TargetDistanceM));
                status="SYNTHETIC DEMO | Head "+(rawHead.HasValue?"valid":"UNAVAILABLE")+" | Weapon "+(rawWeapon.HasValue?"valid":"UNAVAILABLE");
            }
            // Replay stays raw; no velocity is invented by interpolating adjacent observations.
            renderedHead=RenderHeadPrediction.Predict(rawHead,headVelocity,replay==null?predictionSeconds:0);
            head.SetActive(rawHead.HasValue);weapon.SetActive(rawWeapon.HasValue);eyeMarker.SetActive(renderedHead.HasValue);
            if(rawHead.HasValue)head.transform.position=OffAxisCamera.ToUnity(rawHead.Value.Position);
            if(rawWeapon.HasValue)weapon.transform.position=OffAxisCamera.ToUnity(rawWeapon.Value.Position);
            var corners=new[]{screen.Origin,screen.Origin+screen.U*screen.Width,screen.Origin+screen.U*screen.Width+screen.V*screen.Height,screen.Origin+screen.V*screen.Height};
            for(int i=0;i<4;i++){SetLine(screenLines[i],OffAxisCamera.ToUnity(corners[i]),OffAxisCamera.ToUnity(corners[(i+1)%4]));SetLine(rods[i],OffAxisCamera.ToUnity(corners[i]),OffAxisCamera.ToUnity(corners[i]+screen.Normal*RodLengthMeters));}
            participant.enabled=false;viewRay.enabled=false;
            foreach(var line in frustumLines)line.enabled=false;
            if(renderedHead.HasValue)
            {
                var eye=renderedHead.Value.TransformPoint(configuration.Rig.HeadEyeOffsetM);eyeMarker.transform.position=OffAxisCamera.ToUnity(eye);
                try
                {
                    OffAxisCamera.Apply(participant,new OffAxisProjection(screen,eye,NearMarginMeters,FarDistanceMeters));participant.enabled=true;
                    for(int i=0;i<4;i++){frustumLines[i].enabled=true;SetLine(frustumLines[i],OffAxisCamera.ToUnity(eye),OffAxisCamera.ToUnity(corners[i]));}
                    viewRay.enabled=true;SetLine(viewRay,OffAxisCamera.ToUnity(eye),OffAxisCamera.ToUnity(screen.Origin+screen.U*screen.Width*0.5+screen.V*screen.Height*0.5));
                }
                catch(ArgumentException){status+=" | EYE OUTSIDE VALID SCREEN SIDE";}
            }
            var remove=new List<string>();foreach(var key in targetObjects.Keys)if(!visibleTargets.ContainsKey(key))remove.Add(key);
            foreach(var key in remove){Destroy(targetObjects[key]);targetObjects.Remove(key);}
            foreach(var target in visibleTargets){if(!targetObjects.ContainsKey(target.Key))targetObjects.Add(target.Key,Sphere(target.Key,StimulusLayer,(float)configuration.Scenario.TargetRadiusM,amberMaterial));targetObjects[target.Key].transform.position=OffAxisCamera.ToUnity(target.Value);}
            bore.enabled=aim.enabled=rawWeapon.HasValue;
            if(rawWeapon.HasValue)
            {
                var ray=new BoreRay(rawWeapon.Value,configuration.Rig.WeaponMuzzleOffsetM,configuration.Rig.WeaponZero,configuration.Rig.WeaponBoreLocalDirection).Ray;
                bool intersection=screen.Intersect(ray,out var distance,out var point,out var u,out var v);bool onScreen=intersection&&screen.IsInside(u,v);
                bore.sharedMaterial=onScreen?amberMaterial:redMaterial;SetLine(bore,OffAxisCamera.ToUnity(ray.Origin),OffAxisCamera.ToUnity(ray.At(intersection?distance:3)));
                status+=onScreen?" | Bore on screen":" | BORE OUTSIDE DISPLAY";
                aim.enabled=false;
                foreach(var target in visibleTargets){aim.enabled=true;SetLine(aim,OffAxisCamera.ToUnity(ray.Origin),OffAxisCamera.ToUnity(target.Value));status+=" | Aim error "+GeometryMath.AngularErrorDegrees(ray.Direction,target.Value-ray.Origin).ToString("F2")+" deg";break;}
            }
            LayoutCameras();operatorCamera.transform.SetPositionAndRotation(orbitFocus+Quaternion.Euler(orbitPitch,orbitYaw,0)*new Vector3(0,0,-orbitDistance),Quaternion.Euler(orbitPitch,orbitYaw,0));
        }
        private void LayoutCameras()
        {
            // Dashboard previews use camera-owned textures. Preserve full texture
            // viewports and the participant's configured aspect ratio there.
            if(participant.targetTexture!=null && operatorCamera.targetTexture!=null)
            { participant.rect=new Rect(0,0,1,1);operatorCamera.rect=new Rect(0,0,1,1);return; }
            float bottom=Mathf.Min(0.65f,ControlsHeight/Mathf.Max(1,Screen.height)),top=1-BannerHeight/Mathf.Max(1,Screen.height);
            float availableHeight=Mathf.Max(0.1f,top-bottom);
            // Preserve the physical screen's width/height ratio inside the emulated
            // preview. Stretching its frustum over a square viewport distorts targets.
            float previewWidth=0.5f,previewHeight=previewWidth*Screen.width/(float)(screen.Width/screen.Height)/Mathf.Max(1,Screen.height);
            if(previewHeight>availableHeight){previewHeight=availableHeight;previewWidth=previewHeight*Screen.height*(float)(screen.Width/screen.Height)/Mathf.Max(1,Screen.width);}
            participant.rect=new Rect((0.5f-previewWidth)*0.5f,bottom+(availableHeight-previewHeight)*0.5f,previewWidth,previewHeight);
            operatorCamera.rect=new Rect(0.5f,bottom,0.5f,availableHeight);
        }
        private void OnGUI()
        {
            if(!Ready){GUI.Box(new Rect(10,80,Mathf.Max(300,Screen.width-20),70),"Development view unavailable: "+error);return;}
            if(SessionControlsVisible && participant.targetTexture!=null)return;
            if(!SessionControlsVisible && GUI.Button(new Rect(Screen.width-180,40,165,28),"Session controls"))
                GetComponent<DevelopmentSessionPanel>()?.SetVisible(true);
            GUI.Label(new Rect(16,BannerHeight,Screen.width/2-20,24),"PARTICIPANT PREVIEW — stimulus camera only");
            GUI.Label(new Rect(Screen.width/2+16,BannerHeight,Screen.width/2-20,24),"OPERATOR — drag to orbit; shift-drag to pan; wheel to zoom");
            if(!participant.enabled)GUI.Box(new Rect(20,BannerHeight+35,Screen.width/2-40,70),"PREVIEW PAUSED\nValid head pose in front of the screen required");
            if(SessionControlsVisible)return;
            var area=new Rect(10,Screen.height-ControlsHeight+4,Screen.width-20,ControlsHeight-8);
            GUILayout.BeginArea(area,GUI.skin.box);controlScroll=GUILayout.BeginScrollView(controlScroll);
            GUILayout.Label("SINGLE-DISPLAY DEVELOPMENT EMULATION | Synthetic/unmeasured geometry | Study unavailable | Base station not connected");
            var previousColor=GUI.color;if(!rawHead.HasValue||!rawWeapon.HasValue)GUI.color=new Color(1,0.4f,0.3f);GUILayout.Label(status);GUI.color=previousColor;
            if(Screen.width<MinimumWindowWidth)GUILayout.Label("Widen the window for easier diagnostics.");
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("Synthetic demo",GUILayout.Width(120)))UseDemo();
            if(replay==null){animate=GUILayout.Toggle(animate,"Animate");simulateHeadDropout=GUILayout.Toggle(simulateHeadDropout,"Head dropout");simulateWeaponDropout=GUILayout.Toggle(simulateWeaponDropout,"Weapon dropout");}
            else {if(GUILayout.Button(ReplayPlaying?"Pause":"Play",GUILayout.Width(80)))SetReplayPlaying(!ReplayPlaying);double at=GUILayout.HorizontalSlider((float)transport!.PositionSeconds,0,(float)transport.DurationSeconds,GUILayout.Width(240));if(Math.Abs(at-transport.PositionSeconds)>0.001)Scrub(at);GUILayout.Label(transport.PositionSeconds.ToString("F2")+" / "+transport.DurationSeconds.ToString("F2")+" s");}
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("Replay folder",GUILayout.Width(90));replayPath=GUILayout.TextField(replayPath);GUI.enabled=loading==null;if(GUILayout.Button(loading==null?"Load recording":"Loading…",GUILayout.Width(130)))LoadReplay(replayPath);GUI.enabled=true;GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("Screen X (m)",GUILayout.Width(95));var x=GUILayout.HorizontalSlider((float)layoutOffsetX,-1,1,GUILayout.Width(130));GUILayout.Label("Depth offset (m)",GUILayout.Width(115));var z=GUILayout.HorizontalSlider((float)layoutDepthOffset,-0.5f,0.5f,GUILayout.Width(130));if(x!=layoutOffsetX||z!=layoutDepthOffset)SetLayoutOffsets(x,z);if(GUILayout.Button("Reset layout",GUILayout.Width(100)))SetLayoutOffsets(0,0);GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();GUILayout.Label("Demo head prediction (ms)",GUILayout.Width(190));SetPrediction(GUILayout.HorizontalSlider((float)(predictionSeconds*1000),0,50,GUILayout.Width(160))/1000);GUILayout.Label((predictionSeconds*1000).ToString("F0")+" | replay prediction = 0 | config "+configuration.EffectiveSha256.Substring(0,12));GUILayout.EndHorizontal();
            if(error.Length>0)GUILayout.Label("LOAD ERROR: "+error);
            GUILayout.EndScrollView();GUILayout.EndArea();
            var evt=Event.current;var operatorRect=new Rect(Screen.width/2,BannerHeight,Screen.width/2,Screen.height-ControlsHeight-BannerHeight);
            if(operatorRect.Contains(evt.mousePosition))
            {
                if(evt.type==EventType.MouseDrag){if(evt.shift)orbitFocus+=operatorCamera.transform.right*(-evt.delta.x*0.005f)+operatorCamera.transform.up*(evt.delta.y*0.005f);else{orbitYaw+=evt.delta.x*0.4f;orbitPitch=Mathf.Clamp(orbitPitch+evt.delta.y*0.4f,-80,80);}evt.Use();}
                if(evt.type==EventType.ScrollWheel){orbitDistance=Mathf.Clamp(orbitDistance+evt.delta.y*0.15f,0.5f,20);evt.Use();}
            }
        }
        /// <summary>View-only orbit controls used by the dashboard image and keyboard.</summary>
        public void AdjustOperatorView(Vector2 delta,bool pan,float zoom)
        {
            if(pan)orbitFocus+=operatorCamera.transform.right*(-delta.x*0.005f)+operatorCamera.transform.up*(delta.y*0.005f);
            else {orbitYaw+=delta.x*0.4f;orbitPitch=Mathf.Clamp(orbitPitch+delta.y*0.4f,-80,80);}
            orbitDistance=Mathf.Clamp(orbitDistance+zoom*0.15f,0.5f,20);
        }
        public void ResetOperatorView(){orbitYaw=145;orbitPitch=20;orbitDistance=4;orbitFocus=new Vector3(0,0,1.7f);}
        private void OnDestroy()
        {
            foreach(var camera in disabledCameras)if(camera!=null)camera.enabled=true;
            foreach(var material in ownedMaterials)if(material!=null)Destroy(material);
        }
    }
}
