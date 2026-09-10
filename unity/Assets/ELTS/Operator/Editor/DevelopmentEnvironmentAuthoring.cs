#nullable enable
using System;
using System.IO;
using Elts.Config;
using Elts.Geometry;
using UnityEditor;
using UnityEngine;

namespace Elts.Operator.Editor
{
    /// <summary>Serializes a legible synthetic station from the same staged configuration used by DevelopmentView.</summary>
    internal static class DevelopmentEnvironmentAuthoring
    {
        private const string RootName = "ELTS synthetic environment (unmeasured)";
        private const int StimulusLayer = DevelopmentView.StimulusLayer, OperatorLayer = DevelopmentView.OperatorLayer;
        private const float GridStepMetres = 0.5f, FloorMarginMetres = 1.25f, FloorDropMetres = 0.8f;

        public static DevelopmentEnvironment CreateOrUpdate(Transform host)
        {
            var configuration=DevelopmentConfiguration.LoadDirectory(Path.Combine(Application.streamingAssetsPath,"config-generated"));
            var desktopInput=new DesktopInputModel(configuration);
            var environment=host.GetComponent<DevelopmentEnvironment>();
            if(environment==null)environment=Undo.AddComponent<DevelopmentEnvironment>(host.gameObject);
            var root=FindOrCreate(host,RootName,OperatorLayer);
            var floor=FindOrCreate(root,"Synthetic floor grid - configured room metres",StimulusLayer);
            var display=FindOrCreate(root,"Physical display position - configured",OperatorLayer);
            var preview=FindOrCreate(root,"Edit-mode pose and target previews",OperatorLayer);
            var cameras=FindOrCreate(root,"Saved development cameras",OperatorLayer);
            ClearOwnedChildren(floor);ClearOwnedChildren(display);ClearOwnedChildren(preview);ClearOwnedChildren(cameras);
            var floorMaterial=Material("ELTS_EnvironmentFloor",new Color(0.11f,0.18f,0.25f));
            var frameMaterial=Material("ELTS_EnvironmentFrame",new Color(0.08f,0.82f,0.95f));
            var targetMaterial=Material("ELTS_EnvironmentTarget",new Color(0.98f,0.66f,0.14f));
            var rayMaterial=Material("ELTS_EnvironmentRay",new Color(0.9f,0.25f,0.22f));
            var screen=configuration.Rig.Display;
            CreateConfiguredFloor(floor,screen,configuration.Scenario.TargetDistanceM,floorMaterial,frameMaterial);
            CreateConfiguredDisplay(display,screen,frameMaterial);
            CreateEditPreviews(preview,screen,configuration,desktopInput,frameMaterial,targetMaterial,rayMaterial);
            var participant=CreateCamera(cameras,"Participant display camera (configured at Play)",ToUnity(desktopInput.Head.Position),ToUnity(desktopInput.Head.Orientation));
            var outside=CreateCamera(cameras,"Operator outside view (configured at Play)",new Vector3(-3.2f,2.4f,-3.4f),Quaternion.Euler(18,42,0));
            environment.SetAuthoredReferences(display,floor);environment.SetEditPreviewReference(preview);environment.SetAuthoredCameras(participant,outside);EditorUtility.SetDirty(environment);
            return environment;
        }

