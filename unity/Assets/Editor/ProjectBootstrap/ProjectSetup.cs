using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace Elts.Editor
{
    /// <summary>Repeatable Editor-owned project settings. Never edits Unity YAML directly.</summary>
    public static class ProjectSetup
    {
        private const string SelectedEditor = "6000.3.23f1";

        [Serializable]
        private sealed class ImportReport
        {
            public string unityVersion;
            public string target;
            public string renderPipeline;
            public string[] eltsAssemblies;
            public bool syntheticOnly = true;
            public string limitation = "Project import only; no Unity tests, build, physical or gate acceptance.";
        }

        public static void Configure()
        {
            if (Application.unityVersion != SelectedEditor)
                throw new InvalidOperationException("The exact selected Unity Editor is required.");
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.companyName = "ELTS Research";
            PlayerSettings.productName = "ELTS-Synthetic";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows x64 target selection failed.");
            var pipeline = GraphicsSettings.defaultRenderPipeline;
            if (pipeline == null || !pipeline.GetType().Name.Contains("Universal"))
                throw new InvalidOperationException("The URP template render pipeline is missing.");
            AssetDatabase.SaveAssets();
            var report = new ImportReport {
                unityVersion = Application.unityVersion,
                target = EditorUserBuildSettings.activeBuildTarget.ToString(),
                renderPipeline = pipeline.GetType().FullName,
                eltsAssemblies = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name)
                    .Where(name => name.StartsWith("Elts.", StringComparison.Ordinal)).OrderBy(name => name).ToArray()
            };
            string diagnostics = Path.GetFullPath(Path.Combine(Application.dataPath, "../../diagnostics"));
            Directory.CreateDirectory(diagnostics);
            File.WriteAllText(Path.Combine(diagnostics, "unity-import-report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("ELTS_IMPORT_PASS " + Application.unityVersion + " " + report.target);
        }
    }
}
