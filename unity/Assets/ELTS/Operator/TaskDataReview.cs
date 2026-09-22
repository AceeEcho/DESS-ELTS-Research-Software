#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// <summary>Rebuildable descriptive metrics, never primary scoring. All readers
    /// (dashboard, SQL and Excel) consume this same versioned calculation.</summary>
    internal static class TaskDataReview
    {
        public const string Version="elts.task-metrics.v1";
        public static readonly string[] SummaryColumns={"blockId","condition","status","activeSeconds","hits","shots","misses","headHits","bodyHits","limbHits","coverHits","targetKills","damageDealt","magazineCapacity","reloads","dryFires","hitsPerSecond","shotsPerSecond","missesPerSecond","accuracyPercent","meanShotErrorDeg","p95ShotErrorDeg","meanShotOffsetMm","meanMissOffsetMm","aimErrorVarianceDeg2","meanAimErrorDeg","meanAimSpeedDegPerSecond","aimSpeedVariabilityDegPerSecond","aimPathDeg","aimObservedSeconds","aimCoveragePercent","aimObservations","validAimObservations","shotGeometryCount","reason","metricsVersion","scoreStatus"};
        public static readonly string[] SecondColumns={"second","exposureSeconds","hits","shots","misses","coverHits","hitsPerSecond","shotsPerSecond","missesPerSecond","meanAimErrorDeg"};
        public static readonly string[] ShotColumns={"shotNumber","activeSeconds","monotonicTicks","outcome","magazineBefore","magazineAfter","targetId","hitRegion","damage","remainingHealth","coverId","referenceTargetId","referencePolicy","angularErrorDeg","centerOffsetMm","edgeClearanceMm","targetRadiusMm"};
        public static readonly string[][] Definitions={
            new[]{"hitsPerSecond / shotsPerSecond / missesPerSecond","1/s","Accepted count / active seconds. Pauses excluded; partial final bins use actual exposure."},
            new[]{"hits / shots / misses","count","Hits are region hits; misses are shots that hit neither target nor cover. Cover impacts are separate. Trigger lockout and invalid tracking are not accepted shots. Old completed recordings use matching TargetHit events."},
            new[]{"headHits / bodyHits / limbHits","count","Confirmed hits by visible humanoid region. Older hits without a region leave these totals unavailable."},
            new[]{"coverHits / targetKills / damageDealt","count","Cover impacts, health-zero target removals, and summed hit damage in the synthetic scenario."},
            new[]{"reloads / dryFires","count","Manual magazine reloads and empty-magazine trigger attempts. Neither counts as a scored shot."},
            new[]{"magazineBefore / magazineAfter / magazineCapacity","rounds","Recorded ammunition state around each accepted shot and the configured magazine size for that block."},
            new[]{"accuracyPercent","%","100 × hit shots / accepted shots; unavailable with no shots."},
            new[]{"meanShotErrorDeg / p95ShotErrorDeg","deg","Angle between corrected bore and reference target center at shot time. P95 uses nearest rank."},
            new[]{"centerOffsetMm / meanShotOffsetMm / meanMissOffsetMm","mm","Perpendicular target-center distance to the forward bore ray. Miss mean includes only misses with geometry; this is not a screen-plane impact distance."},
            new[]{"edgeClearanceMm","mm","Maximum of zero and center offset minus target radius."},
            new[]{"referencePolicy","policy","Hit target for hits; nearest angular visible forward target for misses/aim observations. Target choice does not identify participant intent."},
            new[]{"aimErrorVarianceDeg2","deg²","Sample variance of target angular error across valid frame-sampled observations (n−1). Includes target switching; not raw weapon position variance."},
            new[]{"meanAimSpeedDegPerSecond","deg/s","Total bore angular travel / accepted observation-pair duration. Never bridges pause, invalid data, duplicate observations or configured maximum gaps."},
            new[]{"aimSpeedVariabilityDegPerSecond","deg/s","Duration-weighted population standard deviation of successive bore angular speeds. Descriptive erraticness proxy; includes deliberate target transitions."},
            new[]{"aimPathDeg","deg","Total angular travel over accepted observation pairs; unavailable without a valid pair."},
            new[]{"aimCoveragePercent / aimObservedSeconds","% / s","Accepted observation-pair time / active task time. Low coverage limits motion statistics. Frame cadence differs from full raw tracker cadence."},
            new[]{"meanAimErrorDeg","deg","Arithmetic mean target-center angular error over valid observations with a visible forward target."},
            new[]{"shotGeometryCount / validAimObservations / aimObservations","count","Coverage counts. Old recordings can lack geometry; null/blank values are unavailable, never substituted with zero."},
            new[]{"metricsVersion / scoreStatus","provenance","elts.task-metrics.v1; descriptive synthetic review only, not integrity-verified analysis or a validated study score."}
        };

        public static JObject Read(string directory)
        {
            string summaryPath=Path.Combine(directory,"session-summary.json");
            foreach(string path in new[]{summaryPath,Path.Combine(directory,"events.ndjson")})
                if(File.Exists(path) && (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recording review cannot follow linked files.");
            var summary=File.Exists(summaryPath)?JObject.Parse(File.ReadAllText(summaryPath)):null;
            var result=Build(ReadCompleteEvents(Path.Combine(directory,"events.ndjson")));
            result["id"]=Path.GetFileName(directory);result["summary"]=summary;result["finalized"]=summary!=null;
            return result;
        }

        private static IEnumerable<JObject> ReadCompleteEvents(string path)
        {
            // Snapshot the byte boundary. Never read a trailing partial line, even
            // if it happens to be valid JSON before its terminating newline arrives.
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                long limit=input.Length;var bytes=new List<byte>();
                while(input.Position<limit)
                {
                    int next=input.ReadByte();if(next<0)break;
                    if(next!=10){bytes.Add((byte)next);if(bytes.Count>16777216)throw new IOException("Event exceeds 16 MB.");continue;}
                    yield return JObject.Parse(new System.Text.UTF8Encoding(false,true).GetString(bytes.ToArray()));bytes.Clear();
                }
            }
        }

        public static JObject Build(IEnumerable<JObject> events)
        {
            var attempts=new Dictionary<string,Attempt>();var notes=new JArray();
            foreach(var item in events)
            {
                string type=(string?)item["eventType"]??"";var p=item["payload"] as JObject??new JObject();
                string block=(string?)p["blockId"]??"";long ticks=(long?)item["monotonicTicks"]??0;
                if(type=="OperatorNote")notes.Add(new JObject{["ticks"]=ticks,["text"]=p["text"]?.DeepClone()});
                if(type=="BlockMetadata" || type=="BlockSkipped")
                {
                    if(!attempts.ContainsKey(block))attempts.Add(block,new Attempt(block,(string?)p["condition"]??""));
                    if(type=="BlockSkipped"){attempts[block].Result["status"]="Skipped";attempts[block].Result["reason"]=p["reason"]?.DeepClone();}
                }
                if(attempts.TryGetValue(block,out var attempt))attempt.Accept(type,ticks,p);
            }
            return new JObject{["metricsVersion"]=Version,["attempts"]=new JArray(attempts.Values.Select(a=>a.Finish())),["notes"]=notes,
                ["limitation"]="Descriptive synthetic review. Raw streams and checksums remain authoritative; no physical validation or study score."};
        }

        private sealed class Moments
        {
            public int Count;public double Mean,M2;
            public void Add(double value){Count++;double delta=value-Mean;Mean+=delta/Count;M2+=delta*(value-Mean);}
            public double? Average=>Count>0?Mean:(double?)null;
            public double? Variance=>Count>1?Math.Max(0,M2/(Count-1)):(double?)null;
        }
        private sealed class Attempt
        {
            public readonly JObject Result;
            private readonly JArray shots=new JArray();
            private readonly Dictionary<int,Moments> binAim=new Dictionary<int,Moments>();
            private readonly Moments aimError=new Moments();
            private long? segmentStart;private double accumulated,lastActive;
            private bool started,closed;
            private JObject? previousAim;
            private double path,observedTime,speedSquaredTime;
            private int observations,validObservations,motionPairs;
            private int targetKills,reloads,dryFires;
            public Attempt(string block,string condition)
            {Result=new JObject{["blockId"]=block,["condition"]=condition,["status"]="Started",["reason"]="",["metricsVersion"]=Version,["scoreStatus"]="unavailable"};}
            private double Active(long ticks)=>accumulated+(segmentStart.HasValue?Math.Max(0,(ticks-segmentStart.Value)/(double)TimeSpan.TicksPerSecond):0);
            public void Accept(string type,long ticks,JObject p)
            {
                if(type=="BlockStarted"){started=true;segmentStart=ticks;}
                if(type=="WeaponConfigured")Result["magazineCapacity"]=p["magazineCapacity"]?.DeepClone();
                double active=Active(ticks);
                if(started && !closed)lastActive=Math.Max(lastActive,active);
                if(type=="BlockPaused") {accumulated=Number(p,"activeSeconds")??active;segmentStart=null;previousAim=null;Result["status"]="Paused";}
                if(type=="BlockResumed"){segmentStart=ticks;previousAim=null;Result["status"]="Started";}
                if(type=="BlockEnded" || type=="BlockAborted")
                {
                    accumulated=Number(p,"activeSeconds")??active;lastActive=accumulated;segmentStart=null;closed=true;previousAim=null;
                    Result["status"]=type=="BlockAborted"?"Aborted":(string?)p["reason"]=="operator_stop"?"Stopped early":"Completed";
                    Result["reason"]=p["reason"]?.DeepClone();
                }
                if(type=="ShotFired" && segmentStart.HasValue && !closed)
                {
                    var shot=(JObject)p.DeepClone();shot["shotNumber"]=shots.Count+1;shot["activeSeconds"]=active;shot["monotonicTicks"]=ticks;
                    shots.Add(shot);
                }
                // Legacy shots had no explicit outcome. Match the event at the
                // exact trigger timestamp; never match a different shot or attempt.
                if(type=="TargetHit" && shots.Last is JObject last && (long?)last["monotonicTicks"]==ticks)
                {last["outcome"]="Hit";}
                if(type=="TargetDestroyed")targetKills++;
                if(type=="WeaponReloaded")reloads++;
                if(type=="WeaponDryFire")dryFires++;
                if(type=="AimObserved" && segmentStart.HasValue && !closed)Observe(active,p);
            }
            private void Observe(double active,JObject p)
            {
                observations++;
                if((bool?)p["valid"]!=true || !Number(p,"observationTicks").HasValue || !Number(p,"directionX").HasValue || !Number(p,"directionY").HasValue || !Number(p,"directionZ").HasValue)
                {previousAim=null;return;}
                validObservations++;
                var error=Number(p,"angularErrorDeg");
                if(error.HasValue)
                {
                    aimError.Add(error.Value);int bin=(int)Math.Floor(active);
                    if(!binAim.ContainsKey(bin))binAim[bin]=new Moments();binAim[bin].Add(error.Value);
                }
                if(previousAim!=null)
                {
                    double dt=((double)p["observationTicks"]!-(double)previousAim["observationTicks"]!)/TimeSpan.TicksPerSecond;
                    if(dt>0 && dt<=(Number(p,"maximumGapSeconds")??0))
                    {
                        double dot=0,normA=0,normB=0;
                        foreach(string axis in new[]{"directionX","directionY","directionZ"})
                        {double a=(double)p[axis]!,b=(double)previousAim[axis]!;dot+=a*b;normA+=a*a;normB+=b*b;}
                        if(normA>0 && normB>0)
                        {
                            double angle=Math.Acos(Math.Max(-1,Math.Min(1,dot/Math.Sqrt(normA*normB))))*180/Math.PI;
                            path+=angle;observedTime+=dt;speedSquaredTime+=angle*angle/dt;motionPairs++;
                        }
                    }
                }
                previousAim=p;
            }
            public JObject Finish()
            {
                bool skipped=(string?)Result["status"]=="Skipped";
                double duration=lastActive;
                // An unfinished legacy last shot may still be awaiting TargetHit.
                foreach(JObject shot in shots)if(shot["outcome"]==null)shot["outcome"]=closed?"Miss":"Unknown";
                int hits=shots.Count(s=>(string?)s["outcome"]=="Hit"),misses=shots.Count(s=>(string?)s["outcome"]=="Miss"),covers=shots.Count(s=>(string?)s["outcome"]=="Cover");
                bool outcomesKnown=hits+misses+covers==shots.Count;
                bool regionsKnown=shots.Where(s=>(string?)s["outcome"]=="Hit").All(s=>s["hitRegion"]!=null);
                foreach(string region in new[]{"head","body","limb"})
                    Result[region+"Hits"]=skipped || !regionsKnown?JValue.CreateNull():JToken.FromObject(shots.Count(s=>(string?)s["hitRegion"]==region));
                Result["coverHits"]=skipped?JValue.CreateNull():JToken.FromObject(covers);
                Result["targetKills"]=skipped?JValue.CreateNull():JToken.FromObject(targetKills);
                Result["damageDealt"]=skipped || !regionsKnown?JValue.CreateNull():JToken.FromObject(shots.Sum(s=>Number(s,"damage")??0));
                Result["reloads"]=skipped?JValue.CreateNull():JToken.FromObject(reloads);
                Result["dryFires"]=skipped?JValue.CreateNull():JToken.FromObject(dryFires);
                Result["activeSeconds"]=started?JToken.FromObject(duration):JValue.CreateNull();
                Result["shots"]=skipped?JValue.CreateNull():JToken.FromObject(shots.Count);
                Result["hits"]=skipped?JValue.CreateNull():JToken.FromObject(hits);
                Result["misses"]=skipped || !outcomesKnown?JValue.CreateNull():JToken.FromObject(misses);
                Put(Result,"hitsPerSecond",Rate(hits,duration));Put(Result,"shotsPerSecond",Rate(shots.Count,duration));
                Put(Result,"missesPerSecond",outcomesKnown?Rate(misses,duration):null);
                Put(Result,"accuracyPercent",outcomesKnown && shots.Count>0?100.0*hits/shots.Count:(double?)null);
                var errors=shots.Select(s=>Number(s,"angularErrorDeg")).Where(n=>n.HasValue).Select(n=>n!.Value).OrderBy(n=>n).ToArray();
                Put(Result,"meanShotErrorDeg",errors.Length>0?errors.Average():(double?)null);
                Put(Result,"p95ShotErrorDeg",errors.Length>0?errors[(int)Math.Ceiling(errors.Length*0.95)-1]:(double?)null);
                Put(Result,"meanShotOffsetMm",Average(shots.Select(s=>Number(s,"centerOffsetMm"))));
                Put(Result,"meanMissOffsetMm",Average(shots.Where(s=>(string?)s["outcome"]=="Miss").Select(s=>Number(s,"centerOffsetMm"))));
                Put(Result,"aimErrorVarianceDeg2",aimError.Variance);Put(Result,"meanAimErrorDeg",aimError.Average);
                Put(Result,"meanAimSpeedDegPerSecond",observedTime>0?path/observedTime:(double?)null);
                Put(Result,"aimSpeedVariabilityDegPerSecond",motionPairs>1?Math.Sqrt(Math.Max(0,speedSquaredTime/observedTime-Math.Pow(path/observedTime,2))):(double?)null);
                Put(Result,"aimPathDeg",motionPairs>0?path:(double?)null);
                Put(Result,"aimObservedSeconds",observations>0?observedTime:(double?)null);
                Put(Result,"aimCoveragePercent",observations>0 && duration>0?Math.Min(100,100*observedTime/duration):(double?)null);
                Result["aimObservations"]=observations;Result["validAimObservations"]=validObservations;Result["shotGeometryCount"]=errors.Length;
                var seconds=new JArray();
                // Half-open active-time bins include quiet seconds. A shot exactly
                // at the last known live timestamp waits for a later refresh.
                for(int i=0;i<Math.Ceiling(duration);i++)
                {
                    double exposure=Math.Min(1,duration-i);
                    var inBin=shots.Where(s=>(double)s["activeSeconds"]!>=i && (double)s["activeSeconds"]!<Math.Min(i+1,duration)).ToArray();
                    int h=inBin.Count(s=>(string?)s["outcome"]=="Hit"),m=inBin.Count(s=>(string?)s["outcome"]=="Miss"),c=inBin.Count(s=>(string?)s["outcome"]=="Cover");
                    var bin=new JObject{["second"]=i,["exposureSeconds"]=exposure,["shots"]=inBin.Length,["hits"]=h,["coverHits"]=c,["misses"]=h+m+c==inBin.Length?JToken.FromObject(m):JValue.CreateNull()};
                    Put(bin,"hitsPerSecond",Rate(h,exposure));Put(bin,"shotsPerSecond",Rate(inBin.Length,exposure));Put(bin,"missesPerSecond",h+m+c==inBin.Length?Rate(m,exposure):null);
                    Put(bin,"meanAimErrorDeg",binAim.TryGetValue(i,out var values)?values.Average:null);seconds.Add(bin);
                }
                Result["seconds"]=seconds;Result["shotDetails"]=shots;
                return Result;
            }
        }
        private static double? Rate(double count,double seconds)=>seconds>0?count/seconds:(double?)null;
        private static double? Number(JToken p,string key)
        {var t=p[key];if(t==null || (t.Type!=JTokenType.Float && t.Type!=JTokenType.Integer))return null;double n=(double)t;return Double.IsNaN(n)||Double.IsInfinity(n)?null:n;}
        private static double? Average(IEnumerable<double?> values){var present=values.Where(v=>v.HasValue).Select(v=>v!.Value).ToArray();return present.Length>0?present.Average():(double?)null;}
        private static void Put(JObject result,string key,double? value)=>result[key]=value.HasValue?JToken.FromObject(value.Value):JValue.CreateNull();
    }
}
