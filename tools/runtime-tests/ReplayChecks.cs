#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Elts.Rendering;

internal static class ReplayChecks
{
    private static int checks;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    private static void Reject(string directory,string message){bool rejected=false;try{RecordedReplay.Load(directory);}catch(Exception){rejected=true;}Check(rejected,message);}
    private static string Hash(string text){using(var sha=SHA256.Create())return Convert.ToHexString(Encoding.UTF8.GetBytes(text)).ToLowerInvariant();}
    private static string Sha(byte[] bytes){using(var sha=SHA256.Create())return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();}
    private static string Summary(string sample,string events,string targets,string config="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",bool complete=true)
    {
        return "{\"schemaVersion\":\"elts.session-summary.v1\",\"runId\":\"replay-check\",\"complete\":"+(complete?"true":"false")+",\"error\":null,\"provenance\":{\"applicationVersion\":\"test\",\"sourceRevision\":\"rev\",\"configurationHash\":\""+config+"\",\"scenarioHash\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"fixtureHash\":\"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc\",\"synthetic\":true,\"utcStartupAnchor\":\"2026-09-08T00:00:00Z\"},\"counts\":{\"droppedSamples\":0,\"writtenSamples\":1,\"writtenEvents\":1,\"writtenTargets\":3},\"checksumsSha256\":{\"samples.ndjson\":\""+Sha(Encoding.UTF8.GetBytes(sample))+"\",\"events.ndjson\":\""+Sha(Encoding.UTF8.GetBytes(events))+"\",\"targets.ndjson\":\""+Sha(Encoding.UTF8.GetBytes(targets))+"\"}}";
    }
    private static string Make(string sample,string events,string targets,string? summaryOverride=null)
    {
        string dir=Path.Combine(Path.GetTempPath(),"elts-replay-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"samples.ndjson"),sample,new UTF8Encoding(false));File.WriteAllText(Path.Combine(dir,"events.ndjson"),events,new UTF8Encoding(false));File.WriteAllText(Path.Combine(dir,"targets.ndjson"),targets,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir,"session-summary.json"),summaryOverride??Summary(sample,events,targets),new UTF8Encoding(false));return dir;
    }
    private static string Sample(string? head=null){head=head??"{\"trackerId\":\"head\",\"connection\":\"Disconnected\",\"validity\":\"Unavailable\",\"pose\":null}";return "{\"schemaVersion\":\"elts.samples.v1\",\"sequence\":1,\"monotonicTicks\":0,\"head\":"+head+",\"weapon\":{\"trackerId\":\"weapon\",\"connection\":\"Connected\",\"validity\":\"Valid\",\"pose\":{\"positionMeters\":{\"x\":0,\"y\":0,\"z\":0},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}}\n";}
    private static readonly string Events="{\"schemaVersion\":\"elts.events.v1\",\"sequence\":1,\"monotonicTicks\":0,\"eventType\":\"RunStarted\",\"payload\":{\"mode\":\"synthetic\"}}\n";
    private static readonly string Targets="{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":1,\"monotonicTicks\":0,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Spawned\",\"worldPositionMeters\":{\"x\":1,\"y\":2,\"z\":3},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n"+
        "{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":2,\"monotonicTicks\":50000000,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Updated\",\"worldPositionMeters\":{\"x\":4,\"y\":5,\"z\":6},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n"+
        "{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":3,\"monotonicTicks\":100000000,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Destroyed\",\"worldPositionMeters\":{\"x\":4,\"y\":5,\"z\":6},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n";
    private static readonly string TargetsV2=Targets.Replace("elts.targets.v1","elts.targets.v2").Replace("Destroyed","Despawned").Replace("\"v1\"","\"v2\"");
    public static void Main(string[] args)
    {
        string dir=Make(Sample(),Events,Targets);var replay=RecordedReplay.Load(dir);Check(replay.FrameCount==1,"valid replay did not load");Check(replay.TargetsAt(0).Count==1,"spawn missing");Check(replay.TargetsAt(6).Count==1&&replay.TargetsAt(6)["block/one"].X==4,"target seek failed");Check(replay.TargetsAt(10).Count==0,"destroy lifecycle failed");
        var expiryReplay=RecordedReplay.Load(Make(Sample(),Events,TargetsV2));Check(expiryReplay.TargetsAt(10).Count==0,"v2 expiry remained an active replay target");
        Reject(Make(Sample(),Events,Targets.Replace("Destroyed","Despawned")),"v1 accepted Despawned");
        Reject(Make(Sample(),Events,Targets+TargetsV2.Split('\n')[0]+"\n"),"mixed target versions were accepted");
        Check(RecordedReplay.Load(Make(Sample(),Events.Replace("\"mode\"","\"1.foo\""),Targets)).FrameCount==1,"schema payload key was rejected");
        string key256=new string('k',256);Check(RecordedReplay.Load(Make(Sample(),Events.Replace("\"mode\"","\""+key256+"\""),Targets)).FrameCount==1,"maximum schema payload key was rejected");
        string invalid=Make(Sample("{\"trackerId\":\"head\",\"connection\":\"Connected\",\"validity\":\"Invalid\",\"pose\":{}}"),Events,Targets);Reject(invalid,"invalid pose was accepted");
        Reject(Make(Sample(),Events.Replace("RunStarted","Unknown!"),Targets),"unsupported event was accepted");
        Reject(Make(Sample(),Events.Replace("\"mode\":\"synthetic\"","\"mode\":\"synthetic\",\"mode\":\"duplicate\""),Targets),"duplicate field was accepted");
        Reject(Make(Sample(),Events.Replace("\"synthetic\"","1e9999"),Targets),"nonfinite payload was accepted");
        string missing=Make(Sample(),Events,Targets);File.Delete(Path.Combine(missing,"events.ndjson"));Reject(missing,"missing product was accepted");
        string tampered=Make(Sample(),Events,Targets);File.AppendAllText(Path.Combine(tampered,"samples.ndjson"),"\n");Reject(tampered,"hash tamper was accepted");
        Reject(Make(Sample(),Events,Targets,"{}"),"unsupported summary was accepted");
        Reject(Make(Sample(),Events,Targets,new string(' ',RecordedReplay.MaximumLineCharacters*4+1)),"oversize summary was accepted");
        Reject(Make(Sample(),new string(' ',RecordedReplay.MaximumLineCharacters+1)+"\n",Targets),"oversize record was accepted");
        Reject(Make(Sample(),Events.Replace("{\"mode\":\"synthetic\"}","{/*comment*/\"mode\":\"synthetic\"}"),Targets),"commented JSON was accepted");
        Reject(Make(Sample(),Events.Replace("\"mode\":\"synthetic\"","'mode':'synthetic'"),Targets),"single-quoted JSON was accepted");
        Reject(Make(Sample(),Events.Replace("{\"mode\":\"synthetic\"}","{\"mode\":\"synthetic\",}"),Targets),"trailing comma was accepted");
        Reject(Make(Sample(),Events.Replace("{\"mode\":\"synthetic\"}","{mode:\"synthetic\"}"),Targets),"unquoted key was accepted");
        Reject(Make(Sample(),Events.Replace("\"mode\":\"synthetic\"","\"mode\":01"),Targets),"leading-zero number was accepted");
        Reject(Make(Sample(),Events.Replace("\"mode\":\"synthetic\"","\"mode\":0x1"),Targets),"hex number was accepted");
        string pending=Path.Combine(Path.GetTempPath(),"elts-replay-pending-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(pending);File.WriteAllText(Path.Combine(pending,".session-summary.pending.json"),Summary(Sample(),Events,Targets));Reject(pending,"pending-only replay was accepted");
        bool constructorRejected=false;try{new RecordedReplay(null!,null!,"rev","x","y",true);}catch(ArgumentException){constructorRejected=true;}Check(constructorRejected,"constructor accepted null/invalid input");
        bool invalidPose=false;try{new RecordedReplay(new[]{new ReplayFrame(0,null,null,"Connected / Valid","Disconnected / Unavailable")},new ReplayTarget[0],"rev","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",true);}catch(InvalidDataException){invalidPose=true;}Check(invalidPose,"constructor accepted invalid pose status");
        bool defaultPose=false;try{new RecordedReplay(new[]{new ReplayFrame(0,new Elts.Geometry.RigidPose(),null,"Connected / Valid","Disconnected / Unavailable")},new ReplayTarget[0],"rev","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",true);}catch(InvalidDataException){defaultPose=true;}Check(defaultPose,"constructor accepted default pose");
        bool invalidLifecycle=false;try{new RecordedReplay(new[]{new ReplayFrame(0,null,null,"Disconnected / Unavailable","Disconnected / Unavailable")},new[]{new ReplayTarget(0,"block/one","Bogus",new Elts.Geometry.Vector3d(0,0,0))},"rev","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",true);}catch(InvalidDataException){invalidLifecycle=true;}Check(invalidLifecycle,"constructor accepted invalid lifecycle");
        foreach(string path in args){if(Directory.Exists(path)){var loaded=RecordedReplay.Load(path);Check(loaded.FrameCount>0,"supplied generated log had no frames");}}
        Console.WriteLine("PASS: "+checks+" replay checks");
    }
}
