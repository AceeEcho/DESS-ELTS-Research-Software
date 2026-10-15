using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Elts.Operator.Editor
{
    /// <summary>Build or update the explicit desktop entry scene through Unity's serializer.</summary>
    public static class DesktopSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/ELTSDesktop.unity";

        [MenuItem("ELTS/Reset saved library pose offsets")]
        public static void ResetLibraryPoseOffsets()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var view = UnityEngine.Object.FindFirstObjectByType<DevelopmentView>();
            if (view == null) throw new System.InvalidOperationException("Desktop scene has no DevelopmentView.");
            var defaults = new DevelopmentGameSettings();
            var serialized = new SerializedObject(view);
            var settings = serialized.FindProperty("gameSettings");
            // Older scenes retain the larger v2 offsets even after C# defaults change.
            // Update only the three offsets whose clearance limits changed in v3.
            settings.FindPropertyRelative("fixedLateralJitterM").floatValue = defaults.fixedLateralJitterM;
            settings.FindPropertyRelative("fixedDepthJitterM").floatValue = defaults.fixedDepthJitterM;
            settings.FindPropertyRelative("fixedSidePeekOffsetM").floatValue = defaults.fixedSidePeekOffsetM;
            serialized.ApplyModifiedProperties();
            ((DevelopmentGameSettings)settings.boxedValue).Validate();
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save library pose offsets.");
            Debug.Log("ELTS_LIBRARY_POSE_SETTINGS_SAVED");
        }

        [MenuItem("ELTS/Create or update desktop environment")]
        public static void CreateOrUpdate()
        {
            var scene = File.Exists(ScenePath)
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var host = GameObject.Find("ELTS desktop pipeline");
            if (host == null) host = new GameObject("ELTS desktop pipeline");
            if (host.GetComponent<DevelopmentView>() == null) host.AddComponent<DevelopmentView>();
            if (host.GetComponent<DevelopmentSessionPanel>() == null) host.AddComponent<DevelopmentSessionPanel>();

            // This affects only our named child hierarchy. Existing cameras, lighting,
            // and administrator-authored objects outside it remain intact.
            DevelopmentEnvironmentAuthoring.CreateOrUpdate(host.transform);
            if(!EditorSceneManager.SaveScene(scene,ScenePath))
                throw new IOException("Could not save the desktop entry scene.");
            // Keep any build scenes an administrator already configured. This command
            // only guarantees that the explicit desktop entry is available.
            var buildScenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            var found = false;
            for (var index = 0; index < buildScenes.Count; index++)
            {
                if (buildScenes[index].path != ScenePath) continue;
                buildScenes[index] = new EditorBuildSettingsScene(ScenePath, true);
                found = true;
            }
            if (!found) buildScenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = buildScenes.ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("ELTS_DESKTOP_SCENE_AUTHORED " + ScenePath);
        }

        // Retain the original batch-mode entry point used by existing automation.
        public static void Create() => CreateOrUpdate();
    }
}
