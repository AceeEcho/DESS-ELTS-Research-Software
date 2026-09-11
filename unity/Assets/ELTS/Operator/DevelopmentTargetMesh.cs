using UnityEngine;

namespace Elts.Operator
{
    /// <summary>A shared smooth sphere of unit diameter; configured target scale supplies metres.</summary>
    internal static class DevelopmentTargetMesh
    {
        public static Mesh Create(int longitudeSegments,int latitudeSegments)
        {
            longitudeSegments=Mathf.Clamp(longitudeSegments,24,128);
            latitudeSegments=Mathf.Clamp(latitudeSegments,12,64);
            int stride=longitudeSegments+1;
            var vertices=new Vector3[stride*(latitudeSegments+1)];
            var normals=new Vector3[vertices.Length];
            var triangles=new int[longitudeSegments*latitudeSegments*6];
            for(int latitude=0;latitude<=latitudeSegments;latitude++)
            {
                float polar=Mathf.PI*latitude/latitudeSegments;
                for(int longitude=0;longitude<=longitudeSegments;longitude++)
                {
                    float azimuth=2*Mathf.PI*longitude/longitudeSegments;
                    int index=latitude*stride+longitude;
                    var normal=new Vector3(Mathf.Sin(polar)*Mathf.Cos(azimuth),Mathf.Cos(polar),Mathf.Sin(polar)*Mathf.Sin(azimuth));
                    normals[index]=normal;vertices[index]=normal*.5f;
                }
            }
            int next=0;
            for(int latitude=0;latitude<latitudeSegments;latitude++)
                for(int longitude=0;longitude<longitudeSegments;longitude++)
                {
                    int top=latitude*stride+longitude,bottom=top+stride;
                    // Outside-facing winding; analytic normals avoid faceted lighting.
                    triangles[next++]=top;triangles[next++]=top+1;triangles[next++]=bottom;
                    triangles[next++]=top+1;triangles[next++]=bottom+1;triangles[next++]=bottom;
                }
            var mesh=new Mesh{name="Smooth development target sphere"};
            mesh.vertices=vertices;mesh.normals=normals;mesh.triangles=triangles;
            mesh.RecalculateBounds();return mesh;
        }
    }
}
