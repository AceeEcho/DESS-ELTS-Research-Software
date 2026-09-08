using System;
using System.IO;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Opt-in standalone graphical probe. Its screenshot is a development artifact, never study evidence.</summary>
    public sealed class DevelopmentViewProbe : MonoBehaviour
    {
        private const int CaptureFrame = 45, MaximumFrames = 900;
        private int frames;
        private string output;
        private bool requested;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateIfRequested()
        {
            string[] args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-eltsViewScreenshot");
            if(index<0)return;
            if(index+1>=args.Length){Debug.LogError("Missing screenshot output argument.");Application.Quit(2);return;}
            var probe=new GameObject("ELTS explicit graphical probe").AddComponent<DevelopmentViewProbe>();probe.output=Path.GetFullPath(args[index+1]);
        }
        private void Update()
        {
            frames++;
            if(frames==CaptureFrame)
            {
                var view=FindFirstObjectByType<DevelopmentView>();
                if(view==null||!view.Ready){Debug.LogError("ELTS_VIEW_PROBE_FAIL view not ready");Application.Quit(2);return;}
                view.SetAnimation(false);view.SetPrediction(0.02);view.SetLayoutOffsets(0.1,0.05);view.SetDropout(true,false);view.Refresh(0);
                if(view.ParticipantCamera.enabled){Debug.LogError("ELTS_VIEW_PROBE_FAIL dropout camera remained active");Application.Quit(2);return;}
                view.SetDropout(false,false);view.SetLayoutOffsets(0,0);view.Refresh(0);
                Directory.CreateDirectory(Path.GetDirectoryName(output));ScreenCapture.CaptureScreenshot(output);requested=true;
            }
            if(requested&&frames>CaptureFrame+5&&File.Exists(output)&&new FileInfo(output).Length>0)
            {Debug.Log("ELTS_VIEW_PROBE_PASS frames="+frames+" screenshot="+output);Application.Quit(0);}
            if(frames>MaximumFrames){Debug.LogError("ELTS_VIEW_PROBE_FAIL screenshot timeout");Application.Quit(2);}
        }
    }
}
