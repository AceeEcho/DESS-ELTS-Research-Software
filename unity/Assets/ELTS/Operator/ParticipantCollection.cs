#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Elts.Operator
{
    /// <summary>Small local index of participants, recordings and their derived task
    /// results. Raw recording files stay authoritative and are read in place; the
    /// database never copies their lines. Participant names live only here.</summary>
    internal sealed class ParticipantCollection
    {
        public readonly string Root;
        public string DatabasePath=>Path.Combine(Root,"collection.sqlite");
        public const int RecordsPerPage=50;
        private const int MaximumRecordBytes=16777216;
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
            db.Execute("CREATE INDEX IF NOT EXISTS sessions_participant ON sessions(participantId)");
            // Remember excluded source folders so Refresh cannot resurrect deleted runs.
            // Original recordings remain intact for logging integrity and recovery.
            db.Execute("CREATE TABLE IF NOT EXISTS removed_recordings (directory TEXT PRIMARY KEY COLLATE NOCASE)");
            // Version 3 and earlier copied every raw line into the database, making it
            // larger than the recordings themselves. Drop that copy and reclaim the space.
            if(db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('records','stream_progress')").Count>0)
            {
                db.Execute("DROP TABLE IF EXISTS records");db.Execute("DROP TABLE IF EXISTS stream_progress");db.Execute("VACUUM");
            }
            // Named, typed columns expose the shared derived review without
            // requiring researchers to navigate JSON or duplicate calculations.
            CreateMetricView(db,"task_results",TaskDataReview.SummaryColumns,"");
            CreateMetricView(db,"task_seconds",TaskDataReview.SecondColumns,"seconds");
            CreateMetricView(db,"task_shots",TaskDataReview.ShotColumns,"shotDetails");
            db.Execute("PRAGMA user_version=4");
        }
        private static void CreateMetricView(CollectionSqlite db,string name,string[] columns,string detail)
        {
            string source=detail.Length==0?"a":"d";
            string fields=String.Join(",",columns.Select(c=>"json_extract("+source+".value,'$."+c+"') AS "+c));
            string context=detail.Length==0?"":",json_extract(a.value,'$.blockId') AS blockId,json_extract(a.value,'$.condition') AS condition";
            // Recreate so a changed metric column list always reaches existing databases.
            db.Execute("DROP VIEW IF EXISTS "+name);
            db.Execute("CREATE VIEW "+name+" AS SELECT p.code AS participantCode,p.name AS participantName,s.id AS sessionId,s.finalized AS recordingFinalized"+context+","+fields+
                " FROM sessions s JOIN participants p ON p.id=s.participantId,json_each(s.review,'$.attempts') a"+
                (detail.Length==0?"":",json_each(a.value,'$."+detail+"') d"));
        }
        public string ExportsFolder()
        {
            string exports=Path.Combine(Root,"exports");Directory.CreateDirectory(exports);
            if((File.GetAttributes(exports)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export folder cannot be a link.");
            return exports;
        }
        /// <summary>One workbook for every participant: one Results row per task attempt,
        /// one Shots row per accepted shot, per-second Timeline rows and the metric guide.
        /// Participant codes identify rows; names stay in the local collection.</summary>
        public string ExportResults()
        {
            string destination=Path.Combine(ExportsFolder(),"elts-results-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".xlsx");
            using(var db=Open())
            {
                // One read transaction keeps every sheet from the same snapshot.
                db.Execute("BEGIN");
                try
                {
                    var sheets=new List<CollectionWorkbook.Sheet>();
                    string[] context={"participantCode","sessionId","recordingFinalized"};
                    foreach(var spec in new[]{("Results","task_results",TaskDataReview.SummaryColumns),("Shots","task_shots",new[]{"blockId","condition"}.Concat(TaskDataReview.ShotColumns).ToArray()),("Timeline","task_seconds",new[]{"blockId","condition"}.Concat(TaskDataReview.SecondColumns).ToArray())})
                    {
                        var columns=context.Concat(spec.Item3).ToArray();
                        var rows=db.Query("SELECT * FROM "+spec.Item2+" ORDER BY participantCode COLLATE NOCASE,sessionId");
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
        /// <summary>Create or update a profile at intake. A blank name keeps an earlier one.</summary>
        public void SaveParticipant(string code,string name)
        {
            Initialize();
            using(var db=Open())
            {
                db.Execute("INSERT OR IGNORE INTO participants VALUES (?,?,?,?)",Guid.NewGuid().ToString("N"),code,"",DateTime.UtcNow.ToString("O"));
                if(name.Length>0)db.Execute("UPDATE participants SET name=? WHERE code=?",name,code);
            }
        }
        /// <summary>Index one recording folder and store its derived review. Raw files are only read.</summary>
        public void Import(string directory,string code,string review)
        {
            Initialize();directory=Path.GetFullPath(directory);
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recording folder cannot be a link.");
            using(var db=Open())
            {
                if(db.Query("SELECT directory FROM removed_recordings WHERE directory=?",directory).Count>0)return;
                db.Execute("BEGIN IMMEDIATE");
                try
                {
                    db.Execute("INSERT OR IGNORE INTO participants VALUES (?,?,?,?)",Guid.NewGuid().ToString("N"),code,"",DateTime.UtcNow.ToString("O"));
                    string participant=db.Query("SELECT id FROM participants WHERE code=?",code)[0]["id"];
                    db.Execute("INSERT OR IGNORE INTO sessions(id,participantId,directory,modifiedUtc) VALUES(?,?,?,?)",Guid.NewGuid().ToString("N"),participant,directory,DateTime.UtcNow.ToString("O"));
                    var session=db.Query("SELECT id,participantId FROM sessions WHERE directory=?",directory)[0];
                    db.Execute("UPDATE sessions SET participantId=?,modifiedUtc=?,finalized=?,review=? WHERE id=?",participant,Directory.GetLastWriteTimeUtc(directory).ToString("O"),
                        File.Exists(Path.Combine(directory,"session-summary.json"))?"1":"0",review,session["id"]);
                    // Only a reassigned run can leave its previous profile empty. Profiles saved
                    // at intake but not yet imported must survive other recordings' refreshes.
                    if(session["participantId"]!=participant)
                        db.Execute("DELETE FROM participants WHERE id=? AND NOT EXISTS (SELECT 1 FROM sessions WHERE participantId=participants.id)",session["participantId"]);
                    db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
        }
        private static void CheckFile(string path)
        {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Data import cannot follow linked files.");}
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
        public bool IsRemoved(string directory)
        {
            using(var db=Open())return db.Query("SELECT directory FROM removed_recordings WHERE directory=?",Path.GetFullPath(directory)).Count>0;
        }
        /// <summary>Remove a participant (including their name) or one whole run from the
        /// collection. Import exclusions commit together; raw files are not rewritten.</summary>
        public void Remove(string id,bool participant,string activeDirectory="",string activeParticipantCode="")
        {
            Guid.ParseExact(id,"N");
            using(var db=Open())
            {
                db.Execute("BEGIN IMMEDIATE");
                try
                {
                    var runs=db.Query(participant?"SELECT id,directory,participantId FROM sessions WHERE participantId=?":"SELECT id,directory,participantId FROM sessions WHERE id=?",id);
                    if(participant?db.Query("SELECT id FROM participants WHERE id=?",id).Count==0:runs.Count==0)
                        throw new IOException("Select existing participant data or a saved run.");
                    // Intake may have opened a new raw recording before its first
                    // SQL import. Protect that participant by code as well as path.
                    if(participant && activeParticipantCode.Length>0 && db.Query("SELECT id FROM participants WHERE id=? AND code=?",id,activeParticipantCode).Count>0)
                        throw new IOException("Finish or abort the active participant session before removing its data.");
                    if(activeDirectory.Length>0 && runs.Any(run=>String.Equals(Path.GetFullPath(run["directory"]),Path.GetFullPath(activeDirectory),StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("Finish or abort the active participant session before removing its data.");
                    foreach(var run in runs)
                    {
                        db.Execute("INSERT OR IGNORE INTO removed_recordings VALUES(?)",run["directory"]);
                        db.Execute("DELETE FROM sessions WHERE id=?",run["id"]);
                    }
                    string owner=participant?id:runs[0]["participantId"];
                    db.Execute("DELETE FROM participants WHERE id=? AND NOT EXISTS (SELECT 1 FROM sessions WHERE participantId=participants.id)",owner);
                    db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
        }
        /// <summary>One page of original records, read directly from the recording.
        /// NDJSON pages contain complete lines only, keyed by byte offset; a JSON file
        /// is one record at offset zero. Only files directly inside the run are readable.</summary>
        public List<Dictionary<string,string>> Records(string session,string file,int page)
        {
            string directory=SessionDirectory(session);
            if(file.Length==0 || Path.GetFileName(file)!=file || file.Contains(".pending") || !(Streams.Contains(file) || file.EndsWith(".json",StringComparison.Ordinal)))
                throw new IOException("Choose a file from this recording.");
            string path=Path.Combine(directory,file);
            var rows=new List<Dictionary<string,string>>();
            if(!File.Exists(path))return rows;
            CheckFile(path);
            var utf8=new UTF8Encoding(false,true);
            if(!file.EndsWith(".ndjson",StringComparison.Ordinal))
            {
                if(page<=0)rows.Add(new Dictionary<string,string>{["offset"]="0",["json"]=File.ReadAllText(path,utf8)});
                return rows;
            }
            long skip=Math.Max(0,page)*(long)RecordsPerPage,index=0,lineStart=0,position=0;
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                // Snapshot the length once. A trailing partial line is still being written.
                long limit=input.Length;var chunk=new byte[65536];var line=new MemoryStream();
                while(position<limit && rows.Count<RecordsPerPage)
                {
                    int count=input.Read(chunk,0,(int)Math.Min(chunk.Length,limit-position));if(count<=0)break;
                    for(int i=0;i<count && rows.Count<RecordsPerPage;i++)
                    {
                        if(chunk[i]!=10)
                        {
                            if(index>=skip){line.WriteByte(chunk[i]);if(line.Length>MaximumRecordBytes)throw new IOException("Raw record exceeds 16 MB.");}
                            continue;
                        }
                        if(index>=skip)
                        {
                            rows.Add(new Dictionary<string,string>{["offset"]=lineStart.ToString(CultureInfo.InvariantCulture),["json"]=utf8.GetString(line.ToArray()).TrimEnd('\r')});
                            line.SetLength(0);
                        }
                        index++;lineStart=position+i+1;
                    }
                    position+=count;
                }
            }
            return rows;
        }
    }
}
