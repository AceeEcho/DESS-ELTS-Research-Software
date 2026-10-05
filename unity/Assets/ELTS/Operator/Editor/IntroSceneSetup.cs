using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Elts.Operator.Editor
{
    public static class IntroSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/TexasTechIntro.unity";

        [MenuItem("ELTS/Create Texas Tech startup intro")]
        public static void Create()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Intro background camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.98f, .98f, .97f);
            camera.cullingMask = 0;
            new GameObject("Texas Tech startup intro").AddComponent<TexasTechIntro>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var existing in EditorBuildSettings.scenes)
                if (existing.path != ScenePath) scenes.Add(existing);
            EditorBuildSettings.scenes = scenes.ToArray();
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            foreach (string asset in new[] { "TexasTechDoubleT", "WhitacreSignature" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/ELTS/Operator/Resources/ELTS/Branding/" + asset + ".png");
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                // Preserve the source aspect ratio instead of rounding each dimension to a power of two.
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ELTS_TEXAS_TECH_INTRO_READY");
        }
    }
}
