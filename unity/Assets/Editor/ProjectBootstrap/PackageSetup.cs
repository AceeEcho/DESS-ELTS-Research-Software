using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Elts.Editor
{
    /// <summary>
    /// Asynchronous UPM operations. Invoke without -quit: the update callback
    /// exits only when the package manager reports completion or timeout.
    /// </summary>
    public static class PackageSetup
    {
        private const double TimeoutSeconds = 600;
        private static ListRequest listing;
        private static AddAndRemoveRequest changing;
        private static double deadline;

        [Serializable] private sealed class PackagePin { public string id; public string version; public string source; }
        [Serializable] private sealed class PackagePins { public int schemaVersion; public string unityVersion; public PackagePin[] required; }

        public static void InstallRequired()
        {
            string config = Path.GetFullPath(Path.Combine(Application.dataPath, "../../config/unity-packages.json"));
            var pins = JsonUtility.FromJson<PackagePins>(File.ReadAllText(config));
            if (pins == null || pins.schemaVersion != 1 || pins.unityVersion != Application.unityVersion
                || pins.required == null || pins.required.Length != 3 || pins.required.Select(p => p.id).Distinct().Count() != 3)
                throw new InvalidOperationException("Validate the canonical Unity package configuration before installing.");
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            changing = Client.AddAndRemove(pins.required.Select(p => p.id + "@" + p.version).ToArray(), Array.Empty<string>());
            EditorApplication.update += PollRequired;
        }

        private static void PollRequired()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "UPM required-package installation timed out"); return; }
            if (changing == null || !changing.IsCompleted) return;
            if (changing.Status != StatusCode.Success) { Finish(1, changing.Error.message); return; }
            Finish(0, "Required packages resolved; run the offline pin audit and a fresh import");
        }

        public static void RemoveXr()
        {
            deadline = EditorApplication.timeSinceStartup + TimeoutSeconds;
            listing = Client.List(offlineMode: true, includeIndirectDependencies: true);
            EditorApplication.update += PollRemoval;
        }

        private static bool Forbidden(string name)
        {
            return name.StartsWith("com.unity.xr.", StringComparison.Ordinal)
                || name == "com.unity.modules.vr" || name == "com.unity.modules.xr";
        }

        private static void PollRemoval()
        {
            if (EditorApplication.timeSinceStartup > deadline) { Finish(2, "UPM removal timed out"); return; }
            if (listing != null)
            {
                if (!listing.IsCompleted) return;
                if (listing.Status != StatusCode.Success) { Finish(1, listing.Error.message); return; }
                string[] remove = listing.Result.Where(p => Forbidden(p.name)).Select(p => p.name).ToArray();
                listing = null;
                if (remove.Length == 0) { Finish(0, "No XR packages present"); return; }
                Debug.Log("ELTS_UPM_REMOVE " + string.Join(",", remove));
                changing = Client.AddAndRemove(Array.Empty<string>(), remove);
                return;
            }
            if (changing == null || !changing.IsCompleted) return;
            if (changing.Status != StatusCode.Success) { Finish(1, changing.Error.message); return; }
            if (changing.Result.Any(p => Forbidden(p.name))) { Finish(1, "XR packages remain after resolution"); return; }
            Finish(0, "XR packages absent after UPM resolution");
        }

        private static void Finish(int code, string message)
        {
            EditorApplication.update -= PollRemoval;
            EditorApplication.update -= PollRequired;
            Debug.Log("ELTS_UPM_RESULT " + code + " " + message);
            EditorApplication.Exit(code);
        }
    }
}
