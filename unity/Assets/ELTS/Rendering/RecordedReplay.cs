#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elts.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.Rendering
{
    public readonly struct ReplayFrame
    {
        public readonly long Ticks;
        public readonly RigidPose? Head, Weapon;
        public readonly string HeadStatus, WeaponStatus;
        public ReplayFrame(long ticks, RigidPose? head, RigidPose? weapon, string headStatus, string weaponStatus)
        { Ticks=ticks;Head=head;Weapon=weapon;HeadStatus=headStatus;WeaponStatus=weaponStatus; }
    }
    public readonly struct ReplayTarget
    {
        public readonly long Ticks;
        public readonly string Key, Lifecycle;
        public readonly Vector3d Position;
        public ReplayTarget(long ticks,string key,string lifecycle,Vector3d position) {Ticks=ticks;Key=key;Lifecycle=lifecycle;Position=position;}
    }
    /// <summary>Strict, read-only v1 replay. Load on a worker, then query immutable data on the main thread.</summary>
    public sealed class RecordedReplay
    {
        // Bounded development viewer; larger studies belong in the streaming analysis package.
        public const int MaximumRecordsPerProduct = 1000000;
        public const int MaximumLineCharacters = 65536;
        private readonly ReplayFrame[] frames;
        private readonly ReplayTarget[] targets;
        public string SourceRevision { get; }
        public string ConfigurationHash { get; }
        public string SummaryHash { get; }
        public bool Synthetic { get; }
        public long StartTicks { get; }
        public double DurationSeconds { get; }
        public int FrameCount => frames.Length;
        public RecordedReplay(ReplayFrame[] frames,ReplayTarget[] targets,string revision,string configHash,string summaryHash,bool synthetic)
        {
            if(frames.Length==0) throw new InvalidDataException("Replay has no samples.");
            this.frames=(ReplayFrame[])frames.Clone();this.targets=(ReplayTarget[])targets.Clone();
            SourceRevision=revision;ConfigurationHash=configHash;SummaryHash=summaryHash;Synthetic=synthetic;
            StartTicks=frames[0].Ticks;
            DurationSeconds=(Math.Max(frames[frames.Length-1].Ticks,targets.Length==0?0:targets[targets.Length-1].Ticks)-StartTicks)/10000000.0;
        }
        public ReplayFrame FrameAt(double seconds)
        {
            long ticks=AtTicks(seconds);int left=0,right=frames.Length-1;
            while(left<right){int mid=(left+right+1)/2;if(frames[mid].Ticks<=ticks)left=mid;else right=mid-1;}
            return frames[left];
        }
        public IReadOnlyDictionary<string,Vector3d> TargetsAt(double seconds)
        {
            long ticks=AtTicks(seconds);var result=new Dictionary<string,Vector3d>(StringComparer.Ordinal);
            foreach(var target in targets){if(target.Ticks>ticks)break;if(target.Lifecycle=="Destroyed")result.Remove(target.Key);else result[target.Key]=target.Position;}
            return new ReadOnlyDictionary<string,Vector3d>(result);
        }
        private long AtTicks(double seconds)
        {
            if(!RenderNumbers.Finite(seconds)||seconds<0||seconds>DurationSeconds)throw new ArgumentOutOfRangeException(nameof(seconds));
            return StartTicks+(long)Math.Round(seconds*10000000);
        }
        public static RecordedReplay Load(string directory)
        {
            string root=Path.GetFullPath(directory);
            if((File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Replay directory cannot be a link.");
            string summaryPath=Path.Combine(root,"session-summary.json");
            string summaryText=File.ReadAllText(summaryPath,new UTF8Encoding(false,true));
            var summary=Parse(summaryText);
            Fields(summary,"schemaVersion","runId","complete","error","provenance","counts","checksumsSha256");
            Equal(summary,"schemaVersion","elts.session-summary.v1");
            if(summary["complete"]?.Type!=JTokenType.Boolean || !(bool)summary["complete"]! || summary["error"]?.Type!=JTokenType.Null)
                throw new InvalidDataException("Replay requires a complete successful run.");
            Identifier(summary,"runId",true);
            var provenance=Object(summary,"provenance");
            Fields(provenance,"applicationVersion","sourceRevision","configurationHash","scenarioHash","fixtureHash","synthetic","utcStartupAnchor");
            Text(provenance,"applicationVersion");Text(provenance,"sourceRevision");
            foreach(string key in new[]{"configurationHash","scenarioHash","fixtureHash"})HashText(provenance,key);
            if(provenance["synthetic"]?.Type!=JTokenType.Boolean || !DateTimeOffset.TryParse(Text(provenance,"utcStartupAnchor"),CultureInfo.InvariantCulture,DateTimeStyles.None,out _))throw new InvalidDataException("Invalid provenance.");
            var counts=Object(summary,"counts");Fields(counts,"droppedSamples","writtenSamples","writtenEvents","writtenTargets");Integer(counts,"droppedSamples");
            var hashes=Object(summary,"checksumsSha256");Fields(hashes,"samples.ndjson","events.ndjson","targets.ndjson");
            var frames=new List<ReplayFrame>();var targets=new List<ReplayTarget>();
            foreach(var product in new[]{("samples","writtenSamples"),("events","writtenEvents"),("targets","writtenTargets")})
            {
                string filename=product.Item1+".ndjson",path=Path.Combine(root,filename);
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Replay products cannot be links.");
                using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    using(var sha=SHA256.Create())if(Hex(sha.ComputeHash(stream))!=HashText(hashes,filename))throw new InvalidDataException("Replay checksum mismatch: "+filename);
                    stream.Position=0;long previousSequence=0,previousTicks=-1;int count=0;
                    using(var reader=new StreamReader(stream,new UTF8Encoding(false,true),false,4096,true))
                    {
                        string? line;
                        while((line=reader.ReadLine())!=null)
                        {
                            if(++count>MaximumRecordsPerProduct)throw new InvalidDataException("Replay record limit exceeded.");
                            var row=Parse(line);Equal(row,"schemaVersion","elts."+product.Item1+".v1");
                            long sequence=Integer(row,"sequence"),ticks=Integer(row,"monotonicTicks");
                            if(sequence<=previousSequence||ticks<previousTicks)throw new InvalidDataException("Nonmonotonic replay stream.");previousSequence=sequence;previousTicks=ticks;
                            if(product.Item1=="samples")
                            {
                                Fields(row,"schemaVersion","sequence","monotonicTicks","head","weapon");
                                var head=Pose(Object(row,"head"),out var hs);var weapon=Pose(Object(row,"weapon"),out var ws);
                                frames.Add(new ReplayFrame(ticks,head,weapon,hs,ws));
                            }
                            else if(product.Item1=="targets")
                            {
                                Fields(row,"schemaVersion","sequence","monotonicTicks","targetId","blockId","lifecycle","worldPositionMeters","worldVelocityMetersPerSecond","scenarioSeed","scenarioVersion");
                                string lifecycle=Choice(row,"lifecycle","Spawned","Updated","Destroyed");
                                Integer(row,"scenarioSeed");Text(row,"scenarioVersion");Vector(Object(row,"worldVelocityMetersPerSecond"));
                                targets.Add(new ReplayTarget(ticks,Identifier(row,"blockId")+"/"+Identifier(row,"targetId"),lifecycle,Vector(Object(row,"worldPositionMeters"))));
                            }
                            else
                            {
                                Fields(row,"schemaVersion","sequence","monotonicTicks","eventType","payload");Identifier(row,"eventType");
                                foreach(var property in Object(row,"payload").Properties())
                                {
                                    if(!Regex.IsMatch(property.Name,@"^[A-Za-z][A-Za-z0-9_.-]*$")||property.Name.Length>128)throw new InvalidDataException("Invalid event payload key.");
                                    var type=property.Value.Type;
                                    if(type!=JTokenType.String&&type!=JTokenType.Integer&&type!=JTokenType.Float&&type!=JTokenType.Boolean)throw new InvalidDataException("Unsupported event payload value.");
                                    if(type==JTokenType.Float&&!RenderNumbers.Finite((double)property.Value))throw new InvalidDataException("Nonfinite payload.");
                                }
                            }
                        }
                    }
                    if(count!=Integer(counts,product.Item2))throw new InvalidDataException("Replay count mismatch: "+filename);
                }
            }
            using(var sha=SHA256.Create())return new RecordedReplay(frames.ToArray(),targets.ToArray(),Text(provenance,"sourceRevision"),HashText(provenance,"configurationHash"),Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(summaryText))),(bool)provenance["synthetic"]!);
        }
        private static JObject Parse(string text)
        {
            if(text.Length>MaximumLineCharacters)throw new InvalidDataException("Replay JSON record is too large.");
            using(var reader=new JsonTextReader(new StringReader(text)){DateParseHandling=DateParseHandling.None,MaxDepth=24})
            {
                var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error,CommentHandling=CommentHandling.Load});
                if(reader.Read())throw new InvalidDataException("Trailing replay JSON content.");return result;
            }
        }
        private static RigidPose? Pose(JObject value,out string status)
        {
            Fields(value,"trackerId","connection","validity","pose");Identifier(value,"trackerId");
            string connection=Choice(value,"connection","Connected","Disconnected","Reconnecting"),validity=Choice(value,"validity","Unavailable","Valid","OutOfRange","DriverFault","RejectedNativePose");
            status=connection+" / "+validity;
            if(validity!="Valid"){if(value["pose"]?.Type!=JTokenType.Null)throw new InvalidDataException("Invalid tracking must have null pose.");return null;}
            if(connection!="Connected")throw new InvalidDataException("Valid tracking must be connected.");
            var pose=Object(value,"pose");Fields(pose,"positionMeters","orientation");var q=Object(pose,"orientation");Fields(q,"x","y","z","w");
            double x=Number(q,"x"),y=Number(q,"y"),z=Number(q,"z"),w=Number(q,"w");
            if(Math.Abs(x*x+y*y+z*z+w*w-1)>1e-8)throw new InvalidDataException("Replay quaternion must be unit length.");
            return new RigidPose(Vector(Object(pose,"positionMeters")),new Quaterniond(x,y,z,w));
        }
        private static Vector3d Vector(JObject value){Fields(value,"x","y","z");return new Vector3d(Number(value,"x"),Number(value,"y"),Number(value,"z"));}
        private static double Number(JObject value,string key){if(value[key]?.Type!=JTokenType.Integer&&value[key]?.Type!=JTokenType.Float)throw new InvalidDataException("Expected number: "+key);double number=(double)value[key]!;if(!RenderNumbers.Finite(number))throw new InvalidDataException("Nonfinite number.");return number;}
        private static long Integer(JObject value,string key){if(value[key]?.Type!=JTokenType.Integer)throw new InvalidDataException("Expected integer: "+key);long result=(long)value[key]!;if(result<0)throw new InvalidDataException("Negative integer.");return result;}
        private static string Text(JObject value,string key){if(value[key]?.Type!=JTokenType.String)throw new InvalidDataException("Expected string: "+key);string s=(string)value[key]!;if(s.Length==0||s.Length>4096)throw new InvalidDataException("Invalid string length.");return s;}
        private static string Identifier(JObject value,string key,bool run=false){string s=Text(value,key);if(s.Length>(run?80:256)||!Regex.IsMatch(s,run?@"^[A-Za-z0-9_-]+$":@"^[A-Za-z0-9_.-]+$"))throw new InvalidDataException("Invalid identifier: "+key);return s;}
        private static string HashText(JObject value,string key){string s=Text(value,key);if(!Regex.IsMatch(s,"^[a-f0-9]{64}$"))throw new InvalidDataException("Invalid hash.");return s;}
        private static string Choice(JObject value,string key,params string[] choices){string s=Text(value,key);if(!choices.Contains(s))throw new InvalidDataException("Unsupported "+key+": "+s);return s;}
        private static void Equal(JObject value,string key,string expected){if(Text(value,key)!=expected)throw new InvalidDataException("Unsupported "+key+"; expected "+expected);}
        private static JObject Object(JObject value,string key)=>value[key] as JObject??throw new InvalidDataException("Expected object: "+key);
        private static void Fields(JObject value,params string[] names){if(value.Properties().Count()!=names.Length||names.Any(n=>value[n]==null))throw new InvalidDataException("Unknown or missing replay fields.");}
        private static string Hex(byte[] value)=>string.Concat(value.Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));
    }
}
