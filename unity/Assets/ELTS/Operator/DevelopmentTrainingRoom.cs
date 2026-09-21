#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Elts.Operator
{
    /// <summary>Presentation settings in virtual metres; never apparatus measurements.</summary>
    [Serializable]
    public sealed class TrainingRoomSettings
    {
        public float width = 12, height = 6, depth = 26;
        public float floorBelowScreenCentre = 1.6f, rearOfEye = 4;
        public float tileSize = 1.5f;
        public Color plaster = new Color(.82f, .83f, .79f);
        public Color floor = new Color(.55f, .59f, .57f);
        public Color trim = new Color(.32f, .40f, .39f);
        public Color daylight = new Color(1f, .95f, .84f);
        public Vector3 directionToLight = new Vector3(-.45f, .7f, -.8f);
        [Range(.5f, 3)] public float lightIntensity = 1.5f;
    }

    /// <summary>
    /// A spacious, lit presentation shell around the configured target area.
    /// It has no colliders or connection to tracking, hitscan or physical geometry.
    /// All generated objects and materials belong to the view and die with it.
    /// </summary>
    internal static class DevelopmentTrainingRoom
    {
        public static void Create(Transform parent, ScreenPlane screen, TrainingRoomSettings settings,
            List<Material> ownedMaterials, Material targetMaterial)
        {
            var root = new GameObject("Daylit training room (virtual metres)");
            root.transform.SetParent(parent, false);
            var centre = screen.Origin + screen.U * (screen.Width * .5) + screen.V * (screen.Height * .5);
            root.transform.position = new Vector3((float)centre.X, (float)centre.Y, (float)centre.Z);
            root.transform.rotation = Quaternion.LookRotation(ToUnity(screen.Normal), ToUnity(screen.V));

            var template = Resources.Load<Material>("ELTS/TrainingRoomSurface");
            if (template == null) throw new InvalidOperationException("Training room surface material is missing.");
            Material Surface(Color color, float smoothness = .2f)
            {
                var material = new Material(template);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", smoothness);
                ownedMaterials.Add(material);
                return material;
            }
            var wall = Surface(settings.plaster);
            var floor = Surface(settings.floor, .3f);
            var trim = Surface(settings.trim);
            var seam = Surface(Color.Lerp(settings.floor, settings.trim, .3f));
            var sky = Surface(new Color(.76f, .87f, .91f));
            sky.EnableKeyword("_EMISSION");
            sky.SetColor("_EmissionColor", new Color(.45f, .56f, .60f));

            float width = Mathf.Max(6, settings.width), depth = Mathf.Max(10, settings.depth);
            float height = Mathf.Max(4, settings.height), bottom = -settings.floorBelowScreenCentre;
            float front = -settings.rearOfEye, back = front + depth, mid = (front + back) * .5f;
            void Box(string name, Vector3 position, Vector3 scale, Material material)
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
                part.name = name; part.layer = DevelopmentView.StimulusLayer;
                part.transform.SetParent(root.transform, false);
                part.transform.localPosition = position; part.transform.localScale = scale;
                // Disable immediately, including the frame before deferred destruction.
                var collider = part.GetComponent<Collider>(); collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
                var renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            }

            Box("Stone floor", new Vector3(0, bottom-.1f, mid), new Vector3(width,.2f,depth), floor);
            Box("Far wall", new Vector3(0,bottom+height*.5f,back), new Vector3(width,height,.2f), wall);
            // The near end stays open so the administrator's orbit can inspect the room.
            foreach (float side in new[]{-1f,1f})
            {
                Box("Side wall", new Vector3(side*width*.5f,bottom+height*.5f,mid), new Vector3(.2f,height,depth), wall);
                Box("Continuous skirting",new Vector3(side*(width*.5f-.13f),bottom+.18f,mid),new Vector3(.08f,.36f,depth),trim);
            }
            Box("Back skirting", new Vector3(0,bottom+.18f,back-.13f),new Vector3(width,.36f,.08f),trim);

            // Fine, low contrast joints supply perspective without the old luminous grid.
            float tile = Mathf.Max(.5f, settings.tileSize);
            for(float x=-width*.5f+tile;x<width*.5f;x+=tile)
                Box("Floor joint",new Vector3(x,bottom+.002f,mid),new Vector3(.012f,.004f,depth),seam);
            for(float z=front+tile;z<back;z+=tile)
                Box("Floor joint",new Vector3(0,bottom+.002f,z),new Vector3(width,.004f,.012f),seam);

            // Repeated structural bays and high windows make the room's scale legible.
            for(float z=front+1.5f;z<back;z+=3)
            {
                foreach(float side in new[]{-1f,1f})
                {
                    Box("Wall pier",new Vector3(side*(width*.5f-.18f),bottom+height*.5f,z),new Vector3(.3f,height,.18f),wall);
                    Box("High daylight panel",new Vector3(side*(width*.5f-.12f),bottom+height-1,z+1.4f),new Vector3(.04f,1.2f,2.2f),sky);
                }
                Box("Overhead beam",new Vector3(0,bottom+height-.15f,z),new Vector3(width,.3f,.22f),wall);
            }
            // A recessed back panel gives a clean silhouette behind the target field.
            Box("Back wall inset",new Vector3(0,bottom+height*.5f,back-.12f),new Vector3(width*.65f,height*.62f,.06f),Surface(new Color(.70f,.76f,.75f)));

            var lightHost = new GameObject("Warm directional daylight");
            lightHost.transform.SetParent(root.transform, false);
            var direction = settings.directionToLight.sqrMagnitude>.001f ? settings.directionToLight.normalized : Vector3.up;
            var worldDirection = root.transform.TransformDirection(direction);
            lightHost.transform.rotation = Quaternion.LookRotation(-worldDirection);
            var light = lightHost.AddComponent<Light>(); light.type = LightType.Directional;
            light.color = settings.daylight; light.intensity = settings.lightIntensity;
            light.shadows = LightShadows.Soft; light.shadowStrength = .65f;
            light.shadowBias = .025f; light.shadowNormalBias = .15f;
            light.cullingMask = 1 << DevelopmentView.StimulusLayer;
            // Sphere shading and architecture share the same world-space key light.
            targetMaterial.SetVector("_LightDirection", worldDirection);
        }

        private static Vector3 ToUnity(Vector3d value) => new Vector3((float)value.X,(float)value.Y,(float)value.Z);
    }
}
