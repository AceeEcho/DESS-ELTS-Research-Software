using System;
using System.IO;
using Elts.Config;
using UnityEngine;

namespace Elts.Development
{
    /// <summary>Explicit batch-player diagnostic; never runs during normal startup.</summary>
    public sealed class DevelopmentSmokeProbe : MonoBehaviour
    {
        private const int RequiredFrames = 3;
        private int frames;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-eltsSmokeTest") < 0) return;
            var probe = new GameObject("ELTS explicit development startup probe");
            DontDestroyOnLoad(probe);
            probe.AddComponent<DevelopmentSmokeProbe>();
        }

        private void Update()
        {
            if (++frames != RequiredFrames) return;
            try
            {
                var config = DevelopmentConfiguration.LoadDirectory(Path.Combine(Application.streamingAssetsPath, "config-generated"));
                if (config.Mode != "synthetic" || config.StudyReady) throw new InvalidOperationException("Development configuration boundary violated");
                bool denied = false;
                try { config.RequireStudyReadiness(); }
                catch (InvalidOperationException) { denied = true; }
                if (!denied) throw new InvalidOperationException("Study authorization unexpectedly succeeded");
                Debug.Log("ELTS_PLAYER_SMOKE_PASS " + Application.version + " frames=" + frames);
                Application.Quit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("ELTS_PLAYER_SMOKE_FAIL " + error.Message);
                Application.Quit(1);
            }
        }
    }
}
