using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Geometry;
using Elts.Clock;
using Elts.Scenario;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Elts.Operator.Tests
{
    public sealed class DevelopmentTargetRenderingTests
    {
        [UnityTest]
        public IEnumerator MotionUsesActiveTimeBendsKneesAndKeepsFeetGrounded()
        {
            var host = new GameObject("Animation regression fixture");
            var view = host.AddComponent<DevelopmentView>();
            try
            {
                yield return null;
                Assert.That(view.Ready, Is.True, view.Error);
                view.enabled = false;
                void Frame(Vector3d position, double time)
                {
                    view.SetSessionFrame(null, null, new Dictionary<string, Vector3d> { { "motion-test", position } }, time);
                    view.Refresh(0);
                }
                double standingY=HumanoidGeometry.StandingCenterAt(view.Configuration.Rig.VirtualFloorY);
                double crouchedY=HumanoidGeometry.CrouchedCenterAt(view.Configuration.Rig.VirtualFloorY);
                var standing = new Vector3d(0, standingY, 4.5);
                Frame(standing, 0);
                var actor = host.transform.Find("motion-test");
                var bones = actor.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r => r.bones).Distinct().ToArray();
                var feet = bones.Where(b => b.name == "foot_l" || b.name == "foot_r").ToArray();
                Assert.That(feet.Length, Is.EqualTo(2));
                float floor = feet.Min(b => b.position.y);
                var initial = bones.Select(b => b.localRotation).ToArray();
                for (int i = 1; i <= 60; i++) Frame(standing, i / 60.0);
                Assert.That(bones.Select((b, i) => Quaternion.Angle(initial[i], b.localRotation)).Sum(), Is.GreaterThan(1), "Idle must breathe rather than freeze");
                var beforeCrouch = bones.Select(b => b.localRotation).ToArray();
                var headBone = bones.First(b => b.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0);
                float standingHeadY = headBone.position.y;
                var scales = bones.Select(b => b.localScale).ToArray();
                for (int i = 1; i <= 60; i++)
                {
                    double y = standingY + (crouchedY - standingY) * i / 60.0;
                    Frame(new Vector3d(0, y, 4.5), 1 + i / 60.0);
                    Assert.That(feet.Min(b => b.position.y), Is.EqualTo(floor).Within(.002f), "Supporting foot must not sink or float during crouch");
                    Assert.That(actor.localScale, Is.EqualTo(Vector3.one));
                }
                Assert.That(bones.Select((b, i) => Quaternion.Angle(beforeCrouch[i], b.localRotation)).Sum(), Is.GreaterThan(40), "Crouch must bend the skeleton");
                Assert.That(headBone.position.y, Is.LessThan(standingHeadY - .15f), "Crouch must visibly lower the head while the feet stay grounded");
                var animation = actor.GetComponentInChildren<Animation>();
                foreach (AnimationState state in animation)
                    if (state.name.Contains("|")) Assert.That(state.enabled, Is.False, "The FBX default clip must not contaminate the blend");
                for (int i = 0; i < bones.Length; i++) Assert.That(Vector3.Distance(bones[i].localScale, scales[i]), Is.LessThan(.001f));
                var paused = bones.Select(b => b.localRotation).ToArray();
                var pausedPositions = bones.Select(b => b.position).ToArray();
                for (int i = 0; i < 3; i++)
                {
                    yield return null;
                    Frame(new Vector3d(0, crouchedY, 4.5), 2);
                }
                for (int i = 0; i < bones.Length; i++)
                {
                    Assert.That(Quaternion.Angle(bones[i].localRotation, paused[i]), Is.LessThan(.01f), "Wall time cannot advance a paused pose");
                    Assert.That(Vector3.Distance(bones[i].position, pausedPositions[i]), Is.LessThan(.0001f));
                }
                Assert.That(actor.rotation, Is.EqualTo(Quaternion.identity));
                Assert.That(actor.GetComponentsInChildren<Collider>().All(c => !c.enabled), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator WalkingCadenceIsConsistentAcrossFrameRatesAndPauseResume()
        {
            var host = new GameObject("Cadence fixture");
            var view = host.AddComponent<DevelopmentView>();
            try
            {
                yield return null;
                Assert.That(view.Ready, Is.True, view.Error);
                view.enabled = false;
                float standingY=(float)HumanoidGeometry.StandingCenterAt(view.Configuration.Rig.VirtualFloorY);
                view.SetSessionFrame(null, null, new Dictionary<string, Vector3d> { { "cadence", new Vector3d(0, standingY, 4.5) } });
                view.Refresh(0);
                var actor = host.transform.Find("cadence");
                var visual = actor.GetComponents<MonoBehaviour>().Single(m => m.GetType().Name == "DevelopmentHumanoidVisual");
                var pose = visual.GetType().GetMethod("Pose");
                var bones = actor.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r => r.bones).Distinct().ToArray();
                Quaternion[] reference = null;
                foreach (int fps in new[] { 30, 120 })
                {
                    pose.Invoke(visual, new object[] { new Vector3(0, standingY, 4.5f), -1.0 });
                    for (int frame = 0; frame <= fps; frame++)
                    {
                        double time = frame / (double)fps;
                        pose.Invoke(visual, new object[] { new Vector3((float)time, standingY, 4.5f), time });
                    }
                    var rotations = bones.Select(b => b.localRotation).ToArray();
                    if (reference == null) reference = rotations;
                    else Assert.That(rotations.Select((r, i) => Quaternion.Angle(r, reference[i])).Average(), Is.LessThan(2), "Cadence uses distance, not frame count");
                    for (int i = 0; i < 4; i++)
                    {
                        yield return null;
                        pose.Invoke(visual, new object[] { new Vector3(1, standingY, 4.5f), 1.0 });
                    }
                    Assert.That(bones.Select((b, i) => Quaternion.Angle(b.localRotation, rotations[i])).Max(), Is.LessThan(.01f), "Pausing mid-stride retains the running pose");
                    pose.Invoke(visual, new object[] { new Vector3(1 + 1f / fps, standingY, 4.5f), 1 + 1.0 / fps });
                    Assert.That(bones.Select((b, i) => Quaternion.Angle(b.localRotation, rotations[i])).Max(), Is.LessThan(35), "Resume must continue the stride without a pose jump");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [UnityTest]
        public IEnumerator ChoreographyRendersSmoothEntryCrouchAndPeek()
        {
            var host = new GameObject("Movement sequence fixture");
            var view = host.AddComponent<DevelopmentView>();
            var cameraHost = new GameObject("Movement inspection camera");
            var camera = cameraHost.AddComponent<Camera>();
            var texture = new RenderTexture(480, 640, 24, RenderTextureFormat.ARGB32);
            texture.Create();
            try
            {
                yield return null;
                Assert.That(view.Ready, Is.True, view.Error);
                view.enabled = false;
                var settings = view.GameSettings;
                var layout = typeof(DevelopmentView).Assembly.GetType("Elts.Operator.DevelopmentScenarioLayout");
                var positionMethod = layout.GetMethod("Position");
                var target = new TargetEntity("target-1", Vector3d.Zero, Vector3d.Zero, .1, new MonotonicTimestamp(0));
                Vector3d Position(double time) => (Vector3d)positionMethod.Invoke(null, new object[] { target, new Vector3d(0, 0, 3), true, time, settings, view.Configuration.Rig.VirtualFloorY, 0 });
                // The lane must arrive with no velocity jump, including at the peek boundaries.
                foreach (double boundary in new[] { 0, settings.runInSeconds, settings.runInSeconds + settings.peekCycleSeconds * .17,
                    settings.runInSeconds + settings.peekCycleSeconds * .31, settings.runInSeconds + settings.peekCycleSeconds * .52,
                    settings.runInSeconds + settings.peekCycleSeconds * .66 })
                {
                    var a = Position(Math.Max(0, boundary - .001));
                    var b = Position(boundary);
                    var c = Position(boundary + .001);
                    Assert.That(Math.Abs(c.X - 2*b.X + a.X) + Math.Abs(c.Y - 2*b.Y + a.Y), Is.LessThan(.0001), "No abrupt acceleration at choreography boundaries");
                }
                camera.enabled = false;
                camera.cullingMask = 1 << 29;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.055f, .065f, .075f);
                camera.fieldOfView = 42;
                camera.aspect = .75f;
                var directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../test-results/natural-movement"));
                Directory.CreateDirectory(directory);
                var captures = new[] { 60, 138, 250, 294, 360, 402, 480 };
                for (int frame = 0; frame <= captures.Last(); frame++)
                {
                    double time = frame / 60.0;
                    var position = Position(time);
                    view.SetSessionFrame(null, null, new Dictionary<string, Vector3d> { { target.Id, position } }, time);
                    view.Refresh(0);
                    var actor = host.transform.Find(target.Id);
                    Assert.That(Vector3.Distance(actor.position, new Vector3((float)position.X, (float)position.Y, (float)position.Z)), Is.LessThan(.00001f));
                    foreach (var part in actor.GetComponentsInChildren<Transform>()) part.gameObject.layer = 29;
                    if (!captures.Contains(frame)) continue;
                    // GPU skinning refreshes on Unity frames, not on each render
                    // request. Give the sampled bones a frame before inspecting pixels.
                    yield return null;
                    var focus = new Vector3(actor.position.x, (float)view.Configuration.Rig.VirtualFloorY+.82f, actor.position.z);
                    camera.transform.position = focus + new Vector3(1.3f, .6f, -3.3f);
                    camera.transform.LookAt(focus);
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture });
                    var previous = RenderTexture.active;
                    var image = new Texture2D(480, 640, TextureFormat.RGBA32, false);
                    try
                    {
                        RenderTexture.active = texture;
                        image.ReadPixels(new Rect(0, 0, 480, 640), 0, 0);
                        image.Apply();
                        File.WriteAllBytes(Path.Combine(directory, "motion-" + frame + ".png"), image.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; UnityEngine.Object.DestroyImmediate(image); }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraHost);
                UnityEngine.Object.DestroyImmediate(host);
                texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator LibraryRendersThirtyMannequinsAndCover()
        {
            var host=new GameObject("Training room visual fixture");
            var view=host.AddComponent<DevelopmentView>();
            var texture=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            texture.antiAliasing=4;texture.Create();
            try
            {
                yield return null;
                Assert.That(view.Ready,Is.True,view.Error);
                var desktop=new DesktopInputModel(view.Configuration);
                var screen=view.DisplayPlane;
                var centre=screen.Origin+screen.U*(screen.Width*.5)+screen.V*(screen.Height*.5)+screen.Normal*view.Configuration.Scenario.TargetDistanceM;
                view.ParticipantOnly=true;
                var layout=typeof(DevelopmentView).Assembly.GetType("Elts.Operator.DevelopmentScenarioLayout");
                var targets=new Dictionary<string,Vector3d>();
                for(int lane=0;lane<30;lane++)
                {
                    var actor=new TargetEntity("room-"+(lane+1),Vector3d.Zero,Vector3d.Zero,.1,MonotonicTimestamp.Zero);
                    targets.Add(actor.Id,(Vector3d)layout.GetMethod("Position").Invoke(null,new object[]{actor,centre,false,0.0,view.GameSettings,view.Configuration.Rig.VirtualFloorY,lane}));
                }
                view.SetSessionFrame(desktop.Head,desktop.Weapon,targets);
                view.Refresh(0);view.enabled=false;
                Assert.That(host.GetComponentsInChildren<MonoBehaviour>().Count(m=>m.GetType().Name=="DevelopmentHumanoidVisual"),Is.EqualTo(30),"Thirty consistent actors populate the library");
                Assert.That(host.transform.Find("Cover · reading-desk-1"),Is.Not.Null);
                Assert.That(host.transform.Find("Cover · low-bookcase-2"),Is.Not.Null);
                Assert.That(host.transform.Find("Cover · low-bookcase-3"),Is.Not.Null);
                var camera=view.ParticipantCamera;
                var request=new RenderPipeline.StandardRequest{destination=texture};
                RenderPipeline.SubmitRenderRequest(camera,request);
                var previous=RenderTexture.active;
                var image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
                try
                {
                    RenderTexture.active=texture;image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();
                    var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/target-surface"));
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory,"training-room.png"),image.EncodeToPNG());
                    var pixels=image.GetPixels();
                    Assert.That(pixels.Count(c=>c.grayscale>.35f),Is.GreaterThan(pixels.Length*.5),"The room and targets remain legible");
                    Assert.That(pixels.Count(c=>c.r<.35f && c.g<.4f && c.b<.45f),Is.GreaterThan(1000),"Dark mannequin joints and cover remain visible against the lit room");
                    Assert.That(host.transform.Find("Library interior (virtual metres)").GetComponentsInChildren<Collider>().All(c=>!c.enabled),Is.True,"Room geometry cannot intercept a shot");
                }
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
            }
            finally{UnityEngine.Object.DestroyImmediate(host);texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }

        [UnityTest]
        public IEnumerator SourcedMannequinHasSkinAndRigFromMultipleViewpoints()
        {
            // This is a rendering fixture only: it never creates participant recordings.
            var host=new GameObject("Target surface rendering fixture");
            var view=host.AddComponent<DevelopmentView>();
            var cameraHost=new GameObject("Target inspection camera");
            var camera=cameraHost.AddComponent<Camera>();
            var texture=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);
            texture.antiAliasing=4;texture.Create();
            try
            {
                yield return null;
                Assert.That(view.Ready,Is.True,view.Error);
                var position=new Vector3d(0,HumanoidGeometry.StandingCenterAt(view.Configuration.Rig.VirtualFloorY),4.5);
                float standingY=(float)position.Y;
                view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>{{"surface-test",position}});
                view.Refresh(0);view.enabled=false;
                var sphere=host.transform.Find("surface-test");
                Assert.That(sphere,Is.Not.Null);
                Assert.That(sphere.position,Is.EqualTo(new Vector3(0,standingY,4.5f)));
                Assert.That(sphere.gameObject.layer,Is.EqualTo(DevelopmentView.StimulusLayer));
                var model=sphere.Find("Quaternius mannequin");
                Assert.That(model,Is.Not.Null);
                var skin=model.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(skin.Length,Is.GreaterThan(0),"The sourced skinned mesh replaces primitive people");
                Assert.That(skin.All(r=>r.sharedMesh.vertexCount>100 && r.bones.Length>10),Is.True);
                Assert.That(model.GetComponentsInChildren<Collider>().All(c=>!c.enabled),Is.True,"Visuals cannot intercept scenario shots");
                var clips=Resources.LoadAll<AnimationClip>("ELTS/Mannequin/UAL1_Standard");
                Assert.That(clips.Count(c=>!c.name.StartsWith("__preview__")),Is.EqualTo(5),"Idle, walk, jog and two crouch gaits are shipped");
                Assert.That(clips.Any(c=>c.name.EndsWith("Jog_Fwd_Loop")),Is.True);
                var visual=sphere.GetComponents<MonoBehaviour>().Single(c=>c.GetType().Name=="DevelopmentHumanoidVisual");
                var pose=visual.GetType().GetMethod("Pose");
                var bones=skin.SelectMany(r=>r.bones).Distinct().ToArray();
                var standing= bones.Select(b=>b.localRotation).ToArray();
                for(int frame=1;frame<=15;frame++)
                    pose.Invoke(visual,new object[]{new Vector3(frame/60f,standingY,4.5f),frame/60.0});
                Assert.That(bones.Select((b,i)=>Quaternion.Angle(b.localRotation,standing[i])).Sum(),Is.GreaterThan(10),"Jog sampling must animate the rig");
                Assert.That(sphere.position,Is.EqualTo(new Vector3(.25f,standingY,4.5f)),"Animation cannot move a logged target");
                Assert.That(sphere.localScale,Is.EqualTo(Vector3.one),"Crouching must never squash the mesh");
                pose.Invoke(visual,new object[]{new Vector3(0,standingY,4.5f),0.0});
                // Isolate the actual runtime target for a readable close-up; move
                // the camera around it without rotating or moving the target.
                foreach(var part in sphere.GetComponentsInChildren<Transform>())part.gameObject.layer=31;
                camera.enabled=false;camera.cullingMask=1<<31;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.055f);
                camera.fieldOfView=42;camera.nearClipPlane=.01f;camera.farClipPlane=10;
                camera.aspect=1;
                Color[] first=null;
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/target-surface"));
                Directory.CreateDirectory(directory);
                foreach(float yaw in new[]{-35f,0f,35f})
                {
                    var offset=Quaternion.Euler(8,yaw,0)*new Vector3(0,0,-2.5f);
                    camera.transform.position=sphere.position+offset;camera.transform.LookAt(sphere.position);
                    var request=new RenderPipeline.StandardRequest{destination=texture};
                    Assert.That(RenderPipeline.SupportsRenderRequest(camera,request),Is.True);
                    RenderPipeline.SubmitRenderRequest(camera,request);
                    var previous=RenderTexture.active;
                    var image=new Texture2D(512,512,TextureFormat.RGBA32,false);
                    Color[] pixels;
                    try
                    {
                        RenderTexture.active=texture;
                        image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                        pixels=image.GetPixels();
                        File.WriteAllBytes(Path.Combine(directory,"target-"+yaw.ToString("0",System.Globalization.CultureInfo.InvariantCulture)+".png"),image.EncodeToPNG());
                    }
                    finally {RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
                    var body=pixels.Where(c=>c.r>.08f && c.g>.08f && c.b>.08f).ToArray();
                    Assert.That(body.Length,Is.GreaterThan(10000),"The mannequin is rendered");
                    if(first==null)first=pixels;
                    else Assert.That(pixels.Select((c,i)=>Mathf.Abs(c.r-first[i].r)+Mathf.Abs(c.g-first[i].g)).Average(),Is.GreaterThan(.015f),"Moving around the stationary mannequin changes its surface appearance");
                }
                Assert.That(sphere.rotation,Is.EqualTo(Quaternion.identity),"Camera motion never rotates the target to fake perspective");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraHost);UnityEngine.Object.DestroyImmediate(host);
                texture.Release();UnityEngine.Object.DestroyImmediate(texture);
            }
        }
    }
}
