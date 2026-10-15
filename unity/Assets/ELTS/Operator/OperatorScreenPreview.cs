#nullable enable
using Elts.Geometry;
using Elts.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Elts.Operator
{
    /// <summary>A view-only monitor at the configured physical display plane. Its
    /// texture uses the same off-axis camera as the participant, never a second eye.</summary>
    internal sealed class OperatorScreenPreview
    {
        private readonly Mesh mesh;
        private readonly Renderer surface;
        private readonly RenderTexture texture;
        public OperatorScreenPreview(Transform parent,Material material)
        {
            texture=new RenderTexture(1024,576,24,RenderTextureFormat.ARGB32){name="Operator physical screen feed"};
            texture.Create();
            var go=new GameObject("Physical display · participant image");
            go.layer=DevelopmentView.OperatorLayer;go.transform.SetParent(parent,false);
            mesh=new Mesh{name="Physical display rectangle"};
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            surface=go.AddComponent<MeshRenderer>();surface.sharedMaterial=material;
            material.SetTexture("_BaseMap",texture);
            material.SetFloat("_Cull",0);
        }
        public void Update(ScreenPlane screen,bool valid)
        {
            var o=screen.Origin;
            mesh.vertices=new[]{OffAxisCamera.ToUnity(o),OffAxisCamera.ToUnity(o+screen.U*screen.Width),
                OffAxisCamera.ToUnity(o+screen.U*screen.Width+screen.V*screen.Height),OffAxisCamera.ToUnity(o+screen.V*screen.Height)};
            mesh.uv=new[]{Vector2.zero,Vector2.right,Vector2.one,Vector2.up};
            mesh.triangles=new[]{0,2,1,0,3,2};mesh.RecalculateBounds();
            surface.enabled=valid; // Never display a stale image during head dropout.
        }
        public void Capture(Camera participant)
        {
            if(!surface.enabled)return;
            var request=new RenderPipeline.StandardRequest{destination=texture};
            if(RenderPipeline.SupportsRenderRequest(participant,request))RenderPipeline.SubmitRenderRequest(participant,request);
        }
        public void Dispose(){texture.Release();Object.Destroy(texture);Object.Destroy(mesh);}
    }
}
