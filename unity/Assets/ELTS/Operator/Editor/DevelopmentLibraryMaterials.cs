using UnityEditor;
using UnityEngine;

namespace Elts.Operator.Editor
{
    /// <summary>Keep the library's runtime material features in standalone shader builds.</summary>
    public static class DevelopmentLibraryMaterials
    {
        [MenuItem("ELTS/Development/Rebuild library material templates")]
        public static void Rebuild()
        {
            var source=Resources.Load<Material>("ELTS/TrainingRoomSurface");
            if(source==null)throw new System.InvalidOperationException("Training room material is missing.");
            Save("LibrarySurface",material=>
            {
                material.SetTexture("_BaseMap",Resources.Load<Texture2D>("ELTS/Library/oak_veneer_03_diff"));
                material.SetTexture("_BumpMap",Resources.Load<Texture2D>("ELTS/Library/oak_veneer_03_nor_gl"));
                material.EnableKeyword("_NORMALMAP");
            });
            Save("LibraryEmission",material=>
            {
                material.SetColor("_EmissionColor",new Color(2.1f,1.85f,1.45f));
                material.EnableKeyword("_EMISSION");
            });
            AssetDatabase.SaveAssets();

            void Save(string name,System.Action<Material> configure)
            {
                string path="Assets/Resources/ELTS/"+name+".mat";
                var material=AssetDatabase.LoadAssetAtPath<Material>(path);
                if(material==null)
                {
                    material=new Material(source){name=name};
                    AssetDatabase.CreateAsset(material,path);
                }
                else material.CopyPropertiesFromMaterial(source);
                material.enableInstancing=true;configure(material);EditorUtility.SetDirty(material);
            }
        }
    }
}
