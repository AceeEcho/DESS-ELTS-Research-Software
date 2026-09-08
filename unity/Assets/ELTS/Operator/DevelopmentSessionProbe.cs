using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Opt-in UI render probe. Does not start a session or manipulate devices.</summary>
    public sealed class DevelopmentSessionProbe : MonoBehaviour
    {
        private const int CaptureWidth = 1400, CaptureHeight = 360;
        private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var args=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(args,"-eltsSessionScreenshot");
            if(index<0 || index+1>=args.Length) return;
            new GameObject("Session UI render probe").AddComponent<DevelopmentSessionProbe>().output=args[index+1];
        }
        private IEnumerator Start()
        {
            yield return null;
            var panel=FindFirstObjectByType<DevelopmentSessionPanel>();
            if(panel==null || String.IsNullOrEmpty(output) || !Path.IsPathRooted(output) || File.Exists(output))
            { Debug.LogError("ELTS_SESSION_PROBE_FAIL invalid panel or output");Application.Quit(2);yield break; }
            var target=new RenderTexture(CaptureWidth,CaptureHeight,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            target.Create();panel.SetDiagnosticRenderTarget(target);
            // UI Toolkit renders its target panel during the player loop.
            for(int index=0;index<5;index++) yield return null;
            var previous=RenderTexture.active;
            RenderTexture.active=target;
            var image=new Texture2D(CaptureWidth,CaptureHeight,TextureFormat.RGBA32,false);
            image.ReadPixels(new Rect(0,0,CaptureWidth,CaptureHeight),0,0);image.Apply();
            RenderTexture.active=previous;
            byte[] png=image.EncodeToPNG();
            var write=Task.Run(()=> { using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write)) stream.Write(png,0,png.Length); });
            while(!write.IsCompleted) yield return null;
            Destroy(image);target.Release();Destroy(target);
            if(write.IsFaulted) { Debug.LogError("ELTS_SESSION_PROBE_FAIL "+write.Exception);Application.Quit(2);yield break; }
            Debug.Log("ELTS_SESSION_PROBE_PASS screenshot="+output);Application.Quit(0);
        }
    }
}
