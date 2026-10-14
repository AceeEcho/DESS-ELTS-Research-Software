#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Elts.Operator
{
    /// <summary>Small reusable furniture parts, in virtual metres. No physics colliders.</summary>
    internal sealed class LibraryInterior
    {
        private readonly List<Material> owned;
        public readonly Material Oak, DarkOak, Metal, Paper, Fabric;
        private readonly Material[] bindings;

        public LibraryInterior(List<Material> ownedMaterials)
        {
            owned=ownedMaterials;
            Oak=Surface(new Color(.83f,.76f,.65f),.3f,"oak_veneer_03");
            DarkOak=Surface(new Color(.40f,.35f,.29f),.22f,"oak_veneer_03");
            Metal=Surface(new Color(.12f,.14f,.14f),.38f);
            Paper=Surface(new Color(.76f,.72f,.62f),.08f);
            Fabric=Surface(new Color(.25f,.32f,.30f),.05f,"dirty_carpet");
            var colors=new[]{new Color(.25f,.32f,.33f),new Color(.40f,.21f,.18f),new Color(.57f,.49f,.35f),
                new Color(.24f,.28f,.36f),new Color(.42f,.44f,.38f),new Color(.63f,.59f,.49f),new Color(.28f,.20f,.20f)};
            bindings=new Material[colors.Length];
            for(int i=0;i<colors.Length;i++)bindings[i]=Surface(colors[i],.15f);
        }

        public Material Surface(Color color,float smoothness=.15f,string? texture=null,Vector2? tiling=null)
        {
            var template=Resources.Load<Material>(texture==null?"ELTS/TrainingRoomSurface":"ELTS/LibrarySurface");
            if(template==null)throw new InvalidOperationException("Training room surface material is missing.");
            var material=new Material(template){enableInstancing=true};
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",smoothness);
            if(texture!=null)
            {
                material.SetTexture("_BaseMap",Resources.Load<Texture2D>("ELTS/Library/"+texture+"_diff"));
                var normal=Resources.Load<Texture2D>("ELTS/Library/"+texture+"_nor_gl");
                if(normal!=null){material.SetTexture("_BumpMap",normal);material.SetFloat("_BumpScale",.35f);material.EnableKeyword("_NORMALMAP");}
                material.SetTextureScale("_BaseMap",tiling??Vector2.one);
            }
            owned.Add(material);return material;
        }

        public static Transform Group(Transform parent,string name,Vector3 position,float yaw=0)
        {
            var host=new GameObject(name);host.layer=DevelopmentView.StimulusLayer;
            host.transform.SetParent(parent,false);host.transform.localPosition=position;
            host.transform.localRotation=Quaternion.Euler(0,yaw,0);return host.transform;
        }

        public static GameObject Box(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool shadows=true)
        {
            var part=GameObject.CreatePrimitive(PrimitiveType.Cube);part.name=name;part.layer=DevelopmentView.StimulusLayer;
            part.transform.SetParent(parent,false);part.transform.localPosition=position;part.transform.localScale=size;
            var collider=part.GetComponent<Collider>();collider.enabled=false;UnityEngine.Object.Destroy(collider);
            var renderer=part.GetComponent<Renderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=shadows?ShadowCastingMode.On:ShadowCastingMode.Off;renderer.receiveShadows=true;
            return part;
        }

        public void Bookcase(Transform parent,float width,float height,float depth,int seed)
        {
            const float board=.035f;
            Box(parent,"Recessed oak back",new Vector3(0,height/2,depth/2-.025f),new Vector3(width,height,.05f),DarkOak);
            foreach(float side in new[]{-1f,1f})
                Box(parent,"Oak upright",new Vector3(side*(width-board)/2,height/2,0),new Vector3(board,height,depth),Oak);
            Box(parent,"Recessed plinth",new Vector3(0,.05f,.02f),new Vector3(width-.06f,.1f,depth-.08f),DarkOak);
            int rows=Mathf.Max(2,Mathf.RoundToInt(height/.36f));
            for(int row=0;row<=rows;row++)
            {
                float y=.1f+row*(height-.12f)/rows;
                Box(parent,"Shelf edge",new Vector3(0,y,0),new Vector3(width,.025f,depth),Oak);
                if(row==rows)continue;
                float gap=(height-.12f)/rows;
                // Seeded variation changes only book dressing, never target choreography.
                var random=new System.Random(seed*97+row*31);
                float x=-width/2+.07f;
                while(x<width/2-.12f)
                {
                    float w=.025f+(float)random.NextDouble()*.045f;
                    float h=gap*(.65f+(float)random.NextDouble()*.24f);
                    var binding=bindings[random.Next(bindings.Length)];
                    float z=-depth/2+.10f+(float)random.NextDouble()*.035f;
                    Box(parent,"Bound volume",new Vector3(x+w/2,y+.015f+h/2,z),new Vector3(w,h,.19f),binding);
                    if(random.Next(3)==0)
                        Box(parent,"Spine label",new Vector3(x+w/2,y+h*.27f,z-.096f),new Vector3(w*.7f,.018f,.002f),Paper,false);
                    x+=w+.009f;
                    if(random.Next(13)==0)x+=.1f;
                }
            }
        }

        public void Chair(Transform parent,Vector3 position,float yaw=0)
        {
            var root=Group(parent,"Reading chair",position,yaw);
            Box(root,"Upholstered seat",new Vector3(0,.45f,0),new Vector3(.43f,.07f,.43f),Fabric);
            var back=Box(root,"Upholstered back",new Vector3(0,.70f,.19f),new Vector3(.43f,.43f,.065f),Fabric);
            back.transform.localRotation=Quaternion.Euler(-7,0,0);
            foreach(float x in new[]{-.18f,.18f})foreach(float z in new[]{-.16f,.16f})
                Box(root,"Chair leg",new Vector3(x,.21f,z),new Vector3(.028f,.42f,.028f),Metal);
        }

        public void StudyTable(Transform parent,Vector3 position,float width=2.4f)
        {
            var root=Group(parent,"Shared study table",position);
            Box(root,"Oak tabletop",new Vector3(0,.75f,0),new Vector3(width,.045f,.9f),Oak);
            foreach(float x in new[]{-width/2+.12f,width/2-.12f})foreach(float z in new[]{-.33f,.33f})
                Box(root,"Table leg",new Vector3(x,.36f,z),new Vector3(.055f,.72f,.055f),Metal);
            foreach(float x in new[]{-width*.25f,width*.25f})
            {
                Chair(root,new Vector3(x,0,-.7f),180);
                Chair(root,new Vector3(x,0,.7f));
                Box(root,"Closed reference book",new Vector3(x,.794f,.15f),new Vector3(.25f,.035f,.32f),bindings[3]);
            }
        }
    }
}
