using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Elts.Editor
{
    public static class BuildEntry
    {
        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing " + name);
            return args[index + 1];
        }

        public static void BuildWindows()
        {
            if (Application.unityVersion != Argument("-eltsEditorVersion"))
                throw new InvalidOperationException("Pinned Editor version mismatch.");
            string output = Path.GetFullPath(Argument("-eltsBuildOutput"));
            if (Directory.EnumerateFiles(output, "*.exe").Any())
                throw new InvalidOperationException("Build output must not contain a previous player.");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            string previousVersion = PlayerSettings.bundleVersion;
            PlayerSettings.bundleVersion = Argument("-eltsBuildVersion");
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new InvalidOperationException("No enabled build scenes.");
            BuildReport report;
            try { report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = scenes,
                locationPathName = Path.Combine(output, "ELTS-Synthetic.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.StrictMode
            }); }
            finally { PlayerSettings.bundleVersion = previousVersion; AssetDatabase.SaveAssets(); }
            if (report.summary.result != BuildResult.Succeeded || report.summary.totalErrors != 0)
                throw new InvalidOperationException("Windows build failed: " + report.summary.result);
            Debug.Log("ELTS_WINDOWS_BUILD_PASS " + report.summary.totalSize);
        }
    }
}
