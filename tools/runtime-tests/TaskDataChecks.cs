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
        var legacy=TaskDataReview.Build(new[]{Event("BlockMetadata",0),Event("BlockStarted",0),Event("ShotFired",0.5),Event("TargetHit",0.5),Event("ShotFired",1),Event("BlockEnded",2)})["attempts"][0];
        Check((int)legacy["hits"]==1 && (int)legacy["misses"]==1,"Legacy hit matching works");
        Check(legacy["meanShotErrorDeg"].Type==JTokenType.Null && legacy["aimCoveragePercent"].Type==JTokenType.Null,"Legacy geometry remains unavailable");
        Check(legacy["headHits"].Type==JTokenType.Null,"Old hits without a region remain unavailable rather than becoming body hits");
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
        Check(live["misses"].Type==JTokenType.Null,"Incomplete legacy shot cannot be claimed as miss");
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
        collection.Import(folder,"00123","=SUM(1,2)",result.ToString());
        string id;
        using(var db=new CollectionSqlite(collection.DatabasePath))
        {
            id=db.Query("SELECT id FROM participants")[0]["id"];
            Check(db.Query("SELECT * FROM task_results").Count==2,"SQL has one row per attempt");
            Check(db.Query("SELECT * FROM task_seconds").Count==3 && db.Query("SELECT * FROM task_shots").Count==2,"SQL detail views match shared review");
            Check(db.Query("SELECT typeof(shotsPerSecond) AS kind FROM task_results WHERE blockId='attempt-1'")[0]["kind"]=="real","SQL metrics are numeric");
        }
        using(var db=new CollectionSqlite(collection.Export(id)))Check(db.Query("SELECT * FROM task_shots").Count==2,"Participant SQL export retains metric views");
        string workbook=collection.ExportWorkbook(id);
        using(var zip=ZipFile.OpenRead(workbook))
        {
            XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XDocument sheet;using(var input=zip.GetEntry("xl/worksheets/sheet1.xml").Open())sheet=XDocument.Load(input);
            Check(sheet.Descendants(ns+"row").Count()==3,"XLSX preserves summary rows");
            Check(sheet.Descendants(ns+"f").Count()==0 && sheet.Descendants(ns+"t").Any(t=>t.Value=="=SUM(1,2)"),"Participant text never becomes an Excel formula");
            Check(sheet.Descendants(ns+"t").Any(t=>t.Value=="00123"),"Excel preserves leading-zero participant code");
            Check(sheet.Descendants(ns+"autoFilter").Count()==1 && sheet.Descendants(ns+"pane").Count()==1,"Workbook filters and frozen headers");
            foreach(var entry in zip.Entries.Where(e=>e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))using(var input=entry.Open())XDocument.Load(input);
        }
        // Keep a fictional example for independent spreadsheet-reader inspection.
        string examples=Path.Combine(Environment.CurrentDirectory,"test-results");Directory.CreateDirectory(examples);
        File.Copy(workbook,Path.Combine(examples,"task-metrics-fixture.xlsx"),true);
        Console.WriteLine("PASS: task metrics, SQL views and Excel workbook checks");
    }
}
