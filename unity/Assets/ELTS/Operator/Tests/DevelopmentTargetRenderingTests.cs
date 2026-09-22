using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Geometry;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Elts.Operator.Tests
{
    public sealed class DevelopmentTargetRenderingTests
    {
        [UnityTest]
        public IEnumerator TrainingRoomRendersThreeClothedTargetsAndCover()
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
                view.SetSessionFrame(desktop.Head,desktop.Weapon,new Dictionary<string,Vector3d>{
                    {"room-left",new Vector3d(-.9,-.15,centre.Z+view.GameSettings.staticDepthOffsetM)},
                    {"room-middle",new Vector3d(0,-.15,centre.Z+view.GameSettings.staticDepthOffsetM)},
                    {"room-right",new Vector3d(.9,-.15,centre.Z+view.GameSettings.staticDepthOffsetM)}});
                view.Refresh(0);view.enabled=false;
                Assert.That(host.GetComponentsInChildren<MonoBehaviour>().Count(m=>m.GetType().Name=="DevelopmentHumanoidVisual"),Is.EqualTo(3),"Three consistent actors replace the sphere targets");
                Assert.That(host.transform.Find("Cover · crate-stack"),Is.Not.Null);
                Assert.That(host.transform.Find("Cover · concrete-barrier"),Is.Not.Null);
                Assert.That(host.transform.Find("Cover · drums"),Is.Not.Null);
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
                    Assert.That(pixels.Count(c=>c.r<.35f && c.g<.4f && c.b<.45f),Is.GreaterThan(1000),"Dark clothing is visible against the lit room");
                    Assert.That(host.transform.Find("Daylit training room (virtual metres)").GetComponentsInChildren<Collider>().All(c=>!c.enabled),Is.True,"Room geometry cannot intercept a shot");
                }
                finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
            }
            finally{UnityEngine.Object.DestroyImmediate(host);texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }

        [UnityTest]
        public IEnumerator ClothedActorHasFaceAndLimbsFromMultipleViewpoints()
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
                var position=new Vector3d(0,0,4.5);
                view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>{{"surface-test",position}});
                view.Refresh(0);view.enabled=false;
                var sphere=host.transform.Find("surface-test");
                Assert.That(sphere,Is.Not.Null);
                Assert.That(sphere.position,Is.EqualTo(new Vector3(0,0,4.5f)));
                Assert.That(sphere.gameObject.layer,Is.EqualTo(DevelopmentView.StimulusLayer));
                Assert.That(sphere.Find("Head and face"),Is.Not.Null);
                Assert.That(sphere.Find("Left sleeve and hand"),Is.Not.Null);
                Assert.That(sphere.Find("Right trouser leg"),Is.Not.Null);
                // Isolate the actual runtime target for a readable close-up; move
                // the camera around it without rotating or moving the target.
                foreach(var part in sphere.GetComponentsInChildren<Transform>())part.gameObject.layer=31;
                camera.enabled=false;camera.cullingMask=1<<31;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.055f);
                camera.fieldOfView=35;camera.nearClipPlane=.01f;camera.farClipPlane=10;
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
                    Assert.That(body.Length,Is.GreaterThan(10000),"The clothed target is rendered");
                    if(first==null)first=pixels;
                    else Assert.That(pixels.Select((c,i)=>Mathf.Abs(c.r-first[i].r)+Mathf.Abs(c.g-first[i].g)).Average(),Is.GreaterThan(.015f),"Moving around the stationary sphere changes its surface appearance");
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
