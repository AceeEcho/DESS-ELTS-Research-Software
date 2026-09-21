#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// <summary>Rebuildable SQL copy of original recordings. Only complete NDJSON lines
    /// enter a transaction; offsets commit together with rows so retries are idempotent.</summary>
    internal sealed class ParticipantCollection
    {
        public readonly string Root;
        public string DatabasePath=>Path.Combine(Root,"collection.sqlite");
        private static readonly string[] Streams={"events.ndjson","samples.ndjson","targets.ndjson"};
        public ParticipantCollection(string root){Root=Path.GetFullPath(root);}
        private CollectionSqlite Open()=>new CollectionSqlite(DatabasePath);
        public void Initialize()
        {
            Directory.CreateDirectory(Root);
            if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Collection folder cannot be a link.");
            if(File.Exists(DatabasePath))CheckFile(DatabasePath);
            using(var db=Open())
            {
                db.Execute("PRAGMA journal_mode=WAL");db.Execute("PRAGMA synchronous=FULL");
                Schema(db);
            }
        }
        private static void Schema(CollectionSqlite db)
        {
            db.Execute("CREATE TABLE IF NOT EXISTS participants (id TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE COLLATE NOCASE, name TEXT NOT NULL, createdUtc TEXT NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, participantId TEXT NOT NULL REFERENCES participants(id), directory TEXT NOT NULL UNIQUE, modifiedUtc TEXT NOT NULL, finalized INTEGER NOT NULL DEFAULT 0, review TEXT NOT NULL DEFAULT '{}')");
            db.Execute("CREATE TABLE IF NOT EXISTS records (sessionId TEXT NOT NULL REFERENCES sessions(id), stream TEXT NOT NULL, offset INTEGER NOT NULL, json TEXT NOT NULL, PRIMARY KEY(sessionId,stream,offset))");
            db.Execute("CREATE TABLE IF NOT EXISTS stream_progress (sessionId TEXT NOT NULL REFERENCES sessions(id), stream TEXT NOT NULL, offset INTEGER NOT NULL, PRIMARY KEY(sessionId,stream))");
            db.Execute("CREATE INDEX IF NOT EXISTS sessions_participant ON sessions(participantId)");
            // Named, typed columns expose the shared derived review without
            // requiring researchers to navigate JSON or duplicate calculations.
            CreateMetricView(db,"task_results",TaskDataReview.SummaryColumns,"");
            CreateMetricView(db,"task_seconds",TaskDataReview.SecondColumns,"seconds");
            CreateMetricView(db,"task_shots",TaskDataReview.ShotColumns,"shotDetails");
            db.Execute("PRAGMA user_version=2");
        }
        private static void CreateMetricView(CollectionSqlite db,string name,string[] columns,string detail)
        {
            string source=detail.Length==0?"a":"d";
            string fields=String.Join(",",columns.Select(c=>"json_extract("+source+".value,'$."+c+"') AS "+c));
            string context=detail.Length==0?"":",json_extract(a.value,'$.blockId') AS blockId,json_extract(a.value,'$.condition') AS condition";
            db.Execute("CREATE VIEW IF NOT EXISTS "+name+" AS SELECT p.code AS participantCode,p.name AS participantName,s.id AS sessionId,s.finalized AS recordingFinalized"+context+","+fields+
                " FROM sessions s JOIN participants p ON p.id=s.participantId,json_each(s.review,'$.attempts') a"+
                (detail.Length==0?"":",json_each(a.value,'$."+detail+"') d"));
        }
        public string ExportWorkbook(string participant)
        {
            Guid.ParseExact(participant,"N");
            string exports=Path.Combine(Root,"exports");Directory.CreateDirectory(exports);
            if((File.GetAttributes(exports)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export folder cannot be a link.");
            string destination=Path.Combine(exports,"participant-"+participant+"-"+Guid.NewGuid().ToString("N")+".xlsx");
            using(var db=Open())
            {
                if(db.Query("SELECT id FROM participants WHERE id=?",participant).Count!=1)throw new IOException("Choose an existing participant.");
                db.Execute("BEGIN");
                try
                {
                    var sheets=new List<CollectionWorkbook.Sheet>();
                    string[] context={"participantCode","participantName","sessionId","recordingFinalized"};
                    foreach(var spec in new[]{("Tasks","task_results",TaskDataReview.SummaryColumns),("Timeline","task_seconds",new[]{"blockId","condition"}.Concat(TaskDataReview.SecondColumns).ToArray()),("Shots","task_shots",new[]{"blockId","condition"}.Concat(TaskDataReview.ShotColumns).ToArray())})
                    {
                        var columns=context.Concat(spec.Item3).ToArray();
                        var rows=db.Query("SELECT * FROM "+spec.Item2+" WHERE sessionId IN (SELECT id FROM sessions WHERE participantId=?)",participant);
                        sheets.Add(new CollectionWorkbook.Sheet(spec.Item1,columns,rows.Select(r=>columns.Select(c=>r[c]).ToArray()).ToArray()));
                    }
                    sheets.Add(new CollectionWorkbook.Sheet("Metric guide",new[]{"Metric","Unit","Definition"},TaskDataReview.Definitions));
                    CollectionWorkbook.Write(destination,sheets);
                    db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
            return destination;
        }
        public void Import(string directory,string code,string name,string review)
        {
            Initialize();directory=Path.GetFullPath(directory);
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recording folder cannot be a link.");
            using(var db=Open())
            {
                db.Execute("INSERT OR IGNORE INTO participants VALUES (?,?,?,?)",Guid.NewGuid().ToString("N"),code,name,DateTime.UtcNow.ToString("O"));
                string participant=db.Query("SELECT id FROM participants WHERE code=?",code)[0]["id"];
                if(name.Length>0)db.Execute("UPDATE participants SET name=? WHERE id=?",name,participant);
                db.Execute("INSERT OR IGNORE INTO sessions(id,participantId,directory,modifiedUtc) VALUES(?,?,?,?)",Guid.NewGuid().ToString("N"),participant,directory,DateTime.UtcNow.ToString("O"));
                string session=db.Query("SELECT id FROM sessions WHERE directory=?",directory)[0]["id"];
                db.Execute("UPDATE sessions SET participantId=? WHERE id=?",participant,session);
                db.Execute("DELETE FROM participants WHERE NOT EXISTS (SELECT 1 FROM sessions WHERE participantId=participants.id)");
                foreach(string stream in Streams)ImportStream(db,session,directory,stream);
                // Supplemental files retain complete provenance/checkpoint/summary JSON too.
                foreach(string path in Directory.GetFiles(directory,"*.json"))
                {
                    if(Path.GetFileName(path).Contains(".pending"))continue;
                    CheckFile(path);string json=File.ReadAllText(path);JToken.Parse(json);
                    db.Execute("INSERT OR REPLACE INTO records VALUES(?,?,0,?)",session,Path.GetFileName(path),json);
                }
                db.Execute("UPDATE sessions SET modifiedUtc=?,finalized=?,review=? WHERE id=?",Directory.GetLastWriteTimeUtc(directory).ToString("O"),File.Exists(Path.Combine(directory,"session-summary.json"))?"1":"0",review,session);
            }
        }
        private static void CheckFile(string path)
        {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Data import cannot follow linked files.");}
        private static void ImportStream(CollectionSqlite db,string session,string directory,string name)
        {
            string path=Path.Combine(directory,name);if(!File.Exists(path))return;CheckFile(path);
            var progress=db.Query("SELECT offset FROM stream_progress WHERE sessionId=? AND stream=?",session,name);
            long offset=progress.Count==0?0:long.Parse(progress[0]["offset"],CultureInfo.InvariantCulture);
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                long limit=input.Length;if(limit<offset)throw new IOException("A previously imported raw stream was shortened: "+name);
                input.Position=offset;var bytes=new List<byte>();int count=0;
                db.Execute("BEGIN IMMEDIATE");
                try
                {
                    // File size is captured once. A trailing partial line is retried next sync.
                    while(input.Position<limit)
                    {
                        int next=input.ReadByte();if(next<0)break;
                        if(next!=10){bytes.Add((byte)next);if(bytes.Count>16777216)throw new IOException("Raw record exceeds 16 MB.");continue;}
                        string json=new UTF8Encoding(false,true).GetString(bytes.ToArray()).TrimEnd('\r');bytes.Clear();
                        JToken.Parse(json);
                        db.Execute("INSERT INTO records VALUES(?,?,?,?)",session,name,offset.ToString(CultureInfo.InvariantCulture),json);
                        offset=input.Position;count++;
                        if(count%500==0)
                        {
                            SaveOffset(db,session,name,offset);db.Execute("COMMIT");db.Execute("BEGIN IMMEDIATE");
                        }
                    }
                    SaveOffset(db,session,name,offset);db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
        }
        private static void SaveOffset(CollectionSqlite db,string session,string stream,long offset)=>
            db.Execute("INSERT OR REPLACE INTO stream_progress VALUES(?,?,?)",session,stream,offset.ToString(CultureInfo.InvariantCulture));
        public object Profiles()
        {
            using(var db=Open())return db.Query("SELECT p.id,p.code,p.name,COUNT(s.id) AS sessions,MAX(s.modifiedUtc) AS modifiedUtc FROM participants p LEFT JOIN sessions s ON s.participantId=p.id GROUP BY p.id ORDER BY modifiedUtc DESC");
        }
        public object Profile(string id)
        {
            using(var db=Open())return new { participant=db.Query("SELECT * FROM participants WHERE id=?",id).FirstOrDefault(),
                sessions=db.Query("SELECT id,modifiedUtc,finalized,review FROM sessions WHERE participantId=? ORDER BY modifiedUtc DESC",id) };
        }
        public string SessionDirectory(string id)
        {
            using(var db=Open())return db.Query("SELECT directory FROM sessions WHERE id=?",id).FirstOrDefault()?["directory"]??throw new IOException("Select a saved session.");
        }
        public object Records(string session,string stream,int page)
        {
            using(var db=Open())return db.Query("SELECT offset,json FROM records WHERE sessionId=? AND stream=? ORDER BY offset LIMIT 50 OFFSET ?",session,stream,(Math.Max(0,page)*50L).ToString(CultureInfo.InvariantCulture));
        }
        public string Export(string participant="")
        {
            string exports=Path.Combine(Root,"exports");Directory.CreateDirectory(exports);
            if((File.GetAttributes(exports)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export folder cannot be a link.");
            string destination=Path.Combine(exports,(participant.Length==0?"collection":"participant-"+Guid.ParseExact(participant,"N").ToString("N"))+"-"+Guid.NewGuid().ToString("N")+".sqlite");
            string pending=destination+".pending";
            try
            {
                using(var db=Open())
                {
                    if(participant.Length==0)db.Backup(pending);
                    else
                    {
                        if(db.Query("SELECT id FROM participants WHERE id=?",participant).Count!=1)throw new IOException("Choose an existing participant.");
                        using(var output=new CollectionSqlite(pending))Schema(output);
                        db.Execute("ATTACH DATABASE ? AS exported",pending);
                        try
                        {
                            db.Execute("BEGIN");
                            db.Execute("INSERT INTO exported.participants SELECT * FROM main.participants WHERE id=?",participant);
                            db.Execute("INSERT INTO exported.sessions SELECT * FROM main.sessions WHERE participantId=?",participant);
                            db.Execute("INSERT INTO exported.records SELECT r.* FROM main.records r JOIN main.sessions s ON s.id=r.sessionId WHERE s.participantId=?",participant);
                            db.Execute("INSERT INTO exported.stream_progress SELECT r.* FROM main.stream_progress r JOIN main.sessions s ON s.id=r.sessionId WHERE s.participantId=?",participant);
                            db.Execute("COMMIT");
                        }
                        catch {db.Execute("ROLLBACK");throw;}
                        finally {db.Execute("DETACH DATABASE exported");}
                    }
                }
                File.Move(pending,destination);return destination;
            }
            catch {if(File.Exists(pending))File.Delete(pending);throw;}
        }
    }
}
