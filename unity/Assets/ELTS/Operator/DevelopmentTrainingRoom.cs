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
        public float width = 30, height = 4.8f, depth = 52;
        public float rearOfEye = 3;
        public Color plaster = new Color(.81f, .79f, .73f);
        public Color floor = new Color(.63f, .66f, .63f);
        public Color ambient = new Color(.40f,.42f,.44f);
        public Color bounceColor = new Color(.90f,.93f,1);
        [Range(0,1)] public float bounceIntensity=.55f;
        public Color daylight = new Color(1f, .96f, .89f);
        public Vector3 directionToLight = new Vector3(-.6f, 1f, -.35f);
        [Range(.5f, 3)] public float lightIntensity = 1.3f;
    }

    /// <summary>
    /// A spacious, lit presentation shell around the configured target area.
    /// It has no colliders or connection to tracking, hitscan or physical geometry.
    /// All generated objects and materials belong to the view and die with it.
    /// </summary>
    internal static class DevelopmentTrainingRoom
    {
        public static void Create(Transform parent, ScreenPlane screen, double floorY, TrainingRoomSettings settings,
            List<Material> ownedMaterials, Material targetMaterial, DevelopmentGameSettings game, double targetDistanceM)
        {
            var root = new GameObject("Library interior (virtual metres)");
            root.transform.SetParent(parent, false);
            var centre = screen.Origin + screen.U * (screen.Width * .5) + screen.V * (screen.Height * .5);
            root.transform.position = new Vector3((float)centre.X, (float)floorY, (float)centre.Z);
            // The floor is horizontal in the room frame, even for a tilted display.
            var forward = Vector3.ProjectOnPlane(ToUnity(screen.Normal), Vector3.up);
            root.transform.rotation = Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : Vector3.forward, Vector3.up);

            var furniture=new LibraryInterior(ownedMaterials);
            var wall=furniture.Surface(settings.plaster,.1f,"white_plaster_02",new Vector2(8,2));
            var floor=furniture.Surface(settings.floor,.05f,"poly_wool_herringbone",new Vector2(40,65));
            var ceiling=furniture.Surface(new Color(.77f,.76f,.71f));
            var accent=furniture.Surface(new Color(.24f,.31f,.29f));
            var sky=furniture.Surface(new Color(.66f,.77f,.81f));
            sky.EnableKeyword("_EMISSION");sky.SetColor("_EmissionColor",new Color(.38f,.47f,.52f));
            var diffuser=furniture.Surface(new Color(.92f,.87f,.74f));
            diffuser.EnableKeyword("_EMISSION");diffuser.SetColor("_EmissionColor",new Color(2.1f,1.85f,1.45f));
            float width=Mathf.Max(30,settings.width),depth=Mathf.Max(52,settings.depth);
            float height=Mathf.Max(4.2f,settings.height);
            float front=-settings.rearOfEye,back=front+depth,mid=(front+back)*.5f;
            void Box(string name,Vector3 at,Vector3 size,Material material,bool shadows=true)
                =>LibraryInterior.Box(root.transform,name,at,size,material,shadows);

            Box("Stone floor",new Vector3(0,-.1f,mid),new Vector3(width,.2f,depth),floor);
            var walkway=furniture.Surface(new Color(.72f,.68f,.60f),.22f,"oak_veneer_03",new Vector2(2,26));
            foreach(float x in new[]{-9.8f,0,9.8f})
                Box("Oak circulation aisle",new Vector3(x,.004f,mid),new Vector3(x==0?1.0f:1.4f,.008f,depth),walkway);
            // The shell receives light but does not block the broad daylight approximation.
            // Furniture and actors still cast real-time shadows onto the floor and one another.
            Box("Far wall",new Vector3(0,height/2,back),new Vector3(width,height,.2f),wall,false);
            Box("Near wall",new Vector3(0,height/2,front),new Vector3(width,height,.2f),wall,false);
            Box("Ceiling",new Vector3(0,height+.1f,mid),new Vector3(width,.2f,depth),ceiling,false);
            foreach(float side in new[]{-1f,1f})
            {
                Box("Side wall",new Vector3(side*width/2,height/2,mid),new Vector3(.2f,height,depth),wall,false);
                Box("Oak skirting",new Vector3(side*(width/2-.13f),.08f,mid),new Vector3(.06f,.16f,depth),furniture.DarkOak);
                for(float z=front+3;z<back-2;z+=5)
                {
                    float x=side*(width/2-.12f);
                    Box("Tall window",new Vector3(x,2.35f,z),new Vector3(.04f,2.7f,3.7f),sky,false);
                    foreach(float edge in new[]{-1.9f,0,1.9f})
                        Box("Window mullion",new Vector3(x-side*.05f,2.35f,z+edge),new Vector3(.08f,2.85f,.055f),furniture.Metal);
                    foreach(float y in new[]{.97f,2.45f,3.73f})
                        Box("Window transom",new Vector3(x-side*.05f,y,z),new Vector3(.08f,.055f,3.85f),furniture.Metal);
                    Box("Oak window sill",new Vector3(x-side*.15f,.94f,z),new Vector3(.4f,.065f,4),furniture.Oak);
                    Box("Wall pilaster",new Vector3(x-side*.08f,height/2,z+2.45f),new Vector3(.25f,height,.32f),wall,false);
                }
            }
            // A restrained wall composition: collections, a quiet study alcove and upper glazing.
            Box("Study alcove wall",new Vector3(0,1.55f,back-.14f),new Vector3(6.2f,3.1f,.06f),accent,false);
            for(int bay=0;bay<10;bay++)
            {
                float x=-12.6f+bay*2.8f;
                if(Mathf.Abs(x)<3)continue;
                var shelf=LibraryInterior.Group(root.transform,"Perimeter collection bay",new Vector3(x,0,back-.55f));
                furniture.Bookcase(shelf,2.45f,2.65f,.50f,bay+73);
            }
            Box("Clerestory glazing",new Vector3(0,3.75f,back-.15f),new Vector3(width-.8f,1.15f,.04f),sky,false);
            for(float x=-width/2+.45f;x<width/2;x+=2.4f)
                Box("Clerestory mullion",new Vector3(x,3.75f,back-.2f),new Vector3(.05f,1.25f,.08f),furniture.Metal);
            foreach(float y in new[]{3.12f,4.38f})
                Box("Clerestory transom",new Vector3(0,y,back-.2f),new Vector3(width-.8f,.07f,.08f),furniture.Metal);
            Box("Alcove bench",new Vector3(0,.43f,back-.65f),new Vector3(4.8f,.16f,.8f),furniture.Fabric);
            Box("Alcove bench plinth",new Vector3(0,.18f,back-.6f),new Vector3(4.6f,.36f,.65f),furniture.DarkOak);
            var sign=LibraryInterior.Group(root.transform,"Library sign",new Vector3(0,2.15f,back-.21f));
            var label=sign.gameObject.AddComponent<TextMesh>();label.text="QUIET STUDY";label.anchor=TextAnchor.MiddleCenter;
            label.alignment=TextAlignment.Center;label.characterSize=.075f;label.fontSize=80;
            label.color=new Color(.78f,.75f,.64f);

            // Peripheral reading tables frame the central field without crossing entry aisles.
            foreach(float side in new[]{-1f,1f})
                for(int row=-2;row<5;row++)
                {
                    // The full table/chair group fits between offscreen entry lanes.
                    float z=(float)targetDistanceM+game.libraryFirstRowOffsetM+(row+.5f)*game.libraryRowSpacingM;
                    furniture.StudyTable(root.transform,new Vector3(side*11.6f,0,z));
                }
            // Oak ceiling rafts and suspended linear fixtures, spaced like real building services.
            for(float z=5;z<back;z+=8)
            {
                foreach(float x in new[]{-9f,0f,9f})
                {
                    Box("Acoustic ceiling raft",new Vector3(x,height-.08f,z),new Vector3(5.5f,.12f,2.1f),furniture.DarkOak,false);
                    for(int slat=0;slat<12;slat++)
                        Box("Oak acoustic slat",new Vector3(x-2.5f+slat*.45f,height-.16f,z),new Vector3(.12f,.08f,2.1f),furniture.Oak,false);
                    foreach(float offset in new[]{-1.6f,1.6f})
                        Box("Pendant cable",new Vector3(x+offset,height-.40f,z),new Vector3(.012f,.5f,.012f),furniture.Metal,false);
                    Box("Pendant housing",new Vector3(x,height-.68f,z),new Vector3(3.6f,.10f,.16f),furniture.Metal,false);
                    Box("Warm pendant diffuser",new Vector3(x,height-.738f,z),new Vector3(3.48f,.018f,.13f),diffuser,false);
                }
            }
            var lightHost=new GameObject("Soft window daylight");lightHost.transform.SetParent(root.transform,false);
            var direction=settings.directionToLight.normalized;
            var worldDirection=root.transform.TransformDirection(direction);
            lightHost.transform.rotation=Quaternion.LookRotation(-worldDirection);
            var light=lightHost.AddComponent<Light>();light.type=LightType.Directional;
            light.color=settings.daylight;light.intensity=settings.lightIntensity;
            light.shadows=LightShadows.Soft;light.shadowStrength=.70f;
            light.shadowBias=.025f;light.shadowNormalBias=.08f;
            light.cullingMask=1<<DevelopmentView.StimulusLayer;
            var fillHost=new GameObject("Ceiling and wall bounce");fillHost.transform.SetParent(root.transform,false);
            fillHost.transform.localRotation=Quaternion.Euler(-65,-25,0);
            var fill=fillHost.AddComponent<Light>();fill.type=LightType.Directional;
            fill.color=settings.bounceColor;fill.intensity=settings.bounceIntensity;fill.shadows=LightShadows.None;
            fill.cullingMask=1<<DevelopmentView.StimulusLayer;
            targetMaterial.SetVector("_LightDirection",worldDirection);
            StaticBatchingUtility.Combine(root);
        }

        private static Vector3 ToUnity(Vector3d value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
    }
}
