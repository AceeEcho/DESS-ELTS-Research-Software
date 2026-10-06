using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using Elts.Operator;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class CollectionChecks
{
    private static int passed;
    private static void Check(bool value,string message){if(!value)throw new Exception(message);passed++;}
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"ELTS SQL checks "+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            TaskDataChecks.Run(root);
            var collection=new ParticipantCollection(Path.Combine(root,"collection data"));collection.Initialize();
            Check(File.Exists(collection.DatabasePath),"Database created before recording");
            string first=Path.Combine(root,"first run"),second=Path.Combine(root,"second run"),third=Path.Combine(root,"other person");
            foreach(string dir in new[]{first,second,third}){Directory.CreateDirectory(dir);File.WriteAllText(Path.Combine(dir,"events.ndjson"),"{\"eventType\":\"OperatorNote\",\"payload\":{\"text\":\"雪 ' \\\" <script>\"}}\n",new UTF8Encoding(false));}
            File.AppendAllText(Path.Combine(first,"events.ndjson"),"{\"partial\":");
            collection.Import(first,"P-01","Zoë '雪'","{}");collection.Import(second,"p-01","Zoë '雪'","{}");collection.Import(third,"P-02","Other","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM participants").Count==2,"Stable case-insensitive unique profiles");
                Check(db.Query("SELECT * FROM records").Count==3,"Incomplete trailing line excluded");
                Check(db.Query("PRAGMA integrity_check")[0]["integrity_check"]=="ok","SQLite integrity");
            }
            collection.Import(first,"P-01","Zoë '雪'","{}");
            File.AppendAllText(Path.Combine(first,"events.ndjson"),"true}\n");collection.Import(first,"P-01","Zoë '雪'","{}");
            string id;
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM records").Count==4,"Idempotent incremental line completion");
                id=db.Query("SELECT id FROM participants WHERE code=?","P-01")[0]["id"];
            }
            string exported=collection.Export(id),complete=collection.Export();
            using(var db=new CollectionSqlite(exported))
            {
                Check(db.Query("SELECT * FROM participants").Count==1,"Participant export excludes other profiles");
                Check(db.Query("SELECT * FROM sessions").Count==2,"Participant export includes repeated sessions");
                Check(db.Query("SELECT * FROM records").Count==3,"Participant export includes only selected raw rows");
                Check(db.Query("PRAGMA foreign_key_check").Count==0,"Export referential integrity");
                Check(db.Query("SELECT name FROM participants")[0]["name"]=="Zoë '雪'","Unicode and SQL quotes round trip");
            }
            using(var db=new CollectionSqlite(complete))Check(db.Query("SELECT * FROM participants").Count==2,"Whole export opens independently of WAL");
            File.AppendAllText(Path.Combine(first,"events.ndjson"),"bad json\n");
            bool rejected=false;try{collection.Import(first,"P-01","","{}");}catch(JsonReaderException){rejected=true;}
            Check(rejected,"Malformed complete JSON rejected");
            using(var db=new CollectionSqlite(collection.DatabasePath))Check(db.Query("SELECT * FROM records").Count==4,"Malformed batch rollback retains prior rows");
            using(var server=new AdministratorServer(root))
            using(var client=new HttpClient())
            {
                string download=server.RegisterExport(exported);
                Check((int)client.GetAsync(server.Origin+"/api/download/"+download).Result.StatusCode==403,"Download requires session token");
                var response=client.GetAsync(server.Origin+"/api/download/"+download+"?token="+server.Token).Result;
                Check(response.IsSuccessStatusCode,"Registered export downloads");
                Check(response.Content.ReadAsByteArrayAsync().Result.SequenceEqual(File.ReadAllBytes(exported)),"Downloaded bytes match snapshot");
                Check((int)client.GetAsync(server.Origin+"/api/download/not-registered?token="+server.Token).Result.StatusCode==404,"Arbitrary file downloads rejected");
            }
            string runId;
            using(var db=new CollectionSqlite(collection.DatabasePath))runId=db.Query("SELECT id FROM sessions WHERE directory=?",second)[0]["id"];
            bool activeRejected=false;
            try {collection.Remove(id,true,second);} catch(IOException){activeRejected=true;}
            Check(activeRejected,"Participant deletion rejects any active run");
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM sessions").Count==3 && db.Query("SELECT * FROM removed_recordings").Count==0,"Rejected deletion rolls back all changes");
            activeRejected=false;
            try {collection.Remove(runId,false,second);} catch(IOException){activeRejected=true;}
            Check(activeRejected,"Individual active run deletion rejected");
            activeRejected=false;
            try {collection.Remove(id,true,Path.Combine(root,"not indexed yet"),"p-01");} catch(IOException){activeRejected=true;}
            Check(activeRejected,"Active participant protected before a new run's first import");
            collection.Remove(runId,false);
            collection.Import(second,"p-01","Zoë '雪'","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM sessions WHERE participantId=?",id).Count==1,"Individual removal preserves participant's other run and survives refresh");
                Check(db.Query("SELECT * FROM records WHERE sessionId=?",runId).Count==0 && db.Query("SELECT * FROM stream_progress WHERE sessionId=?",runId).Count==0,"Individual removal clears raw rows and offsets");
            }
            collection.Remove(id,true);
            // Reopening the collection must still exclude the old source files,
            // including malformed raw data that no longer belongs to the collection.
            collection=new ParticipantCollection(collection.Root);
            collection.Import(first,"P-01","Zoë '雪'","{}");
            collection.Import(second,"P-01","Zoë '雪'","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
            {
                Check(db.Query("SELECT * FROM participants").Count==1 && db.Query("SELECT * FROM sessions").Count==1,"Participant deletion persists after restart and preserves other participants");
                Check(db.Query("SELECT * FROM records").Count==1 && db.Query("SELECT * FROM stream_progress").Count==1,"Participant deletion clears all dependent rows");
                Check(db.Query("PRAGMA foreign_key_check").Count==0,"Deletion preserves foreign keys");
                Check(db.Query("PRAGMA integrity_check")[0]["integrity_check"]=="ok","Deletion preserves database integrity");
            }
            Check(File.Exists(Path.Combine(first,"events.ndjson")) && File.Exists(exported),"Original files and prior exports retained");
            string fresh=Path.Combine(root,"fresh run");Directory.CreateDirectory(fresh);
            File.WriteAllText(Path.Combine(fresh,"events.ndjson"),"{}\n");
            collection.Import(fresh,"P-01","New recording","{}");
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM participants").Count==2,"Removed participant code can be used for a new recording");
            string lastRun;
            using(var db=new CollectionSqlite(collection.DatabasePath))lastRun=db.Query("SELECT id FROM sessions WHERE directory=?",fresh)[0]["id"];
            collection.Remove(lastRun,false);
            using(var db=new CollectionSqlite(collection.DatabasePath))
                Check(db.Query("SELECT * FROM participants").Count==1,"Removing last run removes its empty profile");
            Console.WriteLine("PASS: "+passed+" collection checks");
        }
        finally {Directory.Delete(root,true);}
    }
}
