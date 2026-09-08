using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

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
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    CaptureCameraPreviews(view,output);requested=true;
                }
                catch(Exception error){Debug.LogError("ELTS_VIEW_PROBE_FAIL "+error);Application.Quit(2);}
            }
            if(requested&&frames>CaptureFrame+5&&File.Exists(output)&&new FileInfo(output).Length>0)
            {Debug.Log("ELTS_VIEW_PROBE_PASS frames="+frames+" screenshot="+output);Application.Quit(0);}
            if(frames>MaximumFrames){Debug.LogError("ELTS_VIEW_PROBE_FAIL screenshot timeout");Application.Quit(2);}
        }
        private static void CaptureCameraPreviews(DevelopmentView view,string path)
        {
            // Hidden windows need not have a readable swap-chain backbuffer. Render both
            // real cameras to explicit targets instead. This captures geometry, not IMGUI.
            const int side=640;
            var image=new Texture2D(side*2,side,TextureFormat.RGB24,false);
            var previous=RenderTexture.active;
            try
            {
                var cameras=new[]{view.ParticipantCamera,view.OperatorCamera};
                for(int index=0;index<cameras.Length;index++)
                {
                    var camera=cameras[index];var originalRect=camera.rect;var projection=camera.projectionMatrix;
                    int height=index==0?Math.Max(1,(int)Math.Round(side*view.DisplayPlane.Height/view.DisplayPlane.Width)):side;
                    var target=RenderTexture.GetTemporary(side,height,24,RenderTextureFormat.ARGB32);
                    try
                    {
                        camera.rect=new Rect(0,0,1,1);
                        var request=new RenderPipeline.StandardRequest{destination=target};
                        if(!RenderPipeline.SupportsRenderRequest(camera,request))throw new InvalidOperationException("Active pipeline cannot render the requested camera preview.");
                        RenderPipeline.SubmitRenderRequest(camera,request);
                        RenderTexture.active=target;image.ReadPixels(new Rect(0,0,side,height),index*side,(side-height)/2,false);
                    }
                    finally{camera.rect=originalRect;camera.projectionMatrix=projection;RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);}
                }
                image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally{RenderTexture.active=previous;Destroy(image);}
        }
    }
}
