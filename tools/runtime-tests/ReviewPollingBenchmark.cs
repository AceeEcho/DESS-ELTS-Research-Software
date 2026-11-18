using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Elts.Operator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Replays a selected historical review against the real loopback bridge. All
// payloads are generated in memory. This benchmark never opens a recording folder.
internal static class ReviewPollingBenchmark
{
    private const int Attempts=1000, Polls=30, PollHz=10;

    public static void Run()
    {
        var events=new List<JObject>();
        for(int i=0;i<Attempts;i++)
            events.Add(JObject.FromObject(new {eventType="BlockSkipped",monotonicTicks=i,
                payload=new {blockId="fixture-"+i,condition="Example",reason="Fictional skipped task"}}));
        string review=TaskDataReview.Build(events).ToString(Formatting.None);
        var participant=new {id="fixture-profile",code="42",sessions=1};
        var details=JObject.FromObject(new {profiles=new[]{participant},profile=new {participant,
            sessions=new[]{new {id="fixture-run",visitId="fixture-visit",review}}},rows=Array.Empty<object>(),rowsPage=0});
        var live=JObject.FromObject(new {participant="42",state="SessionComplete",runId="fixture-run",accepted=0,
            data=new {revision=1,ready=true,busy=false,message="Ready"}});
        var full=(JObject)live.DeepClone();
        ((JObject)full["data"]).Merge(details);

        // The HTTP bridge only serves generated snapshots. Supply a nonexistent
        // asset root so even a stray static request has no source files.
        using(var server=new AdministratorServer(Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"))))
        using(var client=new HttpClient{BaseAddress=new Uri(server.Origin)})
        {
            client.DefaultRequestHeaders.Add("X-Elts-Token",server.Token);
            client.DefaultRequestHeaders.Add("Origin",server.Origin);
            var combined=Publication(full,server.PublishState);
            var compact=Publication(live,server.PublishState);
            var once=Publication(new JObject{["revision"]=1,["details"]=details},server.PublishCollection);
            // Warm the HTTP stack and worker-pool command path before timing either form.
            GetSnapshot(client,"/api/state?preview=0");
            SendCommand(server,client);
            var repeated=Poll(server,client,full,details,false);
            var split=Poll(server,client,live,details,true);
            Console.WriteLine(JsonConvert.SerializeObject(new {attempts=Attempts, pollsPerRun=Polls, nominalPollHz=PollHz,
                publication=new {combined,compact,detailSnapshot=once},
                loopback=new {combined=repeated,split},
                scope="Synthetic common live envelope; publication measures Newtonsoft serialization and UTF-8 publication on .NET. HTTP bytes exclude headers. Command observation is the client poll, before DOM rendering."},Formatting.Indented));
        }
    }

    private static byte[] GetSnapshot(HttpClient client,string path)=>
        client.GetByteArrayAsync(path).GetAwaiter().GetResult();

    private static void SendCommand(AdministratorServer server,HttpClient client)
    {
        using(var body=new StringContent("{\"action\":\"viewParticipant\",\"id\":\"fixture-profile\"}",Encoding.UTF8,"application/json"))
        {
            var request=client.PostAsync("/api/command",body);
            AdministratorServer.PendingCommand pending=null;
            if(!SpinWait.SpinUntil(()=>server.TryTakeCommand(out pending),1000) || !pending.TryBegin())
                throw new Exception("The benchmark command did not reach the loopback queue.");
            pending.Complete("{\"ok\":true}");
            using(var response=request.GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                if(!(bool)JObject.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult())["ok"])
                    throw new Exception("Command rejected.");
            }
        }
    }

    private static object Publication(JObject data,Action<string> publish)
    {
        string Serialize()=>JsonConvert.SerializeObject(data,Formatting.None);
        publish(Serialize()); // Warm up this serializer and publication path.
        long start=GC.GetAllocatedBytesForCurrentThread();
        var clock=Stopwatch.StartNew();
        for(int i=0;i<Polls;i++)publish(Serialize());
        double elapsed=clock.Elapsed.TotalMilliseconds;
        long bytes=GC.GetAllocatedBytesForCurrentThread()-start;
        return new {bodyBytes=Encoding.UTF8.GetByteCount(Serialize()),managedBytesPerPublication=bytes/Polls,
            meanMilliseconds=Math.Round(elapsed/Polls,3)};
    }

    private static object Poll(AdministratorServer server,HttpClient client,JObject state,JObject details,bool split)
    {
        long bodyBytes=0;
        int cachedRevision=0,detailRequests=0,accepted=0,revision=1;
        var acknowledgments=new List<double>();
        var observations=new List<double>();
        var elapsed=Stopwatch.StartNew();
        for(int i=0;i<Polls;i++)
        {
            long deadline=(long)i*1000/PollHz;
            if(deadline>elapsed.ElapsedMilliseconds)Thread.Sleep((int)(deadline-elapsed.ElapsedMilliseconds));
            // Exactly one new historical revision is produced halfway through.
            if(i==Polls/2)revision++;
            if(split && (i==0 || i==Polls/2))
            {
                details["rowsPage"]=revision-1;
                server.PublishCollection(new JObject{["revision"]=revision,["details"]=details}.ToString(Formatting.None));
            }
            if(!split)state["data"]["rowsPage"]=revision-1;
            Stopwatch command=null;
            if(i%3==0)
            {
                command=Stopwatch.StartNew();
                SendCommand(server,client);
                accepted++;
                acknowledgments.Add(command.Elapsed.TotalMilliseconds);
            }
            state["accepted"]=accepted;
            state["data"]["revision"]=revision;
            server.PublishState(state.ToString(Formatting.None));
            var raw=GetSnapshot(client,"/api/state?preview=0");
            bodyBytes+=raw.Length;
            var current=JObject.Parse(Encoding.UTF8.GetString(raw));
            int received=(int)current["data"]["revision"];
            if(split && received!=cachedRevision)
            {
                var historical=GetSnapshot(client,"/api/data");
                var batch=JObject.Parse(Encoding.UTF8.GetString(historical));
                if((int)batch["revision"]!=received || (int)batch["details"]["rowsPage"]!=received-1 ||
                    (string)batch["details"]["profile"]["participant"]["code"]!="42")
                    throw new Exception("A stale or incorrect historical snapshot was served.");
                bodyBytes+=historical.Length;
                detailRequests++;
                cachedRevision=received;
            }
            if((int)current["accepted"]!=accepted)throw new Exception("Acknowledged command is missing from the poll.");
            if(command!=null)observations.Add(command.Elapsed.TotalMilliseconds);
        }
        if(split && detailRequests!=2)throw new Exception("Unchanged polls fetched history again.");
        double Percentile(List<double> sample,double fraction)=>
            Math.Round(sample.OrderBy(x=>x).ElementAt((int)Math.Ceiling(sample.Count*fraction)-1),3);
        return new {stateRequests=Polls,detailRequests,commands=accepted,bodyBytes,
            bodyBytesPerSecondAtNominalCadence=bodyBytes*PollHz/Polls,
            commandAckMedianMs=Percentile(acknowledgments,.5),commandAckP95Ms=Percentile(acknowledgments,.95),
            commandObservedMedianMs=Percentile(observations,.5),commandObservedP95Ms=Percentile(observations,.95)};
    }
}
