#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Scenario;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>A consistent clothed actor assembled from rounded meshes. This is
    /// presentation only; HumanoidGeometry owns the authoritative hit regions.</summary>
    internal sealed class DevelopmentHumanoidVisual : MonoBehaviour
    {
        private Transform leftArm=null!,rightArm=null!,leftLeg=null!,rightLeg=null!;
        internal sealed class Palette
        {
            public Material Jacket=null!,Shirt=null!,Pants=null!,Skin=null!,Hair=null!,Shoes=null!,Detail=null!;
        }
        public static Palette CreatePalette(List<Material> owned)=>new Palette {
            Jacket=Material(owned,new Color(.24f,.31f,.37f)),Shirt=Material(owned,new Color(.70f,.73f,.68f)),
            Pants=Material(owned,new Color(.19f,.23f,.27f)),Skin=Material(owned,new Color(.61f,.42f,.31f)),
            Hair=Material(owned,new Color(.12f,.10f,.09f)),Shoes=Material(owned,new Color(.10f,.11f,.12f)),
            Detail=Material(owned,new Color(.08f,.10f,.12f))};
        public static DevelopmentHumanoidVisual Create(Transform parent,Palette palette)
        {
            var root=new GameObject("Clothed synthetic actor");root.layer=DevelopmentView.StimulusLayer;
            root.transform.SetParent(parent,false);
            var actor=root.AddComponent<DevelopmentHumanoidVisual>();
            var jacket=palette.Jacket;var shirt=palette.Shirt;var pants=palette.Pants;
            var skin=palette.Skin;var hair=palette.Hair;var shoes=palette.Shoes;var detail=palette.Detail;
            Part(root.transform,"Jacket torso",PrimitiveType.Capsule,new Vector3(0,.19f,0),new Vector3(.42f,.34f,.25f),jacket);
            Part(root.transform,"Shirt front",PrimitiveType.Cube,new Vector3(0,.20f,-.13f),new Vector3(.10f,.31f,.025f),shirt);
            Part(root.transform,"Jacket left front",PrimitiveType.Cube,new Vector3(-.11f,.20f,-.145f),new Vector3(.11f,.32f,.025f),jacket);
            Part(root.transform,"Jacket right front",PrimitiveType.Cube,new Vector3(.11f,.20f,-.145f),new Vector3(.11f,.32f,.025f),jacket);
            Part(root.transform,"Belt",PrimitiveType.Cube,new Vector3(0,-.25f,-.13f),new Vector3(.36f,.055f,.025f),detail);
            for(int button=0;button<3;button++)
                Part(root.transform,"Jacket button",PrimitiveType.Sphere,new Vector3(.045f,.29f-button*.11f,-.165f),new Vector3(.018f,.018f,.014f),detail);
            Part(root.transform,"Shirt collar",PrimitiveType.Cylinder,new Vector3(0,.47f,-.04f),new Vector3(.15f,.025f,.14f),shirt);
            Part(root.transform,"Trousers waist",PrimitiveType.Capsule,new Vector3(0,-.17f,0),new Vector3(.38f,.20f,.25f),pants);
            Part(root.transform,"Neck",PrimitiveType.Cylinder,new Vector3(0,.48f,0),new Vector3(.12f,.07f,.12f),skin);
            Part(root.transform,"Head and face",PrimitiveType.Sphere,new Vector3(0,.65f,0),new Vector3(.22f,.27f,.21f),skin);
            Part(root.transform,"Short hair",PrimitiveType.Sphere,new Vector3(0,.76f,.015f),new Vector3(.23f,.12f,.22f),hair);
            Part(root.transform,"Nose",PrimitiveType.Sphere,new Vector3(0,.64f,-.105f),new Vector3(.045f,.055f,.065f),skin);
            Part(root.transform,"Mouth",PrimitiveType.Cube,new Vector3(0,.56f,-.105f),new Vector3(.07f,.012f,.012f),detail);
            foreach(float side in new[]{-1f,1f})
            {
                Part(root.transform,"Ear",PrimitiveType.Sphere,new Vector3(side*.115f,.64f,0),new Vector3(.04f,.065f,.05f),skin);
                Part(root.transform,"Eye",PrimitiveType.Sphere,new Vector3(side*.045f,.68f,-.102f),new Vector3(.024f,.018f,.012f),detail);
                Part(root.transform,"Brow",PrimitiveType.Cube,new Vector3(side*.045f,.714f,-.103f),new Vector3(.065f,.015f,.014f),hair);
                var arm=new GameObject(side<0?"Left sleeve and hand":"Right sleeve and hand");
                arm.layer=DevelopmentView.StimulusLayer;arm.transform.SetParent(root.transform,false);
                arm.transform.localPosition=new Vector3(side*.30f,.38f,0);
                Part(arm.transform,"Upper sleeve",PrimitiveType.Capsule,new Vector3(0,-.11f,0),new Vector3(.14f,.20f,.16f),jacket);
                Part(arm.transform,"Lower sleeve",PrimitiveType.Capsule,new Vector3(0,-.32f,0),new Vector3(.12f,.16f,.14f),jacket);
                Part(arm.transform,"Hand",PrimitiveType.Sphere,new Vector3(0,-.49f,-.015f),new Vector3(.11f,.13f,.10f),skin);
                if(side<0)actor.leftArm=arm.transform;else actor.rightArm=arm.transform;
                var leg=new GameObject(side<0?"Left trouser leg":"Right trouser leg");
                leg.layer=DevelopmentView.StimulusLayer;leg.transform.SetParent(root.transform,false);
                leg.transform.localPosition=new Vector3(side*.11f,-.31f,0);
                Part(leg.transform,"Trouser",PrimitiveType.Capsule,new Vector3(0,-.25f,0),new Vector3(.17f,.27f,.19f),pants);
                Part(leg.transform,"Shoe",PrimitiveType.Cube,new Vector3(0,-.51f,-.055f),new Vector3(.18f,.12f,.31f),shoes);
                if(side<0)actor.leftLeg=leg.transform;else actor.rightLeg=leg.transform;
            }
            return actor;
        }
        public void Pose(float heightScale,bool running,float time)
        {
            transform.localScale=new Vector3(1,heightScale,1);
            float swing=running?Mathf.Sin(time*11f)*25f:0;
            leftArm.localRotation=Quaternion.Euler(-swing,0,0);rightArm.localRotation=Quaternion.Euler(swing,0,0);
            leftLeg.localRotation=Quaternion.Euler(swing,0,0);rightLeg.localRotation=Quaternion.Euler(-swing,0,0);
        }

        public static void CreateCovers(Transform parent,IReadOnlyList<HumanoidCover> covers,List<Material> owned)
        {
            var wood=Material(owned,new Color(.42f,.30f,.19f));
            var concrete=Material(owned,new Color(.45f,.48f,.47f));
            var metal=Material(owned,new Color(.21f,.28f,.30f));
            var trim=Material(owned,new Color(.14f,.17f,.18f));
            foreach(var cover in covers)
            {
                var host=new GameObject("Cover · "+cover.Id);host.layer=DevelopmentView.StimulusLayer;
                host.transform.SetParent(parent,false);
                host.transform.position=new Vector3((float)cover.Center.X,(float)cover.Center.Y,(float)cover.Center.Z);
                Vector3 full=new Vector3((float)cover.HalfSize.X*2,(float)cover.HalfSize.Y*2,(float)cover.HalfSize.Z*2);
                if(cover.Id=="crate-stack")
                {
                    Part(host.transform,"Stacked timber crates",PrimitiveType.Cube,Vector3.zero,full,wood);
                    for(int i=-1;i<=1;i++)Part(host.transform,"Crate seam",PrimitiveType.Cube,new Vector3(0,i*.32f,-full.z*.51f),new Vector3(full.x,.018f,.015f),trim);
                }
                else if(cover.Id=="concrete-barrier")
                {
                    Part(host.transform,"Concrete road barrier",PrimitiveType.Cube,Vector3.zero,full,concrete);
                    Part(host.transform,"Barrier dark footing",PrimitiveType.Cube,new Vector3(0,-full.y*.42f,0),new Vector3(full.x*1.12f,.13f,full.z*1.15f),trim);
                }
                else
                {
                    for(int i=-1;i<=1;i+=2)
                    {
                        Part(host.transform,"Steel drum",PrimitiveType.Cylinder,new Vector3(i*.13f,0,0),new Vector3(.26f,full.y*.5f,.26f),metal);
                        Part(host.transform,"Drum cap",PrimitiveType.Cylinder,new Vector3(i*.13f,full.y*.5f,0),new Vector3(.27f,.025f,.27f),trim);
                    }
                }
            }
        }

        private static Material Material(List<Material> owned,Color color)
        {
            var template=Resources.Load<Material>("ELTS/TrainingRoomSurface");
            if(template==null)throw new InvalidOperationException("Training room surface material is missing.");
            var result=new Material(template);result.SetColor("_BaseColor",color);owned.Add(result);return result;
        }
        private static GameObject Part(Transform parent,string name,PrimitiveType shape,Vector3 position,Vector3 scale,Material material)
        {
            var part=GameObject.CreatePrimitive(shape);part.name=name;part.layer=DevelopmentView.StimulusLayer;
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=scale;
            var collider=part.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial=material;return part;
        }
    }
}
