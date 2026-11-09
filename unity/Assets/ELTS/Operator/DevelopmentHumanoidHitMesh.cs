#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Scenario;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Exact triangle hits on the same skinned surface that is drawn.
    /// Bake only when a shot queries a changed pose; no per-frame physics cooking.</summary>
    internal sealed class DevelopmentHumanoidHitMesh : IDisposable
    {
        public const string GeometryVersion = "posed-mannequin-mesh-v1";
        private readonly Surface[] surfaces;

        public DevelopmentHumanoidHitMesh(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
            surfaces = new Surface[renderers.Length];
            for(int i=0;i<renderers.Length;i++) surfaces[i]=new Surface(renderers[i]);
            if(surfaces.Length==0)throw new InvalidOperationException("Mannequin hit surface is missing.");
        }

        public void Invalidate() { foreach(var surface in surfaces)surface.Dirty=true; }

        public bool Raycast(Ray3d ray, out HumanoidSurfaceHit hit)
        {
            hit=default;
            double nearest=Double.PositiveInfinity;
            foreach(var surface in surfaces)
                if(surface.Raycast(ray,out var candidate) && candidate.Distance<nearest)
                {hit=candidate;nearest=candidate.Distance;}
            return nearest<Double.PositiveInfinity;
        }

        public void Dispose() { foreach(var surface in surfaces)surface.Dispose(); }

        private sealed class Surface : IDisposable
        {
            private readonly SkinnedMeshRenderer renderer;
            private readonly Mesh baked=new Mesh { name="Posed target hit surface" };
            private readonly int[] triangles;
            // X/Y/Z are head/body/limb influence, interpolated at the actual hit.
            private readonly Vector3[] regions;
            private readonly List<Vector3> vertices=new List<Vector3>();
            public bool Dirty=true;

            public Surface(SkinnedMeshRenderer renderer)
            {
                this.renderer=renderer;
                var source=renderer.sharedMesh;
                if(!source.isReadable)throw new InvalidOperationException("Mannequin mesh must be imported with Read/Write enabled.");
                triangles=source.triangles;
                var weights=source.boneWeights;
                var bones=renderer.bones;
                if(weights.Length!=source.vertexCount)throw new InvalidOperationException("Mannequin skin weights are missing.");
                var boneRegions=new Vector3[bones.Length];
                for(int i=0;i<bones.Length;i++)boneRegions[i]=BoneRegion(bones[i]);
                regions=new Vector3[weights.Length];
                for(int i=0;i<weights.Length;i++)
                {
                    var w=weights[i];
                    regions[i]=boneRegions[w.boneIndex0]*w.weight0+boneRegions[w.boneIndex1]*w.weight1
                        +boneRegions[w.boneIndex2]*w.weight2+boneRegions[w.boneIndex3]*w.weight3;
                }
            }

            private static Vector3 BoneRegion(Transform bone)
            {
                // Descendants (fingers/toes) inherit their arm/leg region. Neck,
                // clavicles and pelvis belong to the torso; only Head scores 3.
                for(var current=bone;current!=null;current=current.parent)
                {
                    string name=current.name.ToLowerInvariant();
                    if(name=="head")return Vector3.right;
                    if(name.StartsWith("upperarm_") || name.StartsWith("thigh_"))return Vector3.forward;
                }
                return Vector3.up;
            }

            public bool Raycast(Ray3d ray, out HumanoidSurfaceHit hit)
            {
                hit=default;
                if(!renderer.enabled || !renderer.gameObject.activeInHierarchy)return false;
                if(Dirty)
                {
                    // Compensate the renderer scale here because worldToLocal
                    // below already accounts for it. Otherwise the fitted model
                    // scale is applied twice and the hit surface shrinks.
                    renderer.BakeMesh(baked,true);
                    baked.GetVertices(vertices);
                    baked.RecalculateBounds();
                    Dirty=false;
                }
                var inverse=renderer.transform.worldToLocalMatrix;
                var origin=inverse.MultiplyPoint3x4(ToUnity(ray.Origin));
                // Keep the inverse-transformed direction unnormalized: triangle
                // distances then remain world metres, even under the model scale.
                var direction=inverse.MultiplyVector(ToUnity(ray.Direction));
                if(!baked.bounds.IntersectRay(new Ray(origin,direction)))return false;
                float nearest=float.PositiveInfinity;
                Vector3 influence=default;
                for(int i=0;i<triangles.Length;i+=3)
                {
                    int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                    if(!Triangle(origin,direction,vertices[a],vertices[b],vertices[c],out float distance,out float u,out float v)
                        || distance>=nearest)continue;
                    nearest=distance;
                    influence=regions[a]*(1-u-v)+regions[b]*u+regions[c]*v;
                }
                if(float.IsPositiveInfinity(nearest))return false;
                string region=influence.x>influence.y && influence.x>influence.z?"head":influence.z>influence.y?"limb":"body";
                hit=new HumanoidSurfaceHit(region,nearest);
                return true;
            }

            // Two-sided Moller-Trumbore intersection: rotated/back-facing targets
            // use the same surface and no generous bounding-volume hit padding.
            private static bool Triangle(Vector3 origin,Vector3 direction,Vector3 a,Vector3 b,Vector3 c,
                out float distance,out float u,out float v)
            {
                distance=u=v=0;
                var edge1=b-a;var edge2=c-a;
                var cross=Vector3.Cross(direction,edge2);
                float determinant=Vector3.Dot(edge1,cross);
                if(Mathf.Abs(determinant)<1e-8f)return false;
                float inverse=1/determinant;
                var offset=origin-a;
                u=Vector3.Dot(offset,cross)*inverse;
                if(u<0 || u>1)return false;
                var q=Vector3.Cross(offset,edge1);
                v=Vector3.Dot(direction,q)*inverse;
                if(v<0 || u+v>1)return false;
                distance=Vector3.Dot(edge2,q)*inverse;
                return distance>0;
            }

            public void Dispose() { UnityEngine.Object.Destroy(baked); }
            private static Vector3 ToUnity(Vector3d value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
        }
    }
}
