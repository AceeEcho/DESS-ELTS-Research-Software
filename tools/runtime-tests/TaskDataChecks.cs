using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Elts.Operator;
using Newtonsoft.Json.Linq;

internal static class TaskDataChecks
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static JObject Event(string type,double time,JObject fields=null)
    {var p=fields??new JObject();if(p["blockId"]==null)p["blockId"]="attempt-1";return new JObject{["eventType"]=type,["monotonicTicks"]=(long)(time*TimeSpan.TicksPerSecond),["payload"]=p};}
    public static JObject Fixture()
    {
        var events=new List<JObject>{Event("BlockMetadata",10,new JObject{["condition"]="WE_FT"}),Event("BlockStarted",10)};
        for(int i=0;i<3;i++)events.Add(Event("AimObserved",10+i*0.1,new JObject{["valid"]=true,["observationTicks"]=(long)((10+i*0.1)*TimeSpan.TicksPerSecond),["maximumGapSeconds"]=0.25,["directionX"]=Math.Sin(i*Math.PI/180),["directionY"]=0,["directionZ"]=Math.Cos(i*Math.PI/180),["angularErrorDeg"]=i*2}));
        events.Add(Event("ShotFired",10.3,new JObject{["outcome"]="Hit",["angularErrorDeg"]=1,["centerOffsetMm"]=4}));
        events.Add(Event("TargetHit",10.3));
        events.Add(Event("BlockPaused",11,new JObject{["activeSeconds"]=1}));
        events.Add(Event("AimObserved",13,new JObject{["valid"]=true,["angularErrorDeg"]=100}));
        events.Add(Event("BlockResumed",21));
        events.Add(Event("ShotFired",21.25,new JObject{["outcome"]="Miss",["angularErrorDeg"]=5,["centerOffsetMm"]=20}));
        events.Add(Event("BlockEnded",22.5,new JObject{["activeSeconds"]=2.5,["reason"]="operator_stop"}));
        events.Add(Event("BlockSkipped",23,new JObject{["blockId"]="attempt-2",["condition"]="WE_FT",["reason"]="Skipped fixture"}));
        return TaskDataReview.Build(events);
    }
    public static void Run(string root)
    {
        CheckAnalysisScreen(root);
        var result=Fixture();var a=result["attempts"][0];
        Check((double)a["shotsPerSecond"]==0.8 && (double)a["hitsPerSecond"]==0.4 && (double)a["missesPerSecond"]==0.4,"Rates exclude ten-second pause");
        Check((double)a["accuracyPercent"]==50,"Accuracy uses accepted shots");
        Check((double)a["aimErrorVarianceDeg2"]==4,"Sample variance of 0,2,4 is 4 deg squared");
        Check(Math.Abs((double)a["meanAimSpeedDegPerSecond"]-10)<1e-6,"Known angular motion is 10 degrees per second");
        Check(Math.Abs((double)a["aimObservedSeconds"]-0.2)<1e-8,"Motion coverage excludes pause");
        Check((int)a["aimObservations"]==3,"Paused aim excluded");
        Check((double)a["meanShotErrorDeg"]==3 && (double)a["p95ShotErrorDeg"]==5 && (double)a["meanMissOffsetMm"]==20,"Shot geometry aggregates and percentile");
        Check(a["seconds"].Count()==3 && (double)a["seconds"][2]["exposureSeconds"]==0.5 && (int)a["seconds"][2]["shots"]==0,"Partial final second and quiet bins retained");
        Check((double)a["shotDetails"][1]["activeSeconds"]==1.25,"Shot time excludes pause");
        Check(result["attempts"][1]["shots"].Type==JTokenType.Null,"Skipped attempt is unavailable, never zero performance");
        var sparse=TaskDataReview.Build(new[]{Event("BlockMetadata",0),Event("BlockStarted",0),Event("ShotFired",0.5,new JObject{["outcome"]="Hit"}),Event("ShotFired",1),Event("BlockEnded",2)})["attempts"][0];
        Check((int)sparse["hits"]==1 && sparse["misses"].Type==JTokenType.Null && (string)sparse["shotDetails"][1]["outcome"]=="Unknown","A shot without a recorded outcome stays unknown, never a miss");
        Check(sparse["meanShotErrorDeg"].Type==JTokenType.Null && sparse["aimCoveragePercent"].Type==JTokenType.Null,"Missing geometry remains unavailable");
        Check(sparse["headHits"].Type==JTokenType.Null,"Hits without a region remain unavailable rather than becoming body hits");
        var regional=TaskDataReview.Build(new[]{
            Event("BlockMetadata",0,new JObject{["condition"]="WE_MT"}),Event("BlockStarted",0),
            Event("WeaponConfigured",0,new JObject{["magazineCapacity"]=20}),
            Event("ShotFired",.1,new JObject{["outcome"]="Hit",["hitRegion"]="body",["damage"]=2,["remainingHealth"]=1,["targetId"]="target-1",["magazineBefore"]=20,["magazineAfter"]=19}),
            Event("TargetHit",.1),Event("ShotFired",.2,new JObject{["outcome"]="Hit",["hitRegion"]="limb",["damage"]=1,["remainingHealth"]=0,["targetId"]="target-1"}),
            Event("TargetHit",.2),Event("TargetDestroyed",.2),
            Event("ShotFired",.3,new JObject{["outcome"]="Cover",["coverId"]="crate-stack"}),
            Event("WeaponReloaded",.4),Event("WeaponDryFire",.5),Event("BlockEnded",1)
        })["attempts"][0];
        Check((int)regional["bodyHits"]==1 && (int)regional["limbHits"]==1 && (int)regional["headHits"]==0,"Region counts preserve each hit type");
        Check((int)regional["coverHits"]==1 && (int)regional["targetKills"]==1 && (double)regional["damageDealt"]==3,"Cover, kill and total damage are separate");
        Check((int)regional["misses"]==0 && (int)regional["reloads"]==1 && (int)regional["dryFires"]==1,"Cover is counted separately; reload and dry fire do not add scored shots");
        Check(Math.Abs((double)regional["accuracyPercent"]-200.0/3)<1e-8,"Cover still reduces shot accuracy");
        Check((int)regional["magazineCapacity"]==20 && (int)regional["shotDetails"][0]["magazineAfter"]==19,"Review preserves magazine capacity and shot ammunition state");
        var live=TaskDataReview.Build(new[]{Event("BlockMetadata",0),Event("BlockStarted",0),Event("ShotFired",0.5)})["attempts"][0];
        Check(live["misses"].Type==JTokenType.Null,"Shot without an outcome cannot be claimed as miss");
        var gapEvents=new List<JObject>{Event("BlockMetadata",0),Event("BlockStarted",0)};
        foreach(var point in new[]{(0.0,0.0,true),(.1,1.0,true),(.2,0.0,false),(.3,90.0,true),(1.0,180.0,true),(1.1,181.0,true)})
            gapEvents.Add(Event("AimObserved",point.Item1,new JObject{["valid"]=point.Item3,["observationTicks"]=point.Item1*TimeSpan.TicksPerSecond,["maximumGapSeconds"]=.25,["directionX"]=Math.Sin(point.Item2*Math.PI/180),["directionY"]=0,["directionZ"]=Math.Cos(point.Item2*Math.PI/180)}));
        gapEvents.Add(Event("BlockEnded",2));
        var gaps=TaskDataReview.Build(gapEvents)["attempts"][0];
        Check(Math.Abs((double)gaps["aimPathDeg"]-2)<1e-6 && Math.Abs((double)gaps["aimObservedSeconds"]-.2)<1e-8,"Invalid observations and excessive gaps never contribute large false jumps");
        Check((int)gaps["aimObservations"]==6 && (int)gaps["validAimObservations"]==5,"Invalid observations remain visible in coverage");
        string folder=Path.Combine(root,"partial-review");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"events.ndjson"),Event("BlockMetadata",0).ToString(Newtonsoft.Json.Formatting.None)+"\n"+Event("BlockSkipped",1).ToString(Newtonsoft.Json.Formatting.None));
        Check((string)TaskDataReview.Read(folder)["attempts"][0]["status"]=="Started","Trailing valid JSON without newline is excluded");
        string exportRoot=Path.Combine(root,"metric-collection");var collection=new ParticipantCollection(exportRoot);
        collection.SaveParticipant("00123");
        collection.Import(folder,"00123","visit-A",result.ToString());
        string other=Path.Combine(root,"second-participant");Directory.CreateDirectory(other);File.WriteAllText(Path.Combine(other,"events.ndjson"),"");
        collection.Import(other,"=SUM(1,2)","",result.ToString());
        using(var db=new CollectionSqlite(collection.DatabasePath))
        {
            Check(db.Query("SELECT * FROM task_results").Count==4,"SQL has one row per attempt");
            Check(db.Query("SELECT * FROM task_seconds").Count==6 && db.Query("SELECT * FROM task_shots").Count==4,"SQL detail views match shared review");
            Check(db.Query("SELECT typeof(shotsPerSecond) AS kind FROM task_results WHERE blockId='attempt-1'")[0]["kind"]=="real","SQL metrics are numeric");
            Check(db.Query("SELECT DISTINCT visitId FROM task_results WHERE participantCode='00123'")[0]["visitId"]=="visit-A","Results carry the recording's visit ID");
            Check(db.Query("SELECT visitId,sessionId FROM task_results WHERE participantCode='=SUM(1,2)'").All(r=>r["visitId"]==r["sessionId"]),"A recording without a visit ID is its own visit");
        }
        string workbook=collection.ExportResults();
        using(var zip=ZipFile.OpenRead(workbook))
        {
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument Sheet(int number){using(var input=zip.GetEntry("xl/worksheets/sheet"+number+".xml").Open())return XDocument.Load(input);}
            XDocument book;using(var input=zip.GetEntry("xl/workbook.xml").Open())book=XDocument.Load(input);
            Check(String.Join(",",book.Descendants(ns+"sheet").Select(s=>(string)s.Attribute("name")))=="Results,Shots,Timeline,Metric guide,Recordings,Handoff","Results workbook includes the source manifest");
            var results=Sheet(1);
            Check(results.Descendants(ns+"row").Count()==5,"Results has every participant's attempts in one sheet");
            Check(Sheet(2).Descendants(ns+"row").Count()==5 && Sheet(3).Descendants(ns+"row").Count()==7,"Shots and timeline cover every participant");
            Check(results.Descendants(ns+"f").Count()==0 && results.Descendants(ns+"t").Any(t=>t.Value=="=SUM(1,2)"),"Participant text never becomes an Excel formula");
            Check(results.Descendants(ns+"t").Any(t=>t.Value=="00123"),"Excel preserves leading-zero participant code");
            Check(results.Descendants(ns+"t").Any(t=>t.Value=="Visit Id") && results.Descendants(ns+"t").Any(t=>t.Value=="visit-A"),"Results sheet has a visit column");
            Check(!zip.Entries.Any(e=>{using(var reader=new StreamReader(e.Open()))return reader.ReadToEnd().Contains("Private Name");}),"Results export identifies participants by code, never name");
            Check(results.Descendants(ns+"autoFilter").Count()==1 && results.Descendants(ns+"pane").Count()==1,"Workbook filters and frozen headers");
            foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))using(var input=entry.Open())XDocument.Load(input);
        }
        string owner;
        using(var db=new CollectionSqlite(collection.DatabasePath))
            owner=db.Query("SELECT id FROM participants WHERE code=?","00123")[0]["id"];
        Check(collection.PreviewExport("visit",owner,"visit-A","","").StartsWith("2 result rows"),"Visit export preview has only the matching attempts");
        using(var scoped=ZipFile.OpenRead(collection.ExportResults("visit",owner,"visit-A")))
        using(var input=scoped.GetEntry("xl/worksheets/sheet5.xml").Open())
        {
            string manifest=new StreamReader(input).ReadToEnd();
            Check(manifest.Contains("visit-A") && !manifest.Contains("=SUM"),"Scoped manifest identifies only the selected visit");
        }
        // Keep a fictional example for independent spreadsheet-reader inspection.
        string examples=Path.Combine(Environment.CurrentDirectory,"test-results");Directory.CreateDirectory(examples);
        File.Copy(workbook,Path.Combine(examples,"task-metrics-fixture.xlsx"),true);
        Console.WriteLine("PASS: task metrics, SQL views and Excel workbook checks");
    }
    private static void CheckAnalysisScreen(string root)
    {
        var collection=new ParticipantCollection(Path.Combine(root,"analysis-screen"));
        string Add(string name,bool close,params JObject[] events)
        {
            string folder=Path.Combine(collection.Root,name);
            Directory.CreateDirectory(folder);
            File.WriteAllLines(Path.Combine(folder,"events.ndjson"),events.Select(e=>e.ToString(Newtonsoft.Json.Formatting.None)));
            if(close)File.WriteAllText(Path.Combine(folder,"session-summary.json"),"{\"schemaVersion\":\"elts.session-summary.v2\",\"complete\":true,\"error\":null}");
            collection.Import(folder,"7","shared-visit",TaskDataReview.Read(folder).ToString());
            using(var db=new CollectionSqlite(collection.DatabasePath))return db.Query("SELECT id FROM sessions WHERE directory=?",folder).Single()["id"];
        }
        string standard=Add("standard",true,Event("BlockMetadata",0),Event("BlockStarted",1),Event("BlockEnded",301));
        string flexible=Add("flexible",true,Event("BlockDurationChanged",0,new JObject{["developmentTiming"]=true}),Event("BlockMetadata",1),Event("BlockStarted",1),Event("BlockEnded",301));
        string interrupted=Add("interrupted",false,Event("BlockMetadata",0),Event("BlockStarted",1));
        string skipped=Add("skipped",true,Event("BlockSkipped",0));
        using(var db=new CollectionSqlite(collection.DatabasePath))
        {
            Dictionary<string,string> Row(string id)=>db.Query("SELECT analysisEligibility,analysisReason FROM task_results WHERE sessionId=?",id).Single();
            Check(Row(standard)["analysisEligibility"]=="Needs analysis","A standard interval requires independent offline analysis");
            Check(Row(flexible)["analysisReason"].Contains("flexible"),"A timing change before metadata excludes the interval");
            Check(Row(interrupted)["analysisReason"].Contains("closure"),"Incomplete recordings are excluded across attempts");
            Check(Row(skipped)["analysisReason"].Contains("skipped"),"A skipped attempt has its own exclusion reason");
            Check(db.Query("SELECT visitEligibility FROM visit_analysis").Single()["visitEligibility"]=="Partially excluded","Mixed visits retain their eligible-for-review recordings");
            collection.SetVerification(standard,"Passed","check-time","fixture");
            Check(Row(standard)["analysisEligibility"]=="Needs analysis","A passing checksum does not certify schemas, counts or timing");
            collection.SetVerification(standard,"Failed","check-time","fixture");
            Check(Row(standard)["analysisReason"].Contains("checksum") && db.Query("SELECT visitEligibility FROM visit_analysis").Single()["visitEligibility"]=="Excluded","A failed checksum immediately blocks the attempt, recording and visit");
        }
        using(var archive=ZipFile.OpenRead(collection.ExportResults()))
            foreach(int sheet in new[]{1,5})
                using(var xml=new StreamReader(archive.GetEntry("xl/worksheets/sheet"+sheet+".xml").Open()))
                    Check(xml.ReadToEnd().Contains("A recording file failed checksum verification."),"The workbook carries the same exclusion as SQL and Data");
    }
}
