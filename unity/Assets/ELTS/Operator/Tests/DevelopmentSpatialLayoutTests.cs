using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Geometry;
using Elts.Rendering;
using Elts.Scenario;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Elts.Operator.Tests
{
    public sealed class DevelopmentSpatialLayoutTests
    {
        private static readonly Type Layout = typeof(DevelopmentView).Assembly.GetType("Elts.Operator.DevelopmentScenarioLayout");
        private static Vector3 Unity(Vector3d v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
        private static TargetEntity Target(int number) => new TargetEntity("target-"+number,Vector3d.Zero,Vector3d.Zero,.1,MonotonicTimestamp.Zero);
        private static Vector3d Position(TargetEntity target,Vector3d center,bool moving,double time,DevelopmentView view,int slot)
            =>(Vector3d)Layout.GetMethod("Position").Invoke(null,new object[]{target,center,moving,time,view.GameSettings,view.Configuration.Rig.VirtualFloorY,slot});

        [UnityTest]
        public IEnumerator FloorWindowPerspectiveAndSpatialClearanceAgree()
        {
            var host=new GameObject("Window geometry fixture");
            var view=host.AddComponent<DevelopmentView>();
            var texture=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);
            texture.antiAliasing=4;texture.Create();
            try
            {
                yield return null;
                Assert.That(view.Ready,Is.True,view.Error);
                view.enabled=false;view.ParticipantOnly=true;
                var rig=view.Configuration.Rig;
                var screen=rig.Display;
                double floorY=rig.VirtualFloorY;
                Assert.That(screen.Origin.Y-floorY,Is.EqualTo(4*.3048).Within(1e-9),"Screen bottom is four feet above the shared floor");
                var room=host.transform.Find("Library interior (virtual metres)");
                var floor=room.Find("Stone floor").GetComponent<Renderer>();
                Assert.That(floor.bounds.max.y,Is.EqualTo(floorY).Within(.0001));
                foreach(var name in new[]{"Near wall","Far wall","Ceiling"})Assert.That(room.Find(name),Is.Not.Null,name);
                var center=DevelopmentScenarioLayout.Center(view.Configuration);
                var covers=DevelopmentScenarioLayout.Covers(center,view.GameSettings,floorY);
                Assert.That(covers.Where(c=>c.Id.StartsWith("low-bookcase")).Select(c=>c.Center.Z).Distinct().Count(),Is.EqualTo(5));
                foreach(var cover in covers)
                {
                    Assert.That(cover.Center.Y-cover.HalfSize.Y,Is.GreaterThanOrEqualTo(floorY-1e-8));
                    var coverRoot=host.transform.Find("Cover · "+cover.Id);
                    var bounds=coverRoot.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).ToArray();
                    Assert.That(bounds.Min(b=>b.min.y),Is.EqualTo(cover.Center.Y-cover.HalfSize.Y).Within(.02));
                    Assert.That(bounds.Max(b=>b.max.x)-bounds.Min(b=>b.min.x),Is.EqualTo(cover.HalfSize.X*2).Within(.05));
                }
                foreach(Transform table in room)
                {
                    if(table.name!="Shared study table")continue;
                    var tableBounds=table.GetComponentsInChildren<Renderer>().Select(r=>r.bounds).ToArray();
                    double front=tableBounds.Min(b=>b.min.z),back=tableBounds.Max(b=>b.max.z);
                    for(int row=0;row<5;row++)
                    {
                        double entryZ=DevelopmentScenarioLayout.Station(center,row*10,view.GameSettings,floorY).Z;
                        Assert.That(entryZ<front-.2 || entryZ>back+.2,Is.True,"Peripheral chairs must not obstruct moving entry lanes");
                    }
                }
                var actors=Enumerable.Range(1,30).Select(Target).ToArray();
                var positions=new Dictionary<string,Vector3d>();
                var desktop=new DesktopInputModel(view.Configuration);
                for(int slot=0;slot<30;slot++)positions.Add(actors[slot].Id,Position(actors[slot],center,false,0,view,slot));
                view.SetSessionFrame(desktop.Head,desktop.Weapon,positions,0);view.Refresh(0);
                yield return null;
                for(int slot=0;slot<50;slot++)
                {
                    var position=DevelopmentScenarioLayout.Station(center,slot,view.GameSettings,floorY);
                    var top=view.ParticipantCamera.WorldToViewportPoint(Unity(position+new Vector3d(0,.82,0)));
                    var bottom=view.ParticipantCamera.WorldToViewportPoint(new Vector3((float)position.X,(float)floorY,(float)position.Z));
                    Assert.That(top.y-bottom.y,Is.InRange(.09f,.25f),"Thirty actors remain readable in the room");
                    Assert.That(bottom.y,Is.GreaterThan(.02f));Assert.That(top.y,Is.LessThan(.98f));
                    foreach(float edge in new[]{-.44f,.44f})
                        Assert.That(view.ParticipantCamera.WorldToViewportPoint(Unity(position)+Vector3.right*edge).x,Is.InRange(.001f,.999f));
                }
                Capture(view.ParticipantCamera,texture,"fixed-default.png");
                foreach(var position in positions.Values)
                {
                    var projected=view.OperatorCamera.WorldToViewportPoint(Unity(position));
                    Assert.That(projected.z,Is.GreaterThan(0),"Administrator starts facing the target field");
                    Assert.That(projected.x,Is.InRange(0f,1f));
                    Assert.That(projected.y,Is.InRange(0f,1f));
                }
                Capture(view.OperatorCamera,texture,"administrator-default.png");

                // An eye-to-screen-bottom ray must continue to the same floor
                // point on the virtual side. Changing the eye changes parallax,
                // never the screen rectangle or the floor's world height.
                var baseEye=desktop.Head.TransformPoint(rig.HeadEyeOffsetM);
                foreach(var offset in new[]{Vector3d.Zero,new Vector3d(-.4,.2,-.3),new Vector3d(.4,-.15,.3)})
                {
                    var eye=baseEye+offset;
                    OffAxisCamera.Apply(view.ParticipantCamera,new OffAxisProjection(screen,eye,.05,100));
                    foreach(double u in new[]{0.0,screen.Width*.5,screen.Width})
                    {
                        var edge=screen.Origin+screen.U*u;
                        var direction=edge-eye;
                        var floorPoint=eye+direction*((floorY-eye.Y)/direction.Y);
                        var projected=view.ParticipantCamera.WorldToViewportPoint(Unity(floorPoint));
                        Assert.That(projected.y,Is.EqualTo(0).Within(.0001));
                        Assert.That(projected.x,Is.EqualTo(u/screen.Width).Within(.0001));
                        Assert.That(Vector3d.Dot(floorPoint-screen.Origin,screen.Normal),Is.GreaterThan(0));
                    }
                }

                // Every entry begins beyond the default participant frustum.
                OffAxisCamera.Apply(view.ParticipantCamera,new OffAxisProjection(screen,baseEye,.05,100));
                for(int i=0;i<50;i++)
                {
                    var route=DevelopmentScenarioLayout.Route(i,view.GameSettings);
                    var entry=DevelopmentScenarioLayout.Sample(center,route.Slot,true,i,0,view.GameSettings,floorY);
                    float x=view.ParticipantCamera.WorldToViewportPoint(Unity(entry)).x;
                    Assert.That(x<0 || x>1,Is.True,"Entry must start offscreen");
                }
                var captureFrames=new HashSet<int>{0,120,360,720,1080};
                // Advance at rendering cadence so gait/turning samples reflect travel.
                // Multi-second seeks intentionally reset the visual to an idle pose.
                for(int frame=0;frame<=1080;frame++)
                {
                    double time=frame/60.0;
                    for(int i=0;i<30;i++)positions[actors[i].Id]=Position(actors[i],center,true,time,view,-1);
                    if(time==0)
                        foreach(var position in positions.Values)
                        {
                            var projected=view.ParticipantCamera.WorldToViewportPoint(Unity(position));
                            Assert.That(projected.x,Is.InRange(0f,1f),"Opening moving targets must already be on screen");
                            Assert.That(projected.y,Is.InRange(0f,1f));
                        }
                    view.SetSessionFrame(desktop.Head,desktop.Weapon,positions,time);view.Refresh(0);
                    if(!captureFrames.Contains(frame))continue;
                    yield return null;
                    Capture(view.ParticipantCamera,texture,"moving-"+time+"s.png");
                }
                var shifted=new RigidPose(desktop.Head.Position+new Vector3d(.4,.15,0),desktop.Head.Orientation);
                view.SetSessionFrame(shifted,desktop.Weapon,positions,18);view.Refresh(0);
                yield return null;
                Capture(view.ParticipantCamera,texture,"moving-shifted-eye.png");
            }
            finally{UnityEngine.Object.DestroyImmediate(host);texture.Release();UnityEngine.Object.DestroyImmediate(texture);}
        }

        private static void Capture(Camera camera,RenderTexture target,string name)
        {
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
            var previous=RenderTexture.active;
            var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
            try
            {
                RenderTexture.active=target;image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();
                var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/library-realism"));
                Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
