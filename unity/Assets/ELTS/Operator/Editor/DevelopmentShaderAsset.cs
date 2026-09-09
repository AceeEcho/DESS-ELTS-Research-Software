using System.IO;
using UnityEditor;
using UnityEngine;

namespace Elts.Operator.Editor
{
    /// <summary>Keep the development material in Resources so shader stripping cannot remove its URP shader.</summary>
    internal static class DevelopmentShaderAsset
    {
        private const string DirectoryPath = "Assets/ELTS/Operator/Resources/ELTS";
        private const string AssetPath = DirectoryPath + "/DevelopmentUnlit.mat";
        [InitializeOnLoadMethod]
        private static void EnsureAsset()
        {
            if (File.Exists(AssetPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(AssetPath)) return;
                Directory.CreateDirectory(DirectoryPath);
                AssetDatabase.Refresh();
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new System.InvalidOperationException("Pinned URP Unlit shader missing.");
                AssetDatabase.CreateAsset(new Material(shader), AssetPath);
                AssetDatabase.SaveAssets();
            };
        }
    }
}
