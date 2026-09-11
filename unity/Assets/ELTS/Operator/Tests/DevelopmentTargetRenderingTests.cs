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
        public IEnumerator TargetHasCurvatureAndViewpointCuesWithoutChangingItsGeometry()
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
                float radius=(float)view.Configuration.Scenario.TargetRadiusM;
                Assert.That(sphere.localScale,Is.EqualTo(Vector3.one*radius*2),"Visual styling preserves the configured target diameter");
                Assert.That(sphere.position,Is.EqualTo(new Vector3(0,0,4.5f)),"Visual styling preserves the target position");
                Assert.That(sphere.gameObject.layer,Is.EqualTo(DevelopmentView.StimulusLayer));
                Assert.That(sphere.GetComponent<Renderer>().sharedMaterial.shader.isSupported,Is.True);
                // Isolate the actual runtime target for a readable close-up; move
                // the camera around it without rotating or moving the target.
                sphere.gameObject.layer=31;
                camera.enabled=false;camera.cullingMask=1<<31;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.055f);
                camera.fieldOfView=35;camera.nearClipPlane=.01f;camera.farClipPlane=10;
                camera.aspect=1;
                Color[] first=null;
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/target-surface"));
                Directory.CreateDirectory(directory);
                foreach(float yaw in new[]{-35f,0f,35f})
                {
                    var offset=Quaternion.Euler(8,yaw,0)*new Vector3(0,0,-radius*4.4f);
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
                    var body=pixels.Where(c=>c.r>c.b*1.8f && c.r>.12f).Select(c=>c.grayscale).OrderBy(v=>v).ToArray();
                    Assert.That(body.Length,Is.GreaterThan(10000),"The target renders with its amber surface, not an error shader");
                    Assert.That(body[body.Length*8/10]-body[body.Length*2/10],Is.GreaterThan(.08f),"Most of the sphere has a curved lighting gradient, not a flat fill");
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
