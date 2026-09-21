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
            Console.WriteLine("PASS: "+passed+" collection checks");
        }
        finally {Directory.Delete(root,true);}
    }
}
