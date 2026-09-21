using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Geometry;
using Elts.Rendering;
using Elts.Session;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class DesktopPipelineTests
    {
        private sealed class Controls : IDesktopControls
        {
            public DesktopControlFrame Frame = new DesktopControlFrame(true,Vector2.zero,Vector3.zero);
            public DesktopControlFrame Read(IPanel panel) { var frame=Frame;Frame=new DesktopControlFrame(frame.Focused,frame.Pointer,frame.Movement);return frame; }
        }
        private static void Submit(Button button)
        { using(var evt=NavigationSubmitEvent.GetPooled()) {evt.target=button;button.SendEvent(evt);} }
        private static T Private<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(owner);

        [UnityTest]
        public IEnumerator DesktopSessionRecordsFourConditionsShotsAndReplay()
        {
            var host=new GameObject("Desktop full pipeline fixture");
            var view=host.AddComponent<DevelopmentView>();var panel=host.AddComponent<DevelopmentSessionPanel>();
            var controls=new Controls();panel.Desktop.Controls=controls;
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(panel,clock);
            var target=new RenderTexture(1440,960,24,RenderTextureFormat.ARGB32);target.Create();panel.SetDiagnosticRenderTarget(target);
            try
            {
                for(int i=0;i<5;i++)yield return null;
                var root=panel.PanelRoot;
                root.Q<TextField>("participant").value="desktop-pipeline-"+Guid.NewGuid().ToString("N");
                root.Q<DropdownField>("inputSource").value=DevelopmentSessionPanel.DesktopInputOption;
                Submit(root.Q<Button>("createSession"));
                float deadline=Time.realtimeSinceStartup+15;
                while((panel.Engine==null || panel.IsBusy || panel.Engine.State==SessionState.Idle) && Time.realtimeSinceStartup<deadline)
                { clock.Advance(TimeSpan.FromSeconds(0.05));yield return null; }
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.SessionSetup));
                Assert.That(root.Q<DropdownField>("inputSource").enabledSelf,Is.False);
                string run=root.Q<Label>("recording").text;
                Submit(root.Q<Button>("advance"));Submit(root.Q<Button>("captureCalibration"));Submit(root.Q<Button>("acceptCalibration"));
                Assert.That(panel.AcceptedCalibrationPath,Is.Not.Null);
                Submit(root.Q<Button>("advance"));
                clock.Advance(TimeSpan.FromSeconds(10));yield return null;
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.BlockReady));
                panel.Desktop.SetOpen(true);
                for(int i=0;i<4;i++)yield return null;
                var originalHead=panel.Desktop.Model.Head.Position;
                controls.Frame=new DesktopControlFrame(true,panel.Desktop.PlayImageBounds.center,new Vector3(1,0,0));
                for(int i=0;i<3;i++)yield return null;
                Assert.That(panel.Desktop.Model.Head.Position,Is.Not.EqualTo(originalHead));

                for(int block=0;block<4;block++)
                {
                    Assert.That(panel.Engine.State,Is.EqualTo(SessionState.BlockReady));
                    panel.DesktopPrimaryAction();
                    for(int i=0;i<5;i++)yield return null;
                    Assert.That(panel.Engine.State,Is.EqualTo(SessionState.BlockRunning));
                    var scenario=Private<DevelopmentSessionScenario>(panel,"scenario");
                    Assert.That(scenario.Positions.Count,Is.GreaterThan(0));
                    var point=scenario.Positions.First().Value;
                    var viewport=view.ParticipantCamera.WorldToViewportPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                    var bounds=panel.Desktop.PlayImageBounds;
                    var pointer=new Vector2(bounds.xMin+viewport.x*bounds.width,bounds.yMax-viewport.y*bounds.height);
                    controls.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,pressed:true);yield return null;
                    clock.Advance(TimeSpan.FromMilliseconds(30));
                    controls.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,released:true);yield return null;
                    Assert.That(scenario.ShotCount,Is.EqualTo(1),panel.DesktopFeedback);
                    Assert.That(scenario.HitCount,Is.EqualTo(1),panel.DesktopFeedback);
                    Assert.That(scenario.Positions.Count,Is.EqualTo(3),"A hit immediately restores all three targets");

                    // A click held through focus loss cannot become a later shot.
                    controls.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,pressed:true);yield return null;
                    controls.Frame=new DesktopControlFrame(false,pointer,Vector3.zero);yield return null;
                    Assert.That(panel.Desktop.IsOpen,Is.False);
                    controls.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,released:true);panel.Desktop.SetOpen(true);yield return null;
                    Assert.That(scenario.ShotCount,Is.EqualTo(1));
                    if(block==0)
                    {
                        for(int i=0;i<3;i++)yield return null;
                        Capture(target,"desktop-play.png");
                    }
                    clock.Advance(TimeSpan.FromSeconds(300));yield return null;
                    Assert.That(panel.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                    panel.SubmitDesktopTrigger();Assert.That(scenario.ShotCount,Is.EqualTo(1));
                    panel.DesktopPrimaryAction();Assert.That(panel.Engine.State,Is.EqualTo(SessionState.Break));
                    Assert.That(panel.DesktopPrimaryEnabled,Is.False);
                    clock.Advance(TimeSpan.FromSeconds(5));yield return null;
                    panel.DesktopPrimaryAction();yield return null;
                }
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.SessionComplete));
                deadline=Time.realtimeSinceStartup+15;
                while(panel.IsBusy && Time.realtimeSinceStartup<deadline)yield return null;
                Assert.That(panel.IsBusy,Is.False);
                var events=File.ReadAllLines(Path.Combine(run,"events.ndjson")).Select(JObject.Parse).ToArray();
                var samples=File.ReadAllLines(Path.Combine(run,"samples.ndjson")).Select(JObject.Parse).ToArray();
                Assert.That(events.Count(e=>(string)e["eventType"]=="ShotFired"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="TargetDestroyed"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="BlockStarted"),Is.EqualTo(4));
                var triggers=events.Where(e=>(string)e["eventType"]=="InputTrigger").ToArray();
                Assert.That(triggers.Length,Is.EqualTo(4));
                foreach(var trigger in triggers)
                {
                    long sequence=(long)trigger["payload"]["acquisitionSequence"];
                    var sample=samples.Single(s=>(long)s["sequence"]==sequence);
                    Assert.That((long)sample["monotonicTicks"],Is.EqualTo((long)trigger["monotonicTicks"]));
                    Assert.That((string)sample["head"]["validity"],Is.EqualTo("Valid"));
                    Assert.That((string)sample["weapon"]["validity"],Is.EqualTo("Valid"));
                    Assert.That(events.Any(e=>(string)e["eventType"]=="ShotFired" && (long)e["monotonicTicks"]==(long)trigger["monotonicTicks"]),Is.True);
                }
                for(int i=1;i<events.Length;i++)Assert.That((long)events[i]["monotonicTicks"],Is.GreaterThanOrEqualTo((long)events[i-1]["monotonicTicks"]));
                var replay=RecordedReplay.Load(run);Assert.That(replay.Synthetic,Is.True);Assert.That(replay.FrameCount,Is.GreaterThan(0));
                Assert.That(replay.TargetsAt(replay.DurationSeconds),Is.Empty);
                panel.SetVisible(false);view.AttachReplay(replay);yield return null;
                Assert.That(view.IsReplay,Is.True);
                Assert.That(view.RawHead,Is.EqualTo(replay.FrameAt(0).Head),"Closed session updates must not replace replay input");
                panel.SetVisible(true);yield return null;
                Assert.That(view.IsReplay,Is.False);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics/desktop-pipeline-run.txt")),run);
            }
            finally { UnityEngine.Object.DestroyImmediate(host);target.Release();UnityEngine.Object.DestroyImmediate(target); }
        }

        [UnityTest]
        public IEnumerator InputSystemMouseKeyboardEdgesAndFocusAreObserved()
        {
            var host=new GameObject("Desktop input mapping fixture");host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var mouse=InputSystem.AddDevice<Mouse>();var keyboard=InputSystem.AddDevice<Keyboard>();
            var background=InputSystem.settings.backgroundBehavior;
            var updateMode=InputSystem.settings.updateMode;
            var editorRouting=InputSystem.settings.editorInputBehaviorInPlayMode;
            try
            {
                // Batch PlayMode has no focused game window. Keep virtual devices
                // enabled so this checks mappings independently of OS focus.
                InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
                InputSystem.settings.updateMode=InputSettings.UpdateMode.ProcessEventsManually;
                InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                yield return null;
                InputSystem.EnableDevice(mouse);InputSystem.EnableDevice(keyboard);
                mouse.MakeCurrent();keyboard.MakeCurrent();
                var device=new MouseKeyboardControls(()=>true);
                InputSystem.QueueStateEvent(mouse,new MouseState {position=new Vector2(300,300)}.WithButton(UnityEngine.InputSystem.LowLevel.MouseButton.Left));
                InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.W,Key.D,Key.E));UpdateVirtualInput();
                mouse.MakeCurrent();keyboard.MakeCurrent();
                var down=device.Read(session.PanelRoot.panel);Assert.That(down.Pressed,Is.True,"virtual left="+mouse.leftButton.ReadValue()+" enabled="+mouse.enabled+" updated="+mouse.wasUpdatedThisFrame);Assert.That(down.Movement,Is.EqualTo(Vector3.one));
                InputSystem.QueueStateEvent(mouse,new MouseState {position=new Vector2(320,300)});UpdateVirtualInput();
                mouse.MakeCurrent();keyboard.MakeCurrent();
                Assert.That(device.Read(session.PanelRoot.panel).Released,Is.True);
                Assert.That(new MouseKeyboardControls(()=>false).Read(session.PanelRoot.panel).Focused,Is.False);
            }
            finally {InputSystem.RemoveDevice(mouse);InputSystem.RemoveDevice(keyboard);InputSystem.settings.backgroundBehavior=background;InputSystem.settings.updateMode=updateMode;InputSystem.settings.editorInputBehaviorInPlayMode=editorRouting;UnityEngine.Object.DestroyImmediate(host);}
        }
        private static void UpdateVirtualInput()
        {
            // The public overload chooses Editor updates in a headless Editor.
            // Explicit Manual updates exercise player button-edge bookkeeping.
            typeof(InputSystem).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Static,
                null,new[]{typeof(InputUpdateType)},null).Invoke(null,new object[]{InputUpdateType.Manual});
        }
        private static void Capture(RenderTexture target,string file)
        {
            var previous=RenderTexture.active;RenderTexture.active=target;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();RenderTexture.active=previous;
            File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics",file)),image.EncodeToPNG());UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
