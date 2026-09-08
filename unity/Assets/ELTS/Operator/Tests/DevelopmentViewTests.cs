using System;
using System.Collections;
using Elts.Geometry;
using Elts.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Elts.Operator.Tests
{
    public sealed class DevelopmentViewTests
    {
        [Test]
        public void ActualUnityCameraProjectsRotatedScreenCorners()
        {
            var go=new GameObject("Projection invariant camera");var camera=go.AddComponent<Camera>();
            try
            {
                var random=new System.Random(83);
                for(int i=0;i<30;i++)
                {
                    var rotation=Quaterniond.FromAxisAngle(new Vector3d(0,1,0),random.NextDouble()-0.5);
                    var screen=new ScreenPlane(new Vector3d(-0.6,-0.3,2),rotation.Rotate(new Vector3d(1,0,0)),new Vector3d(0,1,0),1.2,0.6);
                    var eye=screen.Origin+screen.U*random.NextDouble()+screen.V*random.NextDouble()-screen.Normal*(1+random.NextDouble());
                    OffAxisCamera.Apply(camera,new OffAxisProjection(screen,eye,0.05,100));
                    foreach(var corner in new[]{(0.0,0.0,-1f,-1f),(1.2,0.0,1f,-1f),(0.0,0.6,-1f,1f),(1.2,0.6,1f,1f)})
                    {
                        var point=OffAxisCamera.ToUnity(screen.Origin+screen.U*corner.Item1+screen.V*corner.Item2);
                        var clip=camera.projectionMatrix*camera.worldToCameraMatrix*new Vector4(point.x,point.y,point.z,1);
                        Assert.That(clip.x/clip.w,Is.EqualTo(corner.Item3).Within(0.00002));Assert.That(clip.y/clip.w,Is.EqualTo(corner.Item4).Within(0.00002));
                    }
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
        [UnityTest]
        public IEnumerator RuntimeControlsPreserveRawPosesAndCameraIsolation()
        {
            var go=new GameObject("Development view control test");var view=go.AddComponent<DevelopmentView>();
            try
            {
                yield return null;
                Assert.That(view.Ready,Is.True,view.Error);
                Assert.That(view.ParticipantCamera.cullingMask,Is.EqualTo(1<<DevelopmentView.StimulusLayer));
                Assert.That(view.OperatorCamera.cullingMask&(1<<DevelopmentView.OperatorLayer),Is.Not.Zero);
                view.SetAnimation(true);view.SetPrediction(0);view.Refresh(0);var raw=view.RawHead.Value;
                view.SetPrediction(0.02);view.Refresh(0);
                Assert.That(view.RawHead.Value.Position.Equals(raw.Position),Is.True);
                Assert.That(view.RenderedHead.Value.Position.Equals(raw.Position),Is.False);
                var old=view.DisplayPlane.Origin;view.SetLayoutOffsets(0.2,0.1);Assert.That(view.DisplayPlane.Origin.X,Is.EqualTo(old.X+0.2).Within(1e-8));view.SetLayoutOffsets(0,0);
                view.SetDropout(true,false);view.Refresh(0);Assert.That(view.ParticipantCamera.enabled,Is.False);Assert.That(view.RawHead.HasValue,Is.False);
                view.SetDropout(false,false);view.Refresh(0);Assert.That(view.ParticipantCamera.enabled,Is.True);
                var frames=new[]{new ReplayFrame(0,raw,raw,"Connected / Valid","Connected / Valid"),new ReplayFrame(10000000,null,raw,"Disconnected / Unavailable","Connected / Valid"),new ReplayFrame(20000000,raw,raw,"Connected / Valid","Connected / Valid")};
                view.AttachReplay(new RecordedReplay(frames,Array.Empty<ReplayTarget>(),"fixture",new string('a',64),new string('b',64),true));
                view.SetReplayPlaying(true);view.Refresh(0.5);Assert.That(view.ReplayPosition,Is.EqualTo(0.5).Within(1e-8));
                view.SetReplayPlaying(false);view.Refresh(0.5);Assert.That(view.ReplayPosition,Is.EqualTo(0.5).Within(1e-8));
                view.Scrub(1);view.Refresh(0);Assert.That(view.RawHead.HasValue,Is.False);Assert.That(view.ParticipantCamera.enabled,Is.False);
                view.Scrub(2);view.Refresh(0);Assert.That(view.RenderedHead.Value.Position.Equals(raw.Position),Is.True);
                view.UseDemo();view.Refresh(0);Assert.That(view.IsReplay,Is.False);
                yield return null;
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}
