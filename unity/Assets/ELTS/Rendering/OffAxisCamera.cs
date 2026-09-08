using Elts.Geometry;
using UnityEngine;

namespace Elts.Rendering
{
    /// <summary>Unity adapter: camera orientation follows the screen basis, never a look-at approximation.</summary>
    public static class OffAxisCamera
    {
        public static void Apply(Camera camera, OffAxisProjection projection)
        {
            camera.transform.SetPositionAndRotation(ToUnity(projection.Eye),
                Quaternion.LookRotation(ToUnity(projection.Forward), ToUnity(projection.Up)));
            camera.nearClipPlane = (float)projection.Near;
            camera.farClipPlane = (float)projection.Far;
            camera.projectionMatrix = Matrix4x4.Frustum((float)projection.Left, (float)projection.RightExtent,
                (float)projection.Bottom, (float)projection.Top, (float)projection.Near, (float)projection.Far);
        }
        public static Vector3 ToUnity(Vector3d value) => new Vector3((float)value.X, (float)value.Y, (float)value.Z);
    }
}
