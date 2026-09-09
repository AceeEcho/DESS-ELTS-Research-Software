using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Elts.Operator.Editor
{
    /// <summary>Build the explicit desktop entry scene through Unity's serializer.</summary>
    public static class DesktopSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/ELTSDesktop.unity";

        [MenuItem("ELTS/Create desktop pipeline scene")]
        public static void Create()
        {
            // Never overwrite an administrator's later scene edits.
            if (File.Exists(ScenePath))
            { Debug.Log("ELTS_DESKTOP_SCENE_EXISTS " + ScenePath); return; }
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var host=new GameObject("ELTS desktop pipeline");
            host.AddComponent<DevelopmentView>();
            host.AddComponent<DevelopmentSessionPanel>();
            if(!EditorSceneManager.SaveScene(scene,ScenePath))
                throw new IOException("Could not save the desktop entry scene.");
            EditorBuildSettings.scenes=new[] { new EditorBuildSettingsScene(ScenePath,true),
                new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity",false) };
            AssetDatabase.SaveAssets();
            Debug.Log("ELTS_DESKTOP_SCENE_CREATED " + ScenePath);
        }
    }
}
