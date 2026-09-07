using System;
using UnityEditor;
using UnityEngine;

namespace Elts.Editor
{
    public static class OpenVrImporter
    {
        private const string NativePath = "Assets/ThirdParty/OpenVR/Plugins/x86_64/openvr_api.dll";

        public static void Configure()
        {
            var importer = AssetImporter.GetAtPath(NativePath) as PluginImporter;
            if (importer == null) throw new InvalidOperationException("The pinned OpenVR native asset is missing.");
            importer.SetCompatibleWithAnyPlatform(false);
            // Unity's x86_64 folder convention initially enables all desktop
            // platforms. This DLL is a Windows PE image, so clear the others.
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", "Windows");
            importer.SetEditorData("CPU", "x86_64");
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            importer.isPreloaded = false;
            importer.SaveAndReimport();
            if (importer.GetCompatibleWithAnyPlatform() || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneLinux64)
                || importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX) || importer.isPreloaded)
                throw new InvalidOperationException("OpenVR native import platform boundary is not enforced.");
            Debug.Log("ELTS_OPENVR_IMPORT_PASS Windows x86_64 only; preloading disabled; native initialization not called.");
        }
    }
}
