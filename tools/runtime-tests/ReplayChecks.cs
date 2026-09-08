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
    private static string Make(string sample,string events,string targets,string summaryOverride=null)
    {
        string dir=Path.Combine(Path.GetTempPath(),"elts-replay-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,"samples.ndjson"),sample,new UTF8Encoding(false));File.WriteAllText(Path.Combine(dir,"events.ndjson"),events,new UTF8Encoding(false));File.WriteAllText(Path.Combine(dir,"targets.ndjson"),targets,new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir,"session-summary.json"),summaryOverride??Summary(sample,events,targets),new UTF8Encoding(false));return dir;
    }
    private static string Sample(string head=null){head=head??"{\"trackerId\":\"head\",\"connection\":\"Disconnected\",\"validity\":\"Unavailable\",\"pose\":null}";return "{\"schemaVersion\":\"elts.samples.v1\",\"sequence\":1,\"monotonicTicks\":0,\"head\":"+head+",\"weapon\":{\"trackerId\":\"weapon\",\"connection\":\"Connected\",\"validity\":\"Valid\",\"pose\":{\"positionMeters\":{\"x\":0,\"y\":0,\"z\":0},\"orientation\":{\"x\":0,\"y\":0,\"z\":0,\"w\":1}}}}\n";}
    private static readonly string Events="{\"schemaVersion\":\"elts.events.v1\",\"sequence\":1,\"monotonicTicks\":0,\"eventType\":\"RunStarted\",\"payload\":{\"mode\":\"synthetic\"}}\n";
    private static readonly string Targets="{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":1,\"monotonicTicks\":0,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Spawned\",\"worldPositionMeters\":{\"x\":1,\"y\":2,\"z\":3},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n"+
        "{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":2,\"monotonicTicks\":50000000,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Updated\",\"worldPositionMeters\":{\"x\":4,\"y\":5,\"z\":6},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n"+
        "{\"schemaVersion\":\"elts.targets.v1\",\"sequence\":3,\"monotonicTicks\":100000000,\"targetId\":\"one\",\"blockId\":\"block\",\"lifecycle\":\"Destroyed\",\"worldPositionMeters\":{\"x\":4,\"y\":5,\"z\":6},\"worldVelocityMetersPerSecond\":{\"x\":0,\"y\":0,\"z\":0},\"scenarioSeed\":1,\"scenarioVersion\":\"v1\"}\n";
    public static void Main(string[] args)
    {
        string dir=Make(Sample(),Events,Targets);var replay=RecordedReplay.Load(dir);Check(replay.FrameCount==1,"valid replay did not load");Check(replay.TargetsAt(0).Count==1,"spawn missing");Check(replay.TargetsAt(6).Count==1&&replay.TargetsAt(6)["block/one"].X==4,"target seek failed");Check(replay.TargetsAt(10).Count==0,"destroy lifecycle failed");
        string invalid=Make(Sample("{\"trackerId\":\"head\",\"connection\":\"Connected\",\"validity\":\"Invalid\",\"pose\":{}}"),Events,Targets);Reject(invalid,"invalid pose was accepted");
        Reject(Make(Sample(),Events.Replace("RunStarted","Unknown!"),Targets),"unsupported event was accepted");
        Reject(Make(Sample(),Events.Replace("\"mode\":\"synthetic\"","\"mode\":\"synthetic\",\"mode\":\"duplicate\""),Targets),"duplicate field was accepted");
        Reject(Make(Sample(),Events.Replace("\"synthetic\"","1e9999"),Targets),"nonfinite payload was accepted");
        string missing=Make(Sample(),Events,Targets);File.Delete(Path.Combine(missing,"events.ndjson"));Reject(missing,"missing product was accepted");
        string tampered=Make(Sample(),Events,Targets);File.AppendAllText(Path.Combine(tampered,"samples.ndjson"),"\n");Reject(tampered,"hash tamper was accepted");
        Reject(Make(Sample(),Events,Targets,"{}"),"unsupported summary was accepted");
        bool constructorRejected=false;try{new RecordedReplay(null,null,"rev","x","y",true);}catch(ArgumentException){constructorRejected=true;}Check(constructorRejected,"constructor accepted null/invalid input");
        foreach(string path in args){if(Directory.Exists(path)){var loaded=RecordedReplay.Load(path);Check(loaded.FrameCount>0,"supplied generated log had no frames");}}
        Console.WriteLine("PASS: "+checks+" replay checks");
    }
}
