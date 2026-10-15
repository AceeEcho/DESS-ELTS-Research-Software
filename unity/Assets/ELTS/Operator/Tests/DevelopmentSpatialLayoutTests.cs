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
                var floor=room.Find("Library carpet").GetComponent<Renderer>();
                Assert.That(floor.bounds.max.y,Is.EqualTo(floorY).Within(.0001));
                foreach(var name in new[]{"Near wall","Far wall","Ceiling"})Assert.That(room.Find(name),Is.Not.Null,name);
                var center=DevelopmentScenarioLayout.Center(view.Configuration);
                var covers=DevelopmentScenarioLayout.Covers(center,view.GameSettings,floorY);
                Assert.That(LibraryFloorPlan.Fixtures.Count(f=>f.Kind==LibraryFixtureKind.Table),Is.EqualTo(5));
                Assert.That(LibraryFloorPlan.Fixtures.Count(f=>f.Kind==LibraryFixtureKind.Stack),Is.GreaterThan(5));
                foreach(var cover in covers)Assert.That(cover.Center.Y-cover.HalfSize.Y,Is.GreaterThanOrEqualTo(floorY-1e-8));
                // A clear room capture distinguishes architecture from crowd occlusion.
                var emptyDesktop=new DesktopInputModel(view.Configuration);
                view.SetSessionFrame(emptyDesktop.Head,emptyDesktop.Weapon,new Dictionary<string,Vector3d>(),0);view.Refresh(0);
                yield return null;Capture(view.ParticipantCamera,texture,"library-empty.png");
                var actors=Enumerable.Range(1,30).Select(Target).ToArray();
                var positions=new Dictionary<string,Vector3d>();
                var desktop=new DesktopInputModel(view.Configuration);
                var fixedPopulation=new LibraryTargetPopulation(view.GameSettings,false,.1);
                fixedPopulation.Reconcile(0,center,floorY);
                var fixedPoses=new Dictionary<string,DevelopmentFixedPose>();
                foreach(var target in fixedPopulation.ActiveTargets)
                {
                    positions.Add(target.Id,target.Position);
                    fixedPoses.Add(target.Id,DevelopmentScenarioLayout.FixedPose(fixedPopulation.Slot(target),DevelopmentScenarioLayout.Ordinal(target),view.GameSettings));
                }
                view.SetSessionFrame(desktop.Head,desktop.Weapon,positions,0,fixedPoses);view.Refresh(0);
                yield return null;
                Capture(view.ParticipantCamera,texture,"fixed-default.png");
                view.RefreshOperatorScreen();Capture(view.OperatorCamera,texture,"administrator-default.png");
                foreach(var point in new[]{desktop.Head.Position,screen.Origin,screen.Origin+screen.U*screen.Width+screen.V*screen.Height,
                    new Vector3d(desktop.Head.Position.X,floorY,desktop.Head.Position.Z)})
                {
                    var projected=view.OperatorCamera.WorldToViewportPoint(Unity(point));
                    Assert.That(projected.z,Is.GreaterThan(0));
                    Assert.That(projected.x,Is.InRange(.03f,.97f));Assert.That(projected.y,Is.InRange(.03f,.97f));
                }
                view.RefreshOperatorScreen();Capture(view.OperatorCamera,texture,"administrator-default.png");
                Assert.That(host.transform.Find("Muzzle to target"),Is.Null);
                Assert.That(host.transform.Find("Screen aim horizontal").GetComponent<LineRenderer>().enabled,Is.True);
                // Verify the marker follows actual bore/screen geometry, including
                // off-centre aim, rather than pointing at a selected target.
                foreach(var aimPoint in new[]{new Vector2(.15f,.25f),new Vector2(.85f,.75f)})
                {
                    desktop.SetAim(aimPoint);view.SetSessionFrame(desktop.Head,desktop.Weapon,positions,0,fixedPoses);view.Refresh(0);
                    var line=host.transform.Find("Screen aim horizontal").GetComponent<LineRenderer>();
                    var expected=screen.Origin+screen.U*(aimPoint.x*screen.Width)+screen.V*(aimPoint.y*screen.Height)-screen.Normal*.008;
                    Assert.That(Vector3.Distance((line.GetPosition(0)+line.GetPosition(1))/2,Unity(expected)),Is.LessThan(.0001));
                }
                view.RefreshOperatorScreen();Capture(view.OperatorCamera,texture,"administrator-offset-aim.png");
                desktop.Reset();


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

                // Replacements start outside real side doors; distant doors may be
                // visible through the physical screen, so frustum exclusion is not required.
                OffAxisCamera.Apply(view.ParticipantCamera,new OffAxisProjection(screen,baseEye,.05,100));
                for(int i=0;i<50;i++)Assert.That(Math.Abs(LibraryFloorPlan.Entry(i)[0].X),Is.GreaterThan(LibraryFloorPlan.HalfWidth));
                var captureFrames=new HashSet<int>{0,120,360,720,1080};
                // Advance at rendering cadence so gait/turning samples reflect travel.
                // Multi-second seeks intentionally reset the visual to an idle pose.
                for(int frame=0;frame<=1080;frame++)
                {
                    double time=frame/60.0;
                    for(int i=0;i<30;i++)positions[actors[i].Id]=Position(actors[i],center,true,time,view,-1);
                    view.SetSessionFrame(desktop.Head,desktop.Weapon,positions,time);view.Refresh(0);
                    if(!captureFrames.Contains(frame))continue;
                    yield return null;
                    Capture(view.ParticipantCamera,texture,"moving-"+time+"s.png");
                }
                var shifted=new RigidPose(desktop.Head.Position+new Vector3d(.4,.15,0),desktop.Head.Orientation);
                view.SetSessionFrame(shifted,desktop.Weapon,positions,18);view.Refresh(0);
                yield return null;
                Capture(view.ParticipantCamera,texture,"moving-shifted-eye.png");
                view.SetSessionFrame(null,null,positions,18);view.Refresh(0);view.RefreshOperatorScreen();
                Assert.That(host.transform.Find("Screen aim horizontal").GetComponent<LineRenderer>().enabled,Is.False);
                Assert.That(host.transform.Find("Physical display · participant image").GetComponent<Renderer>().enabled,Is.False);
                Capture(view.OperatorCamera,texture,"administrator-dropout.png");

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
                var directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/library-floorplan-v3"));
                Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name),image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(image);}
        }
    }
}
