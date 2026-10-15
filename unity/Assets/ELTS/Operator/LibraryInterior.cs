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
            // Keep the solid back aligned with the shared shot-occlusion volume.
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

        public static void Label(Transform parent,string text,Vector3 position,float size,float yaw=0)
        {
            var root=Group(parent,"Sign · "+text,position,yaw);
            var label=root.gameObject.AddComponent<TextMesh>();label.text=text;label.anchor=TextAnchor.MiddleCenter;
            label.alignment=TextAlignment.Center;label.characterSize=size*.3f;label.fontSize=64;
            label.color=new Color(.90f,.88f,.79f);
        }

        public void Fixture(Transform parent,LibraryFixture fixture,int seed)
        {
            var p=fixture.Center;var s=fixture.Size;
            var root=Group(parent,"Fixture · "+fixture.Id,new Vector3((float)p.X,0,(float)p.Z));
            float width=(float)s.X,height=(float)s.Y,depth=(float)s.Z;
            switch(fixture.Kind)
            {
                case LibraryFixtureKind.Stack:
                case LibraryFixtureKind.LowShelf:
                    if(depth>3)
                    {
                        root.localRotation=Quaternion.Euler(0,90,0);
                        int bays=Mathf.RoundToInt(depth/2);
                        for(int bay=0;bay<bays;bay++)
                        {
                            float x=-depth/2+(bay+.5f)*depth/bays;
                            Bookcase(Group(root,"Collection front",new Vector3(x,0,-width/4)),depth/bays,height,width/2,seed+bay);
                            Bookcase(Group(root,"Collection back",new Vector3(x,0,width/4),180),depth/bays,height,width/2,seed+bay+50);
                        }
                    }
                    else Bookcase(root,width,height,depth,seed);
                    break;
                case LibraryFixtureKind.Table:
                    Box(root,"Oak tabletop",new Vector3(0,.7375f,0),new Vector3(width,.045f,depth),Oak);
                    foreach(float x in new[]{-1f,1f})foreach(float z in new[]{-1f,1f})
                        Box(root,"Table leg",new Vector3(x*(width/2-.12f),.3575f,z*(depth/2-.12f)),new Vector3(.055f,.715f,.055f),Metal);
                    break;
                case LibraryFixtureKind.Chair:
                    Chair(root,Vector3.zero,fixture.Facing);
                    break;
                case LibraryFixtureKind.Counter:
                    Box(root,"Counter cabinet",new Vector3(0,(height-.04f)/2,0),new Vector3(width,height-.04f,depth),DarkOak);
                    Box(root,"Countertop",new Vector3(0,height-.02f,0),new Vector3(width,.04f,depth),Oak);
                    for(float x=-width/2+.12f;x<width/2;x+=.12f)
                        Box(root,"Counter oak fluting",new Vector3(x,.50f,-depth/2-.002f),new Vector3(.035f,.90f,.008f),Oak,false);
                    break;
                case LibraryFixtureKind.Lounge:
                    Box(root,"Upholstered base",new Vector3(0,.25f,0),new Vector3(width,.5f,depth),Fabric);
                    Box(root,"Cushioned back",new Vector3(0,.65f,.35f),new Vector3(width,.4f,.30f),Fabric);
                    foreach(float x in new[]{-1f,1f})
                        Box(root,"Lounge arm",new Vector3(x*(width/2-.12f),.6f,0),new Vector3(.24f,.3f,depth),Fabric);
                    break;
                default:
                    Box(root,"Column",new Vector3(0,height/2,0),new Vector3(width,height,depth),Paper);
                    break;
            }
        }

        public void BookPile(Transform parent,Vector3 position,int seed)
        {
            var pile=Group(parent,"Reader books",position);
            for(int i=0;i<3;i++)
            {
                var book=Box(pile,"Hardcover",new Vector3(i*.009f,.017f+i*.038f,0),new Vector3(.24f,.034f,.31f),bindings[(seed+i)%bindings.Length]);
                book.transform.localRotation=Quaternion.Euler(0,i*8-5,0);
                Box(book.transform,"Page edges",new Vector3(0,0,-.006f),new Vector3(.94f,.66f,.96f),Paper,false);
            }
        }
        public void TableLamp(Transform parent,Vector3 position,Material diffuser)
        {
            var root=Group(parent,"Reading lamp",position);
            Box(root,"Lamp base",new Vector3(0,.015f,0),new Vector3(.20f,.03f,.16f),Metal);
            Box(root,"Lamp stem",new Vector3(0,.20f,.035f),new Vector3(.018f,.38f,.018f),Metal);
            Box(root,"Lamp shade",new Vector3(0,.39f,-.015f),new Vector3(.32f,.08f,.13f),Fabric);
            Box(root,"Lamp diffuser",new Vector3(0,.346f,-.015f),new Vector3(.28f,.008f,.10f),diffuser,false);
        }
        public void Monitor(Transform parent,Vector3 position)
        {
            var root=Group(parent,"Circulation workstation",position,180);
            Box(root,"Monitor base",new Vector3(0,.02f,0),new Vector3(.24f,.04f,.18f),Metal);
            Box(root,"Monitor stand",new Vector3(0,.15f,0),new Vector3(.04f,.26f,.035f),Metal);
            Box(root,"Monitor",new Vector3(0,.33f,0),new Vector3(.48f,.30f,.035f),Metal);
            Box(root,"Monitor screen",new Vector3(0,.33f,-.02f),new Vector3(.44f,.26f,.004f),Fabric,false);
            Box(root,"Keyboard",new Vector3(0,.014f,-.22f),new Vector3(.38f,.028f,.13f),Metal);
        }
        public void Plant(Transform parent,Vector3 position)
        {
            var root=Group(parent,"Window planter",position);
            Box(root,"Planter",new Vector3(0,.24f,0),new Vector3(.42f,.48f,.42f),DarkOak);
            for(int i=0;i<9;i++)
            {
                float angle=i*137.5f*Mathf.Deg2Rad;
                var leaf=GameObject.CreatePrimitive(PrimitiveType.Sphere);leaf.name="Leaf";leaf.layer=DevelopmentView.StimulusLayer;
                leaf.transform.SetParent(root,false);leaf.transform.localPosition=new Vector3(Mathf.Cos(angle)*.18f,.57f+i*.035f,Mathf.Sin(angle)*.18f);
                leaf.transform.localScale=new Vector3(.14f,.38f,.055f);leaf.transform.localRotation=Quaternion.Euler(25,i*137.5f,35);
                UnityEngine.Object.Destroy(leaf.GetComponent<Collider>());leaf.GetComponent<Renderer>().sharedMaterial=Fabric;
            }
        }
    }
}
