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
        private TutorialStage stage;
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

        // Playback, timing and dashboard status must use the same decision.
        // Normal-schedule wording would be misleading after an exception.
        private AudioClip? SelectClip(ParticipantTutorial flow,bool twoMinuteBreak)
        {
            if ((!twoMinuteBreak && (flow.Stage==TutorialStage.BreakOverview ||
                flow.Stage==TutorialStage.BreakRunning || flow.Stage==TutorialStage.FinalBreak)) ||
                (flow.Stage==TutorialStage.ScenarioEnded && flow.LastAttemptAbnormal) ||
                (flow.Stage==TutorialStage.FinalEnded && !flow.AllScenariosCompleted) ||
                ((flow.Stage==TutorialStage.Form || flow.Stage==TutorialStage.NextReady) && flow.LastBreakSkipped))
                return null;
            return clips.TryGetValue(flow.Step.Script,out var clip)?clip:null;
        }
        public bool HasClip(ParticipantTutorial flow,bool twoMinuteBreak)=>SelectClip(flow,twoMinuteBreak)!=null;
        public double Duration(ParticipantTutorial flow,double fallback,bool twoMinuteBreak)
        {
            var clip=SelectClip(flow,twoMinuteBreak);
            return clip!=null?clip.length+gapSeconds:fallback;
        }

        public void Refresh(ParticipantTutorial flow, bool twoMinuteBreak = true)
        {
            if(owner!=flow || presentation!=flow.PresentationVersion)
            {
                var clip=SelectClip(flow,twoMinuteBreak);
                // Part 4.2 covers both actions. Moving from firing to reload
                // must neither restart nor cut off that shared recording.
                bool shared=owner==flow && stage==TutorialStage.Fire && flow.Stage==TutorialStage.Reload && source.clip==clip;
                if(!shared)
                {
                    source.Stop();source.clip=clip;
                    if(clip!=null)source.Play();
                    paused=false;
                }
                owner=flow;stage=flow.Stage;presentation=flow.PresentationVersion;
            }
            // An operator can change the break length while its cue is playing.
            // Stop obsolete wording immediately without restarting narration.
            if(source.clip!=null && SelectClip(flow,twoMinuteBreak)==null)
            { source.Stop();source.clip=null; }
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
