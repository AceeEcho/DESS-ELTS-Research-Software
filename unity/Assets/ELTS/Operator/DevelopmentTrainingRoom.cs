#nullable enable
using System.Collections.Generic;
using Elts.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Elts.Operator
{
    /// <summary>Presentation lighting only; floor-plan dimensions live in LibraryFloorPlan.</summary>
    [System.Serializable]
    public sealed class TrainingRoomSettings
    {
        public Color plaster=new Color(.81f,.80f,.75f), floor=new Color(.48f,.50f,.48f);
        public Color ambient=new Color(.33f,.35f,.37f), bounceColor=new Color(.90f,.93f,1);
        [Range(0,1)] public float bounceIntensity=.38f;
        public Color daylight=new Color(1,.96f,.88f);
        public Vector3 directionToLight=new Vector3(.75f,1,-.4f);
        [Range(.5f,3)] public float lightIntensity=1.45f;
        public Color windowBounce = new Color(.88f,.94f,1f);
        [Range(0,30)] public float windowIntensity = 16;
        [Range(2,18)] public float windowReach = 10;
        [Range(0,1)] public float shadowStrength = .42f;
        [Range(0,1)] public float ceilingGlow = .30f;
    }

    /// <summary>One coherent library: a service desk by the entrance, double-sided
    /// collections, shared reading tables by windows and clear cross aisles. Large
    /// furniture is rendered from the same plan used for routes and shot occlusion.</summary>
    internal static class DevelopmentTrainingRoom
    {
        public static Transform Create(Transform parent,ScreenPlane screen,double floorY,TrainingRoomSettings settings,
            List<Material> ownedMaterials,Material targetMaterial,DevelopmentGameSettings game,double targetDistanceM)
        {
            var center=screen.Origin+screen.U*(screen.Width*.5)+screen.V*(screen.Height*.5)+screen.Normal*targetDistanceM;
            var root=LibraryInterior.Group(parent,"Library interior (virtual metres)",new Vector3((float)center.X,(float)floorY,(float)center.Z));
            root.localScale=new Vector3(game.libraryLayoutScale,1,game.libraryLayoutScale);
            var furniture=new LibraryInterior(ownedMaterials);
            var wall=furniture.Surface(settings.plaster,.12f,"white_plaster_02",new Vector2(8,2));
            var carpet=furniture.Surface(settings.floor,.05f,"poly_wool_herringbone",new Vector2(20,32));
            var readingCarpet=furniture.Surface(new Color(.72f,.80f,.77f),.08f,"dirty_carpet",new Vector2(7,18));
            var ceiling=furniture.Surface(new Color(.84f,.84f,.80f),.04f);
            var ceilingFinish=furniture.PaintedSurface("acoustic-ceiling",.05f);
            ceilingFinish.EnableKeyword("_EMISSION");
            ceilingFinish.SetTexture("_EmissionMap",ceilingFinish.GetTexture("_BaseMap"));
            ceilingFinish.SetColor("_EmissionColor",Color.white*settings.ceilingGlow);
            var lightSurface=furniture.Surface(new Color(.96f,.92f,.81f));
            lightSurface.EnableKeyword("_EMISSION");lightSurface.SetColor("_EmissionColor",new Color(1.4f,1.3f,1.1f));
            const float front=-10.5f,back=28,height=3.8f,width=22;
            void Box(string name,Vector3 at,Vector3 size,Material material,bool shadows=true)
                =>LibraryInterior.Box(root,name,at,size,material,shadows);

            Box("Library carpet",new Vector3(0,-.1f,(front+back)/2),new Vector3(width,.2f,back-front),carpet);
            Box("Window reading area carpet",new Vector3(6.2f,.003f,14.1f),new Vector3(7.9f,.006f,21),readingCarpet,false);
            // The outside camera sits behind this wall when centered on the player.
            // Cut it away only in that view, leaving the rest of the library visible.
            var nearWall=LibraryInterior.Box(root,"Near wall",new Vector3(0,height/2,front),new Vector3(width,height,.2f),wall,false);
            nearWall.layer=DevelopmentView.ParticipantBackdropLayer;
            LibraryWindows.RearWall(root,back,width,height,furniture,wall);
            Box("Ceiling",new Vector3(0,height+.06f,(front+back)/2),new Vector3(width,.12f,back-front),ceiling,false);
            var ceilingPanel=furniture.Model(root,"Acoustic ceiling finish","AcousticCeiling",new Vector3(0,height-.006f,(front+back)/2),
                new Vector3(width,1,back-front),ceilingFinish);
            ceilingPanel.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            // Glazing occupies the same closed wall span. Only its visual surface is divided.
            foreach(var cover in LibraryFloorPlan.Covers(Vector3d.Zero,new DevelopmentGameSettings(),0))
                if(cover.Id.StartsWith("wall-1-"))
                    LibraryWindows.EastWall(root,cover.Id,Unity(cover.Center),Unity(cover.HalfSize*2),furniture,wall);
                else if(cover.Id.StartsWith("wall-") || cover.Id.StartsWith("lintel-"))
                    Box(cover.Id,Unity(cover.Center),Unity(cover.HalfSize*2),wall,false);

            foreach(int side in new[]{-1,1})
            {
                var doors=side<0?LibraryFloorPlan.LeftDoors:LibraryFloorPlan.RightDoors;
                for(int i=0;i<doors.Length;i++)
                {
                    float z=(float)doors[i],x=side*10.88f;
                    foreach(float edge in new[]{-.92f,.92f})
                        Box("Door jamb",new Vector3(x,1.1f,z+edge),new Vector3(.12f,2.2f,.09f),furniture.Oak);
                    Box("Door header",new Vector3(x,2.24f,z),new Vector3(.12f,.10f,1.94f),furniture.Oak);
                    // A dark short vestibule gives incoming actors a real occluded origin.
                    Box("Vestibule back",new Vector3(side*12.6f,1.15f,z),new Vector3(.1f,2.3f,2.2f),furniture.DarkOak,false);
                    foreach(float edge in new[]{-1.08f,1.08f})
                        Box("Vestibule return",new Vector3(side*11.8f,1.15f,z+edge),new Vector3(1.6f,2.3f,.1f),wall,false);
                }
            }
            LibraryWindows.Courtyard(root,furniture,settings);

            int seed=0;
            foreach(var fixture in LibraryFloorPlan.Fixtures)furniture.Fixture(root,fixture,seed++);
            // Service tools and reader belongings establish function without filling the aisles.
            furniture.Monitor(root,new Vector3(-3.8f,1.02f,1.8f));
            furniture.BookPile(root,new Vector3(-2.8f,1.02f,1.7f),17);
            foreach(var fixture in LibraryFloorPlan.Fixtures)
            {
                if(fixture.Kind==LibraryFixtureKind.Table)
                {
                    var p=new Vector3((float)fixture.Center.X,.76f,(float)fixture.Center.Z);
                    furniture.TableLamp(root,p+new Vector3(.55f,0,.08f),lightSurface);
                    furniture.BookPile(root,p+new Vector3(-.55f,0,.17f),fixture.Id.Length);
                    Box("Reader notebook",p+new Vector3(.35f,.012f,-.30f),new Vector3(.28f,.024f,.21f),furniture.Paper);
                }
            }
            // Long suspended lights follow the reading area; compact lights run across
            // the stack bays. There is no repeating theatrical raft over each target.
            foreach(float z in new[]{2f,7.5f,14.4f,21f,26f})
                foreach(float x in new[]{-5.4f,1f,6.8f})
                {
                    Box("Pendant housing",new Vector3(x,3.35f,z),new Vector3(2.4f,.075f,.17f),furniture.Metal,false);
                    Box("Pendant diffuser",new Vector3(x,3.305f,z),new Vector3(2.32f,.018f,.14f),lightSurface,false);
                    foreach(float offset in new[]{-.85f,.85f})
                        Box("Pendant suspension",new Vector3(x+offset,3.59f,z),new Vector3(.008f,.43f,.008f),furniture.Metal,false);
                }
            for(int i=0;i<7;i++)
                Box("Reading area acoustic baffle",new Vector3(6.4f,3.66f,4+i*3.2f),new Vector3(7,.16f,.10f),furniture.Oak,false);
            furniture.Plant(root,new Vector3(9.8f,0,11.1f));
            furniture.Plant(root,new Vector3(-9.8f,0,26.6f));
            var lightHost=new GameObject("Window daylight");lightHost.transform.SetParent(root,false);
            var direction=settings.directionToLight.normalized;
            lightHost.transform.rotation=Quaternion.LookRotation(-direction);
            var sun=lightHost.AddComponent<Light>();sun.type=LightType.Directional;sun.color=settings.daylight;sun.intensity=settings.lightIntensity;
            sun.shadows=LightShadows.Soft;sun.shadowStrength=settings.shadowStrength;sun.shadowBias=.02f;sun.shadowNormalBias=.06f;sun.cullingMask=1<<DevelopmentView.StimulusLayer;
            var fillHost=new GameObject("Indirect library light");fillHost.transform.SetParent(root,false);
            fillHost.transform.rotation=Quaternion.Euler(-65,-25,0);
            var fill=fillHost.AddComponent<Light>();fill.type=LightType.Directional;fill.color=settings.bounceColor;
            fill.intensity=settings.bounceIntensity;fill.shadows=LightShadows.None;fill.cullingMask=1<<DevelopmentView.StimulusLayer;
            targetMaterial.SetVector("_LightDirection",direction);
            StaticBatchingUtility.Combine(root.gameObject);
            return root;
        }
        private static Vector3 Unity(Vector3d value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
    }
}
