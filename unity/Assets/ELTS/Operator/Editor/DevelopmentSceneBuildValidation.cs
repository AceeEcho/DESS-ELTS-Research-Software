using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace Elts.Operator.Editor
{
    /// <summary>Reject stale Inspector settings before producing an unusable player.</summary>
    public sealed class DevelopmentSceneBuildValidation : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return; // This callback also runs when entering Play mode.
            foreach (var root in scene.GetRootGameObjects())
            foreach (var view in root.GetComponentsInChildren<DevelopmentView>(true))
            {
                var serialized = new SerializedObject(view);
                var settings = (DevelopmentGameSettings)serialized.FindProperty("gameSettings").boxedValue;
                try { settings.Validate(); }
                catch (ArgumentException error)
                {
                    throw new BuildFailedException(scene.path + " / " + view.name + ": " + error.Message
                        + " Update the saved DevelopmentView game settings before rebuilding.");
                }
            }
        }
    }
}
