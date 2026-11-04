#nullable enable
using System.Collections.Generic;
using Elts.Geometry;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>An open teaching bay. Scenery stays outside the target's clear lane;
    /// it supplies depth and context without changing the practice hit model.</summary>
    internal static class DevelopmentPracticeRoom
    {
        public static GameObject Create(Transform parent,ScreenPlane screen,double floorY,double targetDistance,
            Color floorColor,List<Material> materials)
        {
            var origin=screen.Origin+screen.U*(screen.Width*.5);
            var root=LibraryInterior.Group(parent,"Practice room",new Vector3((float)origin.X,(float)floorY,(float)origin.Z));
            root.rotation=Quaternion.LookRotation(ToUnity(screen.Normal),ToUnity(screen.V));
            var furniture=new LibraryInterior(materials);
            var floor=furniture.Surface(floorColor,.1f,"poly_wool_herringbone",new Vector2(8,14));
            var wall=furniture.Surface(new Color(.28f,.34f,.39f),.1f,"white_plaster_02",new Vector2(4,2));
            var lowerWall=furniture.Surface(new Color(.16f,.22f,.27f));
            var trim=furniture.Surface(new Color(.54f,.63f,.65f),.3f);
            var lane=furniture.Surface(new Color(.75f,.70f,.49f));
            var light=furniture.Surface(new Color(.82f,.89f,.94f));
            light.EnableKeyword("_EMISSION");light.SetColor("_EmissionColor",new Color(.7f,.8f,.9f));
            // All dimensions are virtual metres. The back wall follows the
            // configurable practice distance, always six metres behind the target.
            float back=(float)targetDistance+6,front=-4,depth=back-front,height=6,width=18;
            void Box(string name,Vector3 at,Vector3 size,Material material,bool shadows=false)
                =>LibraryInterior.Box(root,name,at,size,material,shadows);
            Box("Textured practice floor",new Vector3(0,-.08f,(front+back)/2),new Vector3(width,.16f,depth),floor);
            Box("Rear wall",new Vector3(0,height/2,back),new Vector3(width,height,.2f),wall);
            Box("Rear lower wall",new Vector3(0,.65f,back-.12f),new Vector3(width,1.3f,.08f),lowerWall);
            Box("Rear wall rail",new Vector3(0,1.31f,back-.17f),new Vector3(width,.045f,.08f),trim);
            foreach(float side in new[]{-1f,1f})
            {
                Box("Side wall",new Vector3(side*width/2,height/2,(front+back)/2),new Vector3(.16f,height,depth),wall);
                Box("Side wall base",new Vector3(side*(width/2-.1f),.65f,(front+back)/2),new Vector3(.07f,1.3f,depth),lowerWall);
                Box("Floor lane edge",new Vector3(side*1.45f,.007f,(back+1)/2),new Vector3(.045f,.014f,back-1),lane);
                Box("Rear window recess",new Vector3(side*5,3.1f,back-.15f),new Vector3(3.1f,2.5f,.12f),lowerWall);
                Box("Frosted window",new Vector3(side*5,3.1f,back-.23f),new Vector3(2.9f,2.3f,.025f),light);
                foreach(float offset in new[]{-.5f,.5f})
                    Box("Window mullion",new Vector3(side*5+offset,3.1f,back-.26f),new Vector3(.035f,2.3f,.035f),trim);
                Box("Side bench seat",new Vector3(side*6,.48f,back-3),new Vector3(2.4f,.10f,.55f),furniture.Oak,true);
                foreach(float leg in new[]{-.9f,.9f})
                    Box("Bench support",new Vector3(side*6+leg,.23f,back-3),new Vector3(.08f,.46f,.42f),furniture.Metal,true);
            }
            for(float z=2;z<back;z+=3)
            {
                Box("Floor joint",new Vector3(0,.003f,z),new Vector3(width,.006f,.025f),trim);
                foreach(float side in new[]{-1f,1f})
                    Box("Wall upright",new Vector3(side*8.82f,3,z),new Vector3(.12f,6,.12f),trim);
                Box("Ceiling crossbeam",new Vector3(0,height-.12f,z),new Vector3(width,.24f,.16f),lowerWall);
                Box("Overhead light",new Vector3(0,height-.27f,z),new Vector3(3,.025f,.13f),light);
            }
            Box("Target standing area",new Vector3(0,.009f,(float)targetDistance),new Vector3(2.6f,.018f,2.2f),lowerWall);
            LibraryInterior.Label(root,"PRACTICE",new Vector3(0,4.3f,back-.16f),.10f);
            return root.gameObject;
        }
        private static Vector3 ToUnity(Vector3d p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
    }
}
