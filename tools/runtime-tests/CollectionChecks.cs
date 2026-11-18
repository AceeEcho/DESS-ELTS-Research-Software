using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Elts.Operator;

internal static class CollectionChecks
{
    private static int passed;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;}
    private static bool Throws<T>(Action action) where T:Exception {try{action();}catch(T){return true;}return false;}
    public static void Main(string[] args)
    {
        if(args.SequenceEqual(new[]{"--benchmark-review"}))
        {
            ReviewPollingBenchmark.Run();
            return;
        }
        string root=Path.Combine(Path.GetTempPath(),"ELTS SQL checks "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            TaskDataChecks.Run(root);
            DatasetRemovalChecks.Run(root,Check);
            // A version-3 database copied every raw line. Initialization drops that copy.
            string legacyRoot=Path.Combine(root,"legacy collection");Directory.CreateDirectory(legacyRoot);
            using(var db=new CollectionSqlite(Path.Combine(legacyRoot,"collection.sqlite")))
            {
                // A version-4 sessions table predates the visit column.
                db.Execute("CREATE TABLE participants (id TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE COLLATE NOCASE, name TEXT NOT NULL, createdUtc TEXT NOT NULL)");
                db.Execute("CREATE TABLE sessions (id TEXT PRIMARY KEY, participantId TEXT NOT NULL REFERENCES participants(id), directory TEXT NOT NULL UNIQUE, modifiedUtc TEXT NOT NULL, finalized INTEGER NOT NULL DEFAULT 0, review TEXT NOT NULL DEFAULT '{}')");
                db.Execute("CREATE TABLE records (sessionId TEXT, stream TEXT, offset INTEGER, json TEXT)");db.Execute("CREATE TABLE stream_progress (sessionId TEXT, stream TEXT, offset INTEGER)");
                db.Execute("INSERT INTO participants VALUES ('legacy-p','007','Fictional old name','old-date')");
                db.Execute("INSERT INTO sessions(id,participantId,directory,modifiedUtc) VALUES ('legacy-s','legacy-p','old-run','old-date')");
                db.Execute("PRAGMA user_version=3");
            }
            new ParticipantCollection(legacyRoot).Initialize();
            using(var db=new CollectionSqlite(Path.Combine(legacyRoot,"collection.sqlite")))
            {
                Check(db.Query("SELECT name FROM sqlite_master WHERE name IN ('records','stream_progress')").Count==0,"Raw line copy removed from older databases");
                Check(db.Query("PRAGMA user_version")[0]["user_version"]==ParticipantCollection.SchemaVersion.ToString(),"Database upgraded to current version");
                Check(db.Query("SELECT id FROM sessions WHERE participantId='legacy-p'").Single()["id"]=="legacy-s","Migration retains existing participant and recording identities");
                Check(db.Query("SELECT visitId FROM sessions").Single()["visitId"]=="legacy-s","A pre-visit recording can be exported as its own visit");
                Check(db.Query("SELECT name FROM pragma_table_info('participants') WHERE name='name'").Count==0,"Migration removes old names from the schema");
                var schema=db.Query("PRAGMA schema_version")[0]["schema_version"];
                new ParticipantCollection(legacyRoot).Initialize();
                Check(db.Query("PRAGMA schema_version")[0]["schema_version"]==schema,"Repeated initialization performs no DDL");
                Check(db.Query("SELECT name FROM pragma_table_info('sessions') WHERE name='visitId'").Count==1,"Older sessions table gains the visit column");
            }

            string incremental=Path.Combine(root,"incremental"),incRun=Path.Combine(incremental,"sample");
            Directory.CreateDirectory(incRun);
            string eventsPath=Path.Combine(incRun,"events.ndjson");
            File.WriteAllText(eventsPath,"{}\n");
            var index=new ParticipantCollection(incremental);
            Check(RecordingIndex.Refresh(index).Indexed==1,"Initial refresh indexes a new run");
            var unchanged=RecordingIndex.Refresh(index);
            Check(unchanged.Indexed==0 && unchanged.Unchanged==1,"Unchanged refresh skips the review reader");
            File.AppendAllText(eventsPath,"{}\n");
            Check(RecordingIndex.Refresh(index).Indexed==1 && RecordingIndex.Refresh(index,true).Indexed==1,"Source changes and forced rebuild reindex");
            var hashes=new Newtonsoft.Json.Linq.JObject();
            foreach(string name in new[]{"samples.ndjson","events.ndjson","targets.ndjson"})
            {
                string file=Path.Combine(incRun,name);
                if(!File.Exists(file))File.WriteAllText(file,"");
                hashes[name]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            }
            string supplementary=Path.Combine(incRun,"device.csv");File.WriteAllText(supplementary,"time,reading\n");
            var extra=new Newtonsoft.Json.Linq.JObject{["device.csv"]=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(supplementary))).ToLowerInvariant()};
            string summary=Path.Combine(incRun,"session-summary.json");
            File.WriteAllText(summary,new Newtonsoft.Json.Linq.JObject{
                ["schemaVersion"]="elts.session-summary.v2",["complete"]=true,["checksumsSha256"]=hashes,
                ["supplementaryChecksumsSha256"]=extra}.ToString());
            RecordingIndex.Refresh(index);
            string checkedSession;
            using(var db=new CollectionSqlite(index.DatabasePath))checkedSession=db.Query("SELECT id FROM sessions").Single()["id"];
            Check(RecordingIndex.VerifyRecording(index,checkedSession).StartsWith("Verified 4 file checksums"),"Explicit integrity check compares every stream and supplementary file");
            using(var db=new CollectionSqlite(index.DatabasePath))
                Check(db.Query("SELECT integrityStatus,integrityCheckedUtc FROM sessions").Single()["integrityStatus"]=="Passed","Successful check is recorded independently of closure");
            File.AppendAllText(supplementary,"0,1\n");
            RecordingIndex.Refresh(index);
            using(var db=new CollectionSqlite(index.DatabasePath))
                Check(db.Query("SELECT integrityStatus FROM sessions").Single()["integrityStatus"]=="Unchecked","An edited supplementary file invalidates verification");
            File.AppendAllText(eventsPath,"{}\n");
            Check(Throws<IOException>(()=>RecordingIndex.VerifyRecording(index,checkedSession)),"Integrity check detects a changed stream after closure");
            using(var db=new CollectionSqlite(index.DatabasePath))
                Check(db.Query("SELECT integrityStatus FROM sessions").Single()["integrityStatus"]=="Failed","Failed check is recorded even for a closed session");
            File.AppendAllText(eventsPath,"{}\n");
            RecordingIndex.Refresh(index);
            using(var db=new CollectionSqlite(index.DatabasePath))
                Check(db.Query("SELECT integrityStatus FROM sessions").Single()["integrityStatus"]=="Unchecked","Refresh invalidates a check when indexed inputs change");
            string future=Path.Combine(root,"future");Directory.CreateDirectory(future);
            using(var db=new CollectionSqlite(Path.Combine(future,"collection.sqlite")))db.Execute("PRAGMA user_version=999");
            Check(Throws<IOException>(()=>new ParticipantCollection(future).Initialize()),"Unknown future database is refused");

            var collection=new ParticipantCollection(Path.Combine(root,"collection data"));collection.Initialize();
            Check(File.Exists(collection.DatabasePath),"Database created before recording");
            string first=Path.Combine(root,"first run"),second=Path.Combine(root,"second run"),third=Path.Combine(root,"other person");
            const string line="{\"eventType\":\"OperatorNote\",\"payload\":{\"text\":\"雪 ' \\\" <script>\"}}";
            foreach(string dir in new[]{first,second,third}){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"events.ndjson"),line+"\n",new UTF8Encoding(false));}
            File.AppendAllText(Path.Combine(first,"events.ndjson"),"{\"partial\":");
            collection.SaveParticipant("P-01");
            collection.Import(first,"P-01","","{}");collection.Import(second,"p-01","","{}");collection.Import(third,"P-02","","{}");
            collection.SaveParticipant("p-01");
            string id,firstSession;
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM participants").Count==2,"Stable case-insensitive unique profiles");
                Check(db.Query("SELECT name FROM pragma_table_info('participants') WHERE name='name'").Count==0,"Collection contains no participant name field");
                Check(db.Query("SELECT name FROM sqlite_master WHERE name IN ('records','stream_progress')").Count==0,"Raw lines are never copied into the database");
                Check(db.Query("PRAGMA integrity_check")[0]["integrity_check"]=="ok","SQLite integrity");
                id=db.Query("SELECT id FROM participants WHERE code=?","P-01")[0]["id"];
                firstSession=db.Query("SELECT id FROM sessions WHERE directory=?",first)[0]["id"];
            }

            // Original records are read in place, complete lines only.
            var rows=collection.Records(firstSession,"events.ndjson",0);
            Check(rows.Count==1 && rows[0]["json"]==line && rows[0]["offset"]=="0","Incomplete trailing line excluded");
            File.AppendAllText(Path.Combine(first,"events.ndjson"),"true}\n");
            rows=collection.Records(firstSession,"events.ndjson",0);
            Check(rows.Count==2 && rows[1]["offset"]==Encoding.UTF8.GetByteCount(line+"\n").ToString() && rows[1]["json"]=="{\"partial\":true}","Completed line appears with its byte offset");
            File.WriteAllText(Path.Combine(first,"many.ndjson"),"");
            var paged=Path.Combine(first,"samples.ndjson");File.WriteAllText(paged,String.Concat(Enumerable.Range(0,120).Select(i=>"{\"n\":"+i+"}\n")));
            Check(collection.Records(firstSession,"samples.ndjson",1).Count==50 && collection.Records(firstSession,"samples.ndjson",1)[0]["json"]=="{\"n\":50}","Second page starts at record 51");
            Check(collection.Records(firstSession,"samples.ndjson",2).Count==20 && collection.Records(firstSession,"samples.ndjson",3).Count==0,"Final partial page and empty page");
            File.WriteAllText(Path.Combine(first,"participant.json"),"{\"participantId\":\"P-01\"}");
            Check(collection.Records(firstSession,"participant.json",0).Count==1 && collection.Records(firstSession,"participant.json",1).Count==0,"JSON file is one record");
            Check(collection.Records(firstSession,"targets.ndjson",0).Count==0,"Missing stream has no records");
            Check(Throws<IOException>(()=>collection.Records(firstSession,"..\\other person\\events.ndjson",0)) && Throws<IOException>(()=>collection.Records(firstSession,"notes.txt",0))
                && Throws<IOException>(()=>collection.Records(firstSession,"many.ndjson",0)),"Only known files directly inside the recording are readable");

            string exported=collection.ExportResults();
            Check(File.Exists(exported) && Path.GetExtension(exported)==".xlsx","Results export is one workbook");
            using(var server=new AdministratorServer(root))
            using(var client=new HttpClient())
            {
                string download=server.RegisterExport(exported);
                server.PublishCollection("{\"revision\":8,\"details\":{\"rows\":[]}}");
                Check((int)client.GetAsync(server.Origin+"/api/data").Result.StatusCode==403,"Historical data requires the station token");
                using(var details=new HttpRequestMessage(HttpMethod.Get,server.Origin+"/api/data"))
                {
                    details.Headers.Add("X-Elts-Token",server.Token);
                    Check(client.Send(details).Content.ReadAsStringAsync().Result.Contains("\"revision\":8"),"Historical payload is published independently");
                }
                Check((int)client.GetAsync(server.Origin+"/api/download/"+download).Result.StatusCode==403,"Download requires session token");
                var response=client.GetAsync(server.Origin+"/api/download/"+download+"?token="+server.Token).Result;
                Check(response.IsSuccessStatusCode,"Registered export downloads");
                Check(response.Content.ReadAsByteArrayAsync().Result.SequenceEqual(File.ReadAllBytes(exported)),"Downloaded bytes match export");
                Check((int)client.GetAsync(server.Origin+"/api/download/not-registered?token="+server.Token).Result.StatusCode==404,"Arbitrary file downloads rejected");
            }

            // A profile saved at intake survives refreshes of other recordings.
            collection.SaveParticipant("P-03");collection.Import(third,"P-02","","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))Check(db.Query("SELECT * FROM participants WHERE code='P-03'").Count==1,"Intake profile kept before its first import");

            string runId;
            using(var db=new CollectionSqlite(collection.DatabasePath))runId=db.Query("SELECT id FROM sessions WHERE directory=?",second)[0]["id"];
            Check(Throws<IOException>(()=>collection.Remove(id,true,second)),"Participant deletion rejects any active run");
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM sessions").Count==3 && db.Query("SELECT * FROM removed_recordings").Count==0,"Rejected deletion rolls back all changes");
            Check(Throws<IOException>(()=>collection.Remove(runId,false,second)),"Individual active run deletion rejected");
            Check(Throws<IOException>(()=>collection.Remove(id,true,Path.Combine(root,"not indexed yet"),"p-01")),"Active participant protected before a new run's first import");
            collection.Remove(runId,false);
            collection.Import(second,"p-01","","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM sessions WHERE participantId=?",id).Count==1,"Individual removal preserves participant's other run and survives refresh");
            collection.Remove(id,true);
            // Reopening the collection must still exclude the old source folders.
            collection=new ParticipantCollection(collection.Root);
            collection.Import(first,"P-01","","{}");
            collection.Import(second,"P-01","","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM participants WHERE code='P-01'").Count==0 && db.Query("SELECT * FROM sessions").Count==1,"Participant deletion persists after restart and preserves other participants");
                Check(db.Query("PRAGMA foreign_key_check").Count==0,"Deletion preserves foreign keys");
                Check(db.Query("PRAGMA integrity_check")[0]["integrity_check"]=="ok","Deletion preserves database integrity");
            }
            Check(File.Exists(Path.Combine(first,"events.ndjson")) && File.Exists(exported),"Original files and prior exports retained");
            string fresh=Path.Combine(root,"fresh run");Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh,"events.ndjson"),"{}\n");
            collection.Import(fresh,"P-01","","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM participants WHERE code='P-01'").Count==1,"Removed participant code can be used for a new recording");
            string lastRun;
            using(var db=new CollectionSqlite(collection.DatabasePath))lastRun=db.Query("SELECT id FROM sessions WHERE directory=?",fresh)[0]["id"];
            collection.Remove(lastRun,false);
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM participants WHERE code='P-01'").Count==0,"Removing last run removes its empty profile");
            Console.WriteLine("PASS: "+passed+" collection checks");
        }
        finally {Directory.Delete(root,true);}
    }
}
