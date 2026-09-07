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
            Debug.Log("ELTS_UPM_RESULT " + code + " " + message);
            EditorApplication.Exit(code);
        }
    }
}
