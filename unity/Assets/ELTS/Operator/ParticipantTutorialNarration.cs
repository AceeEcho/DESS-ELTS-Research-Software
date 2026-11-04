#nullable enable
using System;
using System.Collections.Generic;
using Elts.Session;
using Newtonsoft.Json;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>One narrator, owned by the participant session. Audio never
    /// accepts a check, starts practice, or requests a device demonstration.</summary>
    public sealed class ParticipantTutorialNarration : IDisposable
    {
        private readonly AudioSource source;
        private readonly double gapSeconds;
        private readonly Dictionary<string,AudioClip> clips=new Dictionary<string,AudioClip>();
        private ParticipantTutorial? owner;
        private int presentation=-1;
        private bool paused;
        public int ClipCount=>clips.Count;
        public AudioClip? CurrentClip=>source.clip;
        public bool Paused=>paused;

        public ParticipantTutorialNarration(GameObject host,float volume,double gapSeconds=.25)
        {
            this.gapSeconds=gapSeconds;
            var manifest=Resources.Load<TextAsset>("ELTS/ParticipantTutorialAudio");
            var paths=JsonConvert.DeserializeObject<Dictionary<string,string>>(manifest.text)!;
            foreach(var entry in paths)
            {
                var clip=Resources.Load<AudioClip>(entry.Value);
                if(clip==null || clip.length<=0)throw new InvalidOperationException("Missing selected narration: "+entry.Key);
                clips.Add(entry.Key,clip);
            }
            source=host.AddComponent<AudioSource>();source.playOnAwake=false;
            source.spatialBlend=0;source.loop=false;source.volume=volume;
        }
        public double Duration(TutorialStep step,double fallback)
            =>clips.TryGetValue(step.Script,out var clip)?clip.length+gapSeconds:fallback;
        public bool HasClip(TutorialStep step)=>clips.ContainsKey(step.Script);

        // Transition recordings use neutral wording that also applies after
        // shortened scenarios and skipped breaks. All paths select the same cue.
        private AudioClip? SelectClip(ParticipantTutorial flow)
        {
            return clips.TryGetValue(flow.Step.Script,out var clip)?clip:null;
        }
        public bool HasClip(ParticipantTutorial flow)=>SelectClip(flow)!=null;
        public double Duration(ParticipantTutorial flow,double fallback)
        {
            var clip=SelectClip(flow);
            return clip!=null?clip.length+gapSeconds:fallback;
        }

        public void Refresh(ParticipantTutorial flow)
        {
            if(owner!=flow || presentation!=flow.PresentationVersion)
            {
                var clip=SelectClip(flow);
                // Replace the previous source atomically, including on an
                // operator skip. A stopped or paused old cue can never resume.
                source.Stop();source.clip=clip;
                if(clip!=null)source.Play();
                paused=false;
                owner=flow;presentation=flow.PresentationVersion;
            }
            if(flow.Paused!=paused)
            {
                if(flow.Paused)source.Pause();else source.UnPause();
                paused=flow.Paused;
            }
        }
        public void Stop(){source.Stop();source.clip=null;owner=null;presentation=-1;paused=false;}
        public void Dispose(){Stop();UnityEngine.Object.Destroy(source);}
    }
}