        private static void CreateConfiguredFloor(Transform parent,ScreenPlane screen,double targetDistanceMetres,Material floorMaterial,Material gridMaterial)
        {
            var centre=ScreenCentre(screen);var normalStart=-DesktopInputModel.InitialEyeDistanceMetres-FloorMarginMetres;var normalEnd=targetDistanceMetres+FloorMarginMetres;
            var width=screen.Width+FloorMarginMetres*2;var depth=normalEnd-normalStart;var floorCentre=centre+screen.Normal*((normalStart+normalEnd)*0.5)-screen.V*(screen.Height*0.5+FloorDropMetres);var rotation=BasisRotation(screen);
            CreateCube(parent,"Floor slab (synthetic)",ToUnity(floorCentre),rotation,new Vector3((float)width,0.04f,(float)depth),floorMaterial,StimulusLayer);
            for(double u=-FloorMarginMetres;u<=screen.Width+FloorMarginMetres+0.001;u+=GridStepMetres)
                CreateCube(parent,"Grid U "+u.ToString("F1")+" m",ToUnity(screen.Origin+screen.U*u+screen.Normal*((normalStart+normalEnd)*0.5)-screen.V*(FloorDropMetres-0.025)),rotation,new Vector3(0.012f,0.012f,(float)depth),gridMaterial,StimulusLayer);
            for(double n=normalStart;n<=normalEnd+0.001;n+=GridStepMetres)
                CreateCube(parent,"Grid normal "+n.ToString("F1")+" m",ToUnity(centre+screen.Normal*n-screen.V*(screen.Height*0.5+FloorDropMetres-0.025)),rotation,new Vector3((float)width,0.012f,0.012f),gridMaterial,StimulusLayer);
        }
        private static void CreateConfiguredDisplay(Transform parent,ScreenPlane screen,Material material)
        {
            CreateFrame(parent,"Physical screen position",ToUnity(ScreenCentre(screen)),BasisRotation(screen),new Vector2((float)screen.Width,(float)screen.Height),material,OperatorLayer);
            foreach(var u in new[]{0.08,screen.Width-0.08})CreateCube(parent,"Display support (reference)",ToUnity(screen.Origin+screen.U*u-screen.V*0.45),BasisRotation(screen),new Vector3(0.045f,0.9f,0.045f),material,OperatorLayer);
        }
        private static void CreateEditPreviews(Transform parent,ScreenPlane screen,DevelopmentConfiguration configuration,DesktopInputModel desktop,Material headMaterial,Material targetMaterial,Material rayMaterial)
        {
            var head=CreateSphere(parent,"Default desktop head pose (edit preview)",ToUnity(desktop.Head.Position),ToUnity(desktop.Head.Orientation),new Vector3(0.15f,0.18f,0.17f),headMaterial,OperatorLayer);
            // Ray lengths are room metres, independent of the marker mesh scale.
            CreateCube(parent,"Head forward ray (+Z)",ToUnity(desktop.Head.Position+desktop.Head.Orientation.Rotate(new Vector3d(0,0,1))*1),ToUnity(desktop.Head.Orientation),new Vector3(0.012f,0.012f,2),rayMaterial,OperatorLayer);
            var weapon=CreateCube(parent,"Default desktop weapon pose (edit preview)",ToUnity(desktop.Weapon.Position),ToUnity(desktop.Weapon.Orientation),new Vector3(0.09f,0.08f,0.44f),targetMaterial,OperatorLayer);
            var boreRay=new BoreRay(desktop.Weapon,configuration.Rig.WeaponMuzzleOffsetM,configuration.Rig.WeaponZero,configuration.Rig.WeaponBoreLocalDirection).Ray;
            CreateCube(parent,"Weapon bore (+Z)",ToUnity(boreRay.Origin+boreRay.Direction*1),Quaternion.LookRotation(ToUnity(boreRay.Direction),ToUnity(screen.V)),new Vector3(0.012f,0.012f,2),targetMaterial,OperatorLayer);
            var targetCentre=ScreenCentre(screen)+screen.Normal*configuration.Scenario.TargetDistanceM;
            var bounds=new Vector3((float)configuration.Scenario.SpawnWidthM,(float)configuration.Scenario.SpawnHeightM,(float)Math.Max(configuration.Scenario.TargetRadiusM*2,0.05));
            CreateWireBox(parent,"Configured target spawn volume (edit preview)",ToUnity(targetCentre),BasisRotation(screen),bounds,targetMaterial);
            var diameter=(float)configuration.Scenario.TargetRadiusM*2;
            foreach(var offset in new[]{new Vector2(-0.25f,0.2f),new Vector2(0.2f,-0.15f),new Vector2(0.1f,0.25f)})
                CreateSphere(parent,"Sample target (edit preview)",ToUnity(targetCentre+screen.U*(offset.x*configuration.Scenario.SpawnWidthM)+screen.V*(offset.y*configuration.Scenario.SpawnHeightM)),Quaternion.identity,Vector3.one*diameter,targetMaterial,OperatorLayer);
        }
        private static void CreateWireBox(Transform parent,string name,Vector3 centre,Quaternion rotation,Vector3 size,Material material)
        {
            var root=Mark(new GameObject(name));root.layer=OperatorLayer;root.transform.SetParent(parent,false);root.transform.SetPositionAndRotation(centre,rotation);var half=size*0.5f;const float t=0.018f;
            foreach(var x in new[]{-half.x,half.x})foreach(var y in new[]{-half.y,half.y})CreateCube(root.transform,"Depth edge",new Vector3(x,y,0),Quaternion.identity,new Vector3(t,t,size.z),material,OperatorLayer);
            foreach(var x in new[]{-half.x,half.x})foreach(var z in new[]{-half.z,half.z})CreateCube(root.transform,"Vertical edge",new Vector3(x,0,z),Quaternion.identity,new Vector3(t,size.y,t),material,OperatorLayer);
            foreach(var y in new[]{-half.y,half.y})foreach(var z in new[]{-half.z,half.z})CreateCube(root.transform,"Width edge",new Vector3(0,y,z),Quaternion.identity,new Vector3(size.x,t,t),material,OperatorLayer);
        }
        private static Transform FindOrCreate(Transform parent,string name,int layer){var child=parent.Find(name);if(child!=null)return child;var created=new GameObject(name);Undo.RegisterCreatedObjectUndo(created,"Create ELTS synthetic environment");created.layer=layer;created.transform.SetParent(parent,false);return created.transform;}
        private static void ClearOwnedChildren(Transform parent){for(var i=parent.childCount-1;i>=0;i--){var child=parent.GetChild(i);if(child.GetComponent<DevelopmentEnvironmentGenerated>()!=null)Undo.DestroyObjectImmediate(child.gameObject);}}
        private static Material Material(string name,Color color)
        {
            const string folder="Assets/ELTS/Operator/Resources/ELTS/EnvironmentMaterials";if(!AssetDatabase.IsValidFolder(folder))AssetDatabase.CreateFolder("Assets/ELTS/Operator/Resources/ELTS","EnvironmentMaterials");var path=folder+"/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(material==null){var template=Resources.Load<Material>("ELTS/DevelopmentUnlit");var shader=template!=null?template.shader:Shader.Find("Universal Render Pipeline/Unlit");if(shader==null)throw new InvalidOperationException("URP Unlit shader is required for ELTS environment materials.");material=new Material(shader);AssetDatabase.CreateAsset(material,path);}material.SetColor("_BaseColor",color);material.SetColor("_Color",color);EditorUtility.SetDirty(material);return material;
        }
        private static GameObject CreateSphere(Transform parent,string name,Vector3 position,Quaternion rotation,Vector3 scale,Material material,int layer){var go=Mark(GameObject.CreatePrimitive(PrimitiveType.Sphere));go.name=name;go.layer=layer;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(position,rotation);go.transform.localScale=scale;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go;}
        private static GameObject CreateCube(Transform parent,string name,Vector3 position,Quaternion rotation,Vector3 scale,Material material,int layer){var go=Mark(GameObject.CreatePrimitive(PrimitiveType.Cube));go.name=name;go.layer=layer;go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localRotation=rotation;go.transform.localScale=scale;UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());go.GetComponent<Renderer>().sharedMaterial=material;return go;}
        private static void CreateFrame(Transform parent,string name,Vector3 centre,Quaternion rotation,Vector2 size,Material material,int layer){var frame=Mark(new GameObject(name));frame.layer=layer;frame.transform.SetParent(parent,false);frame.transform.SetPositionAndRotation(centre,rotation);const float t=0.035f;CreateCube(frame.transform,"Top",new Vector3(0,size.y/2,0),Quaternion.identity,new Vector3(size.x,t,t),material,layer);CreateCube(frame.transform,"Bottom",new Vector3(0,-size.y/2,0),Quaternion.identity,new Vector3(size.x,t,t),material,layer);CreateCube(frame.transform,"Left",new Vector3(-size.x/2,0,0),Quaternion.identity,new Vector3(t,size.y,t),material,layer);CreateCube(frame.transform,"Right",new Vector3(size.x/2,0,0),Quaternion.identity,new Vector3(t,size.y,t),material,layer);}
        private static Camera CreateCamera(Transform parent,string name,Vector3 position,Quaternion rotation){var go=Mark(new GameObject(name));go.layer=OperatorLayer;go.transform.SetParent(parent,false);go.transform.SetPositionAndRotation(position,rotation);var camera=go.AddComponent<Camera>();camera.enabled=false;return camera;}
        private static GameObject Mark(GameObject go){go.AddComponent<DevelopmentEnvironmentGenerated>();return go;}
        private static Vector3d ScreenCentre(ScreenPlane screen)=>screen.Origin+screen.U*(screen.Width*0.5)+screen.V*(screen.Height*0.5);
        private static Quaternion BasisRotation(ScreenPlane screen)=>Quaternion.LookRotation(ToUnity(screen.Normal),ToUnity(screen.V));
        private static Vector3 ToUnity(Vector3d value)=>new Vector3((float)value.X,(float)value.Y,(float)value.Z);
        private static Quaternion ToUnity(Quaterniond value)=>new Quaternion((float)value.X,(float)value.Y,(float)value.Z,(float)value.W);
    }
}
