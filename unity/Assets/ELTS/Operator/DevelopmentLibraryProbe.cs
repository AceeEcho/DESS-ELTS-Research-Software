using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Elts.Geometry;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Opt-in standalone comparison of the empty library at two repeatable eye positions.</summary>
    public sealed class DevelopmentLibraryProbe : MonoBehaviour
    {
        private string directory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-eltsLibraryProbe");
            if (index < 0) return;
            if (index + 1 >= args.Length)
            {
                Debug.LogError("Missing library probe output directory.");
                Application.Quit(2);
                return;
            }
            var host = new GameObject("Library rendering probe");
            DontDestroyOnLoad(host);
            host.AddComponent<DevelopmentLibraryProbe>().directory = Path.GetFullPath(args[index + 1]);
        }

        private IEnumerator Start()
        {
            while (SceneManager.GetActiveScene().name != "ELTSDesktop") yield return null;
            var view = FindFirstObjectByType<DevelopmentView>();
            if (view == null || !view.Ready)
            {
                Debug.LogError("Library probe could not initialize the participant view.");
                Application.Quit(2);
                yield break;
            }
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            double readyMs = (DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalMilliseconds;
            while (TexasTechIntro.IsShowing) yield return null;

            // Measure only the participant room at steady state. Stop periodic preview captures
            // and session input before fixing the camera for both samples.
            var station = FindFirstObjectByType<DevelopmentTestStation>();
            var panel = FindFirstObjectByType<DevelopmentSessionPanel>();
            foreach (var behaviour in new MonoBehaviour[] { station, panel, view })
            {
                if (behaviour == null) continue;
                behaviour.StopAllCoroutines();
                behaviour.enabled = false;
            }
            foreach (var document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None)) document.enabled = false;
            view.SetTutorialPresentation(false, "", "");
            view.ParticipantOnly = true;
            var desktop = new DesktopInputModel(view.Configuration);
            var empty = new Dictionary<string, Vector3d>();
            view.SetSessionFrame(desktop.Head, desktop.Weapon, empty);
            view.Refresh(0);
            view.OperatorCamera.enabled = false;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var camera = view.ParticipantCamera;
            camera.enabled = false;
            try
            {
                var samples = MeasureRendering(camera);
                Directory.CreateDirectory(directory);
                Capture(camera, Path.Combine(directory, "centered.png"));
                var screen = view.DisplayPlane;
                var head = new RigidPose(desktop.Head.Position + screen.U * .4 + screen.V * .15, desktop.Head.Orientation);
                view.SetSessionFrame(head, desktop.Weapon, empty);
                view.Refresh(0);
                Capture(camera, Path.Combine(directory, "shifted.png"));
                var room = view.transform.Find("Library interior (virtual metres)");
                int renderers = room.GetComponentsInChildren<Renderer>().Length;
                Debug.Log(FormattableString.Invariant($"ELTS_LIBRARY_PROBE_PASS readyMs={readyMs:F1} medianRenderMs={samples[89]:F2} p95RenderMs={samples[170]:F2} renderers={renderers} resolution=1280x720"));
                Application.Quit(0);
            }
            catch (Exception error)
            {
                Debug.LogError("ELTS_LIBRARY_PROBE_FAIL " + error);
                Application.Quit(2);
            }
        }

        private static double[] MeasureRendering(Camera camera)
        {
            // Windows can skip rendering hidden windows. Explicitly render and read one pixel
            // to include GPU completion in each sample, even with no visible swap chain.
            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
            var request = new RenderPipeline.StandardRequest { destination = target };
            var samples = new double[180];
            try
            {
                for (int i = -30; i < samples.Length; i++)
                {
                    double start = Time.realtimeSinceStartupAsDouble;
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    pixel.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false);
                    if (i >= 0) samples[i] = (Time.realtimeSinceStartupAsDouble - start) * 1000;
                }
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(pixel);
            }
            Array.Sort(samples);
            return samples;
        }

        private static void Capture(Camera camera, string path)
        {
            var target = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Destroy(image);
            }
        }
    }
}
