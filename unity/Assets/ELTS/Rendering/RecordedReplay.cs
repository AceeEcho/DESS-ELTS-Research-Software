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
        private const int MaximumSummaryBytes = MaximumLineCharacters * 4;
        private readonly ReplayFrame[] frames;
        private readonly ReplayTarget[] targets;
        // Per-target histories make repeated viewer scrubs proportional to the
        // number of target identities, rather than rescanning every log row.
        private readonly IReadOnlyDictionary<string, ReplayTarget[]> targetHistory;
        public string SourceRevision { get; }
        public string ConfigurationHash { get; }
        public string SummaryHash { get; }
        public bool Synthetic { get; }
        public long StartTicks { get; }
        public double DurationSeconds { get; }
        public int FrameCount => frames.Length;
        public RecordedReplay(ReplayFrame[] frames,ReplayTarget[] targets,string revision,string configHash,string summaryHash,bool synthetic)
        {
            if(frames==null) throw new ArgumentNullException(nameof(frames));
            if(targets==null) throw new ArgumentNullException(nameof(targets));
            if(frames.Length==0) throw new InvalidDataException("Replay has no samples.");
            if(String.IsNullOrWhiteSpace(revision)||String.IsNullOrWhiteSpace(configHash)||String.IsNullOrWhiteSpace(summaryHash))
                throw new ArgumentException("Replay provenance is required.");
            if(!Regex.IsMatch(configHash,"^[a-f0-9]{64}$")||!Regex.IsMatch(summaryHash,"^[a-f0-9]{64}$"))
                throw new ArgumentException("Replay hashes must be lowercase SHA-256 values.");
            this.frames=(ReplayFrame[])frames.Clone();this.targets=(ReplayTarget[])targets.Clone();
            ValidateFrames(this.frames); ValidateTargets(this.targets);
            SourceRevision=revision;ConfigurationHash=configHash;SummaryHash=summaryHash;Synthetic=synthetic;
            StartTicks=this.frames[0].Ticks;
            long endTicks=Math.Max(this.frames[this.frames.Length-1].Ticks,this.targets.Length==0?StartTicks:this.targets[this.targets.Length-1].Ticks);
            if(endTicks<StartTicks) throw new InvalidDataException("Replay ends before it starts.");
            DurationSeconds=(endTicks-StartTicks)/10000000.0;
            var histories=new Dictionary<string,List<ReplayTarget>>(StringComparer.Ordinal);
            foreach(var target in this.targets)
            {
                if(!histories.TryGetValue(target.Key,out var history)) histories.Add(target.Key,history=new List<ReplayTarget>());
                history.Add(target);
            }
            var immutable=new Dictionary<string,ReplayTarget[]>(StringComparer.Ordinal);
            foreach(var pair in histories) immutable.Add(pair.Key,pair.Value.ToArray());
            targetHistory=new ReadOnlyDictionary<string,ReplayTarget[]>(immutable);
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
            foreach(var pair in targetHistory)
            {
                int index=LatestAt(pair.Value,ticks);
                if(index<0||pair.Value[index].Lifecycle=="Destroyed") continue;
                result.Add(pair.Key,pair.Value[index].Position);
            }
            return new ReadOnlyDictionary<string,Vector3d>(result);
        }
        private static int LatestAt(ReplayTarget[] history,long ticks)
        {
            int left=0,right=history.Length-1,result=-1;
            while(left<=right){int mid=left+((right-left)>>1);if(history[mid].Ticks<=ticks){result=mid;left=mid+1;}else right=mid-1;}
            return result;
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
            byte[] summaryBytes=ReadBoundedBytes(summaryPath,MaximumSummaryBytes);
            string summaryText=new UTF8Encoding(false,true).GetString(summaryBytes);
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
                    using(var reader=new BoundedLines(stream))
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
                                    if(!Regex.IsMatch(property.Name,@"^[A-Za-z0-9_.-]+$")||property.Name.Length>256)throw new InvalidDataException("Invalid event payload key.");
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
            using(var sha=SHA256.Create())return new RecordedReplay(frames.ToArray(),targets.ToArray(),Text(provenance,"sourceRevision"),HashText(provenance,"configurationHash"),Hex(sha.ComputeHash(summaryBytes)),(bool)provenance["synthetic"]!);
        }
        private static JObject Parse(string text)
        {
            if(text.Length>MaximumLineCharacters)throw new InvalidDataException("Replay JSON record is too large.");
            ValidateJsonSyntax(text);
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
        private static void ValidateFrames(ReplayFrame[] values)
        {
            long previous=-1; foreach(var frame in values){if(frame.Ticks<0||frame.Ticks<previous)throw new InvalidDataException("Nonmonotonic frame timestamps.");ValidatePoseStatus(frame.Head,frame.HeadStatus);ValidatePoseStatus(frame.Weapon,frame.WeaponStatus);previous=frame.Ticks;}
        }
        private static void ValidateTargets(ReplayTarget[] values)
        {
            long previous=-1; foreach(var target in values){if(target.Ticks<0||target.Ticks<previous)throw new InvalidDataException("Nonmonotonic target timestamps.");if(String.IsNullOrEmpty(target.Key)||!new[]{"Spawned","Updated","Destroyed"}.Contains(target.Lifecycle)||!RenderNumbers.Finite(target.Position.X)||!RenderNumbers.Finite(target.Position.Y)||!RenderNumbers.Finite(target.Position.Z))throw new InvalidDataException("Invalid target record.");previous=target.Ticks;}
        }
        private static void ValidatePoseStatus(RigidPose? pose,string status)
        {
            if(String.IsNullOrEmpty(status))throw new InvalidDataException("Frame status is required.");
            string[] parts=status.Split(new[]{" / "},StringSplitOptions.None);if(parts.Length!=2||!new[]{"Connected","Disconnected","Reconnecting"}.Contains(parts[0])||!new[]{"Unavailable","Valid","OutOfRange","DriverFault","RejectedNativePose"}.Contains(parts[1]))throw new InvalidDataException("Invalid frame status.");
            if(parts[1]=="Valid"?(parts[0]!="Connected"||!pose.HasValue):pose.HasValue)throw new InvalidDataException("Pose does not match frame status.");
            if(pose.HasValue&&(!pose.Value.Position.IsFinite||!pose.Value.Orientation.IsUnit))throw new InvalidDataException("Invalid pose contents.");
        }
        private sealed class BoundedLines : IDisposable
        {
            private readonly StreamReader reader;
            private readonly char[] buffer=new char[4096]; private int offset,length;
            public BoundedLines(Stream stream){reader=new StreamReader(stream,new UTF8Encoding(false,true),false,4096,true);}
            public string? ReadLine(){var line=new StringBuilder(256);while(true){if(offset==length){length=reader.Read(buffer,0,buffer.Length);offset=0;if(length==0)return line.Length==0?null:line.ToString();}while(offset<length){char c=buffer[offset++];if(c=='\n')return line.ToString();if(line.Length>=MaximumLineCharacters)throw new InvalidDataException("Replay JSON record is too large.");line.Append(c);}}}
            public void Dispose(){reader.Dispose();}
        }
        private static byte[] ReadBoundedBytes(string path,int maximum)
        {
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(stream.Length>maximum)throw new InvalidDataException("Replay summary is too large.");using(var output=new MemoryStream((int)stream.Length)){stream.CopyTo(output,4096);return output.ToArray();}}
        }
        // Newtonsoft accepts several JavaScript extensions; this small grammar
        // gate keeps the persisted products JSON RFC syntax before loading them.
        private static void ValidateJsonSyntax(string text){int index=0;ParseJsonValue(text,ref index);SkipWhitespace(text,ref index);if(index!=text.Length)throw new InvalidDataException("Trailing JSON content.");}
        private static void ParseJsonValue(string text,ref int index){SkipWhitespace(text,ref index);if(index>=text.Length)throw new InvalidDataException("Missing JSON value.");switch(text[index]){case '{':ParseJsonObject(text,ref index);break;case '[':ParseJsonArray(text,ref index);break;case '\"':ParseJsonString(text,ref index);break;case 't':Expect(text,ref index,"true");break;case 'f':Expect(text,ref index,"false");break;case 'n':Expect(text,ref index,"null");break;default:if(text[index]=='-'||char.IsDigit(text[index]))ParseJsonNumber(text,ref index);else throw new InvalidDataException("Unsupported JSON syntax.");break;}}
        private static void ParseJsonObject(string text,ref int index){index++;SkipWhitespace(text,ref index);if(Take(text,ref index,'}'))return;while(true){SkipWhitespace(text,ref index);if(index>=text.Length||text[index]!='\"')throw new InvalidDataException("JSON object key must be quoted.");ParseJsonString(text,ref index);SkipWhitespace(text,ref index);Expect(text,ref index,":");ParseJsonValue(text,ref index);SkipWhitespace(text,ref index);if(Take(text,ref index,'}'))return;if(!Take(text,ref index,','))throw new InvalidDataException("Invalid JSON object separator.");SkipWhitespace(text,ref index);if(index<text.Length&&text[index]=='}')throw new InvalidDataException("Trailing JSON comma.");}throw new InvalidDataException("Unterminated JSON object.");}
        private static void ParseJsonArray(string text,ref int index){index++;SkipWhitespace(text,ref index);if(Take(text,ref index,']'))return;while(true){ParseJsonValue(text,ref index);SkipWhitespace(text,ref index);if(Take(text,ref index,']'))return;if(!Take(text,ref index,','))throw new InvalidDataException("Invalid JSON array separator.");SkipWhitespace(text,ref index);if(index<text.Length&&text[index]==']')throw new InvalidDataException("Trailing JSON comma.");}throw new InvalidDataException("Unterminated JSON array.");}
        private static void ParseJsonString(string text,ref int index){if(!Take(text,ref index,'\"'))throw new InvalidDataException("JSON string required.");while(index<text.Length){char c=text[index++];if(c=='\"')return;if(c<0x20)throw new InvalidDataException("Control character in JSON string.");if(c=='\\'){if(index>=text.Length)break;char escape=text[index++];if("\"\\/bfnrt".IndexOf(escape)<0){if(escape!='u'||index+4>text.Length||!IsHex(text[index])||!IsHex(text[index+1])||!IsHex(text[index+2])||!IsHex(text[index+3]))throw new InvalidDataException("Invalid JSON escape.");index+=4;}}}throw new InvalidDataException("Unterminated JSON string.");}
        private static void ParseJsonNumber(string text,ref int index){if(Take(text,ref index,'-')&&index>=text.Length)throw new InvalidDataException("Invalid JSON number.");if(Take(text,ref index,'0')){if(index<text.Length&&char.IsDigit(text[index]))throw new InvalidDataException("Leading zero in JSON number.");}else{if(index>=text.Length||text[index]<'1'||text[index]>'9')throw new InvalidDataException("Invalid JSON number.");while(index<text.Length&&char.IsDigit(text[index]))index++;}if(Take(text,ref index,'.')){if(index>=text.Length||!char.IsDigit(text[index]))throw new InvalidDataException("Invalid JSON fraction.");while(index<text.Length&&char.IsDigit(text[index]))index++;}if(index<text.Length&&(text[index]=='e'||text[index]=='E')){index++;if(index<text.Length&&(text[index]=='+'||text[index]=='-'))index++;if(index>=text.Length||!char.IsDigit(text[index]))throw new InvalidDataException("Invalid JSON exponent.");while(index<text.Length&&char.IsDigit(text[index]))index++;}}
        private static void SkipWhitespace(string text,ref int index){while(index<text.Length&&(text[index]==' '||text[index]=='\t'||text[index]=='\r'||text[index]=='\n'))index++;}
        private static bool Take(string text,ref int index,char value){if(index<text.Length&&text[index]==value){index++;return true;}return false;}
        private static void Expect(string text,ref int index,string value){if(index+value.Length>text.Length||String.CompareOrdinal(text,index,value,0,value.Length)!=0)throw new InvalidDataException("Invalid JSON token.");index+=value.Length;}
        private static bool IsHex(char value)=>(value>='0'&&value<='9')||(value>='a'&&value<='f')||(value>='A'&&value<='F');
        private static string Hex(byte[] value)=>string.Concat(value.Select(b=>b.ToString("x2",CultureInfo.InvariantCulture)));
    }
}
