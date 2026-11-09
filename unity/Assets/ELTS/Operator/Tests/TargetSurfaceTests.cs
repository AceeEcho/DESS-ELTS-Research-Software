using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Elts.Clock;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Elts.Operator.Tests
{
    public sealed class TargetSurfaceTests
    {
        private const string TargetId="surface-regression";
        private static Vector3d ToData(Vector3 value)=>new Vector3d(value.x,value.y,value.z);

        [UnityTest]
        public IEnumerator SurfaceFollowsCrouchYawStrideAndMovingPose()
        {
            var host=new GameObject("Target surface regression");
            var oracleHost=new GameObject("Independent physics mesh oracle");
            var view=host.AddComponent<DevelopmentView>();
            Mesh oracleMesh=null;
            try
            {
                yield return null;
                Assert.That(view.Ready,Is.True,view.Error);
                view.enabled=false;
                var collider=oracleHost.AddComponent<MeshCollider>();
                double floor=view.Configuration.Rig.VirtualFloorY;
                var center=new Vector3d(0,HumanoidGeometry.StandingCenterAt(floor),4.5);
                var regions=new Dictionary<string,Ray3d>();
                int changedToHit=0,changedToMiss=0;
                var cases=new[] {
                    new DevelopmentFixedPose("Standing",0,0,0,false,0,0),
                    new DevelopmentFixedPose("Crouched",1,0,.3f,false,0,0),
                    new DevelopmentFixedPose("CrouchedSide",1,90,.3f,false,0,0),
                    new DevelopmentFixedPose("CrouchedBack",1,180,.3f,false,0,0),
                    new DevelopmentFixedPose("Peek",.65f,45,.6f,false,0,0),
                    new DevelopmentFixedPose("CrouchedStride",1,-90,.25f,true,0,0),
                    new DevelopmentFixedPose("StandingStride",0,135,.25f,true,0,0)};
                foreach(var pose in cases)
                {
                    var position=center+new Vector3d(0,-.3*pose.Crouch,0);
                    view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>{{TargetId,position}},0,
                        new Dictionary<string,DevelopmentFixedPose>{{TargetId,pose}});
                    view.Refresh(0);
                    var actor=host.transform.Find(TargetId);
                    if(oracleMesh!=null)UnityEngine.Object.DestroyImmediate(oracleMesh);
                    oracleMesh=WorldMesh(actor);
                    collider.sharedMesh=oracleMesh;
                    Physics.SyncTransforms();
                    int hitCount=0,missCount=0;
                    for(int y=0;y<39;y++)for(int x=0;x<27;x++)
                    {
                        var origin=new Vector3(-.65f+x*.05f,(float)floor-.04f+y*.05f,1);
                        var ray=new Ray3d(ToData(origin),new Vector3d(0,0,1));
                        bool expected=collider.Raycast(new Ray(origin,Vector3.forward),out var contact,10);
                        bool actual=view.Raycast(ray,TargetId,out var surface);
                        Assert.That(actual,Is.EqualTo(expected),$"{pose.Name}: ray at {origin}");
                        if(actual)
                        {
                            hitCount++;
                            Assert.That(surface.Distance,Is.EqualTo(contact.distance).Within(.001),"World-space distance must preserve mesh scale");
                            if(pose.Name=="Crouched")regions[surface.Region]=ray;
                        }
                        else missCount++;
                        if(pose.Name=="Crouched")
                        {
                            var legacy=new HumanoidShotModel(new SessionEventSequence(),TimeSpan.Zero,Array.Empty<HumanoidCover>(),floor);
                            legacy.Fire(new ShotContext(new MonotonicTimestamp(0),ray,true),"legacy",new[]{Target(position)},_=>true);
                            if(actual && legacy.Outcome!="Hit")changedToHit++;
                            if(!actual && legacy.Outcome=="Hit")changedToMiss++;
                        }
                    }
                    Assert.That(hitCount,Is.GreaterThan(40),pose.Name);
                    Assert.That(missCount,Is.GreaterThan(40),pose.Name);
                    if(pose.Name=="Crouched" || pose.Name=="CrouchedSide")Capture(actor,view,pose.Name,floor);
                }
                Assert.That(changedToHit,Is.GreaterThan(5),"Visible crouched surfaces formerly missed by the upright approximation");
                Assert.That(changedToMiss,Is.GreaterThan(5),"Empty space formerly credited as a crouched hit");
                Assert.That(regions.Keys,Is.EquivalentTo(new[]{"head","body","limb"}));

                // Move through the crouch transition and turn. Querying before a
                // physics update must still see the current manually sampled skin.
                for(int frame=0;frame<=30;frame++)
                {
                    var position=center+new Vector3d(frame*.012,-.3*frame/30,frame*.005);
                    view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>{{TargetId,position}},frame/60.0);
                    view.Refresh(0);
                }
                var movingActor=host.transform.Find(TargetId);
                var headPoint=TargetSurfaceTestPoints.Head(movingActor);
                var headRay=new Ray3d(new Vector3d(headPoint.x,headPoint.y,1),new Vector3d(0,0,1));
                Assert.That(view.Raycast(headRay,TargetId,out var movingHit),Is.True);
                Assert.That(movingHit.Region,Is.EqualTo("head"));
                view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>());view.Refresh(0);
                Assert.That(view.Raycast(headRay,TargetId,out _),Is.False,"Removed actors cannot leave ghost hits");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(oracleHost);
                if(oracleMesh!=null)UnityEngine.Object.DestroyImmediate(oracleMesh);
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator MeshHitsPreserveDamageNearestTargetCoverAndRecordedSurface()
        {
            var host=new GameObject("Posed mesh scoring regression");
            var view=host.AddComponent<DevelopmentView>();
            try
            {
                yield return null;Assert.That(view.Ready,Is.True,view.Error);view.enabled=false;
                double floor=view.Configuration.Rig.VirtualFloorY;
                var position=new Vector3d(0,HumanoidGeometry.CrouchedCenterAt(floor),4.5);
                view.SetSessionFrame(null,null,new Dictionary<string,Vector3d>{{TargetId,position}},0,
                    new Dictionary<string,DevelopmentFixedPose>{{TargetId,new DevelopmentFixedPose("Crouched",1,0,.3f,false,0,0)}});
                view.Refresh(0);
                var rays=new Dictionary<string,Ray3d>();
                Ray3d empty=default;
                for(int y=0;y<36;y++)for(int x=0;x<25;x++)
                {
                    var ray=new Ray3d(new Vector3d(-.6+x*.05,floor+y*.05,1),new Vector3d(0,0,1));
                    if(view.Raycast(ray,TargetId,out var surface))rays[surface.Region]=ray;
                    else empty=ray;
                }
                Assert.That(rays.Keys,Is.EquivalentTo(new[]{"head","body","limb"}));
                foreach(var item in rays)
                {
                    var target=Target(position);
                    var shots=new HumanoidShotModel(new SessionEventSequence(),TimeSpan.Zero,Array.Empty<HumanoidCover>(),floor,view);
                    var events=shots.Fire(new ShotContext(new MonotonicTimestamp(0),item.Value,true),"scoring",new[]{target},_=>true);
                    int damage=item.Key=="head"?3:item.Key=="body"?2:1;
                    Assert.That(shots.Damage,Is.EqualTo(damage));
                    Assert.That(target.Health,Is.EqualTo(3-damage));
                    Assert.That(events[0].Fields.Single(f=>f.Name=="hitGeometry").Text,Is.EqualTo(view.Version));
                    Assert.That(events[0].Fields.Single(f=>f.Name=="impactWorldZ").Number,Is.EqualTo(shots.ImpactPoint.Value.Z));
                    var contact=item.Value.At(1);
                    var cover=new HumanoidCover("near",contact,new Vector3d(.1,.1,.05));
                    var blocked=new HumanoidShotModel(new SessionEventSequence(),TimeSpan.Zero,new[]{cover},floor,view);
                    blocked.Fire(new ShotContext(new MonotonicTimestamp(0),item.Value,true),"cover",new[]{Target(position)},_=>true);
                    Assert.That(blocked.Outcome,Is.EqualTo("Cover"));
                }
                var miss=new HumanoidShotModel(new SessionEventSequence(),TimeSpan.Zero,Array.Empty<HumanoidCover>(),floor,view);
                miss.Fire(new ShotContext(new MonotonicTimestamp(0),empty,true),"miss",new[]{Target(position)},_=>true);
                Assert.That(miss.Outcome,Is.EqualTo("Miss"));
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }

        private static TargetEntity Target(Vector3d position)
        {
            var target=new TargetEntity(TargetId,position,Vector3d.Zero,.2,new MonotonicTimestamp(0));target.Activate();return target;
        }

        private static Mesh WorldMesh(Transform actor)
        {
            var parts=new List<Mesh>();var combine=new List<CombineInstance>();
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh=new Mesh();skin.BakeMesh(mesh,true);parts.Add(mesh);
                for(int sub=0;sub<mesh.subMeshCount;sub++)combine.Add(new CombineInstance{mesh=mesh,subMeshIndex=sub,transform=skin.transform.localToWorldMatrix});
            }
            var result=new Mesh();result.CombineMeshes(combine.ToArray());
            foreach(var part in parts)UnityEngine.Object.DestroyImmediate(part);
            return result;
        }

        private static void Capture(Transform actor,DevelopmentView view,string name,double floor)
        {
            const int width=96,height=128,scale=4;
            var cameraHost=new GameObject("Hit surface comparison camera");
            var camera=cameraHost.AddComponent<Camera>();
            var target=new RenderTexture(width*scale,height*scale,24);target.Create();
            var capture=new Texture2D(width*scale*2,height*scale,TextureFormat.RGB24,false);
            var silhouette=new Texture2D(width*scale,height*scale,TextureFormat.RGB24,false);
            var skins=actor.GetComponentsInChildren<SkinnedMeshRenderer>();
            var originalMaterials=skins.Select(skin=>skin.sharedMaterials).ToArray();
            var maskMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            maskMaterial.SetColor("_BaseColor",Color.white);
            var previous=RenderTexture.active;
            try
            {
                foreach(var child in actor.GetComponentsInChildren<Transform>())child.gameObject.layer=28;
                camera.cullingMask=1<<28;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.13f);
                camera.orthographic=true;camera.orthographicSize=1;camera.aspect=width/(float)height;
                camera.transform.position=new Vector3(0,(float)floor+.9f,1);camera.targetTexture=target;camera.Render();
                RenderTexture.active=target;capture.ReadPixels(new Rect(0,0,target.width,target.height),0,0);
                // Independent GPU silhouette check: a second bake-based oracle
                // alone would miss a shared mistake in renderer-scale handling.
                foreach(var skin in skins)skin.sharedMaterials=skin.sharedMaterials.Select(_=>maskMaterial).ToArray();
                camera.Render();RenderTexture.active=target;
                silhouette.ReadPixels(new Rect(0,0,target.width,target.height),0,0);silhouette.Apply();
                int silhouetteChecks=0;
                for(int y=0;y<height;y++)for(int x=0;x<width;x++)
                {
                    var ray=camera.ViewportPointToRay(new Vector3((x+.5f)/width,(y+.5f)/height,0));
                    var data=new Ray3d(ToData(ray.origin),ToData(ray.direction));
                    Color color=new Color(.08f,.1f,.13f);
                    bool hitSurface=view.Raycast(data,TargetId,out var hit);
                    if(hitSurface)color=hit.Region=="head"?new Color(1,.65f,.1f):hit.Region=="body"?new Color(.1f,.75f,.75f):new Color(.6f,.4f,1);
                    float low=1,high=0;
                    for(int sy=-1;sy<=1;sy++)for(int sx=-1;sx<=1;sx++)
                    {
                        float value=silhouette.GetPixel(x*scale+scale/2+sx,y*scale+scale/2+sy).r;
                        low=Mathf.Min(low,value);high=Mathf.Max(high,value);
                    }
                    // Skip rasterized boundary pixels; interior and empty-space
                    // samples must agree with the actual rendered silhouette.
                    if(low>.9f || high<.1f)
                    {Assert.That(hitSurface,Is.EqualTo(low>.9f),$"{name} GPU silhouette at {x},{y}");silhouetteChecks++;}
                    for(int sy=0;sy<scale;sy++)for(int sx=0;sx<scale;sx++)capture.SetPixel(width*scale+x*scale+sx,y*scale+sy,color);
                }
                capture.Apply();
                Assert.That(silhouetteChecks,Is.GreaterThan(width*height/2));
                string directory=Path.GetFullPath(Path.Combine(Application.dataPath,"../../test-results/hitboxes"));
                Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name+".png"),capture.EncodeToPNG());
            }
            finally
            {
                foreach(var child in actor.GetComponentsInChildren<Transform>())child.gameObject.layer=DevelopmentView.StimulusLayer;
                for(int i=0;i<skins.Length;i++)skins[i].sharedMaterials=originalMaterials[i];
                RenderTexture.active=previous;camera.targetTexture=null;target.Release();
                UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(capture);UnityEngine.Object.DestroyImmediate(cameraHost);
                UnityEngine.Object.DestroyImmediate(silhouette);UnityEngine.Object.DestroyImmediate(maskMaterial);
            }
        }
    }

    internal static class TargetSurfaceTestPoints
    {
        // Aim the integration test at the model's visible head, not the obsolete
        // analytic head centre that the regression is intended to replace.
        public static Vector3 Head(Transform actor)
        {
            Vector3 sum=Vector3.zero;int count=0;
            foreach(var skin in actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int bone=Array.FindIndex(skin.bones,value=>value.name=="Head");
                var weights=skin.sharedMesh.boneWeights;var mesh=new Mesh();skin.BakeMesh(mesh,true);var vertices=mesh.vertices;
                for(int i=0;i<weights.Length;i++)
                    if(weights[i].boneIndex0==bone && weights[i].weight0>.75f)
                    {sum+=skin.transform.TransformPoint(vertices[i]);count++;}
                UnityEngine.Object.DestroyImmediate(mesh);
            }
            Assert.That(count,Is.GreaterThan(0),"Head vertices must be present");return sum/count;
        }
    }
}
