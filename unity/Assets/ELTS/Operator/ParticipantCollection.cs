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
    /// database never copies their lines. Participants are identified only by code.</summary>
    internal sealed class ParticipantCollection
    {
        public readonly string Root;
        public string DatabasePath=>Path.Combine(Root,"collection.sqlite");
        public const int RecordsPerPage=50;
        internal const int SchemaVersion = 7;
        internal static readonly string[] AnalysisColumns={"analysisEligibility","analysisReason"};
        private const int MaximumRecordBytes=16777216;
        private string pageSource = "";
        private readonly List<long> pageOffsets = new List<long>();
        internal static readonly string[] Streams={"events.ndjson","samples.ndjson","targets.ndjson"};
        public ParticipantCollection(string root){Root=Path.GetFullPath(root);}
        private CollectionSqlite Open()=>new CollectionSqlite(DatabasePath);
        public void Initialize()
        {
            Directory.CreateDirectory(Root);
            if((File.GetAttributes(Root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Collection folder cannot be a link.");
            if(File.Exists(DatabasePath))CheckFile(DatabasePath);
            using(var db=Open())
            {
                int version = DatabaseVersion(db);
                if (version == SchemaVersion) return;
                if (version > SchemaVersion) throw new IOException("This collection was created by a newer version of ELTS.");
                db.Execute("PRAGMA journal_mode=WAL");
                db.Execute("PRAGMA secure_delete=ON");
                // SQLite requires FK enforcement to be disabled outside the transaction
                // while rebuilding the parent table. Validate every reference before commit.
                db.Execute("PRAGMA foreign_keys=OFF");
                db.Execute("BEGIN IMMEDIATE");
                bool reclaimSpace = false;
                try
                {
                    version = DatabaseVersion(db);
                    if (version > SchemaVersion) throw new IOException("This collection was created by a newer version of ELTS.");
                    if (version < SchemaVersion) reclaimSpace = Migrate(db);
                    db.Execute("COMMIT");
                }
                catch { db.Execute("ROLLBACK"); throw; }
                if (reclaimSpace) db.Execute("VACUUM");
            }
        }
        private static int DatabaseVersion(CollectionSqlite db)
            => Int32.Parse(db.Query("PRAGMA user_version")[0]["user_version"], CultureInfo.InvariantCulture);

        private static bool Migrate(CollectionSqlite db)
        {
            bool reclaimSpace = false;
            if (db.Query("SELECT name FROM pragma_table_info('participants') WHERE name='name'").Count > 0)
            {
                foreach (string view in new[] {"task_results", "task_seconds", "task_shots"})
                    db.Execute("DROP VIEW IF EXISTS " + view);
                db.Execute("CREATE TABLE participants_new (id TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE COLLATE NOCASE, createdUtc TEXT NOT NULL)");
                db.Execute("INSERT INTO participants_new SELECT id, code, createdUtc FROM participants");
                db.Execute("DROP TABLE participants");
                db.Execute("ALTER TABLE participants_new RENAME TO participants");
                reclaimSpace = true;
            }
            db.Execute("CREATE TABLE IF NOT EXISTS participants (id TEXT PRIMARY KEY, code TEXT NOT NULL UNIQUE COLLATE NOCASE, createdUtc TEXT NOT NULL)");
            db.Execute("CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, participantId TEXT NOT NULL REFERENCES participants(id), directory TEXT NOT NULL UNIQUE, modifiedUtc TEXT NOT NULL, finalized INTEGER NOT NULL DEFAULT 0, review TEXT NOT NULL DEFAULT '{}', visitId TEXT NOT NULL DEFAULT '')");
            // A visit groups a participant's first recording with its repeats and continuations.
            if(db.Query("SELECT name FROM pragma_table_info('sessions') WHERE name='visitId'").Count==0)db.Execute("ALTER TABLE sessions ADD COLUMN visitId TEXT NOT NULL DEFAULT ''");
            db.Execute("UPDATE sessions SET visitId=id WHERE visitId=''");
            if(db.Query("SELECT name FROM pragma_table_info('sessions') WHERE name='sourceStamp'").Count==0)
                db.Execute("ALTER TABLE sessions ADD COLUMN sourceStamp TEXT NOT NULL DEFAULT ''");
            if(db.Query("SELECT name FROM pragma_table_info('sessions') WHERE name='integrityStatus'").Count==0)
            {
                db.Execute("ALTER TABLE sessions ADD COLUMN integrityStatus TEXT NOT NULL DEFAULT 'Unchecked'");
                db.Execute("ALTER TABLE sessions ADD COLUMN integrityCheckedUtc TEXT NOT NULL DEFAULT ''");
                db.Execute("ALTER TABLE sessions ADD COLUMN integrityStamp TEXT NOT NULL DEFAULT ''");
            }
            db.Execute("CREATE INDEX IF NOT EXISTS sessions_participant ON sessions(participantId)");
            // Remember excluded source folders so Refresh cannot resurrect deleted runs.
            // Original recordings remain intact for logging integrity and recovery.
            db.Execute("CREATE TABLE IF NOT EXISTS removed_recordings (directory TEXT PRIMARY KEY COLLATE NOCASE)");
            // Version 3 and earlier copied every raw line into the database, making it
            // larger than the recordings themselves. Drop that copy and reclaim the space.
            if(db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name IN ('records','stream_progress')").Count>0)
            {
                db.Execute("DROP TABLE IF EXISTS records");db.Execute("DROP TABLE IF EXISTS stream_progress");reclaimSpace=true;
            }
            // Named, typed columns expose the shared derived review without
            // requiring researchers to navigate JSON or duplicate calculations.
            CreateAnalysisViews(db);
            CreateMetricView(db,"task_results",TaskDataReview.SummaryColumns,"");
            CreateMetricView(db,"task_seconds",TaskDataReview.SecondColumns,"seconds");
            CreateMetricView(db,"task_shots",TaskDataReview.ShotColumns,"shotDetails");
            if (db.Query("PRAGMA foreign_key_check").Count != 0) throw new IOException("Cannot upgrade a collection with broken recording references.");
            db.Execute("PRAGMA user_version=" + SchemaVersion);
            return reclaimSpace;
        }
        // A failed file check or missing closure blocks the entire recording.
        // Keep this rule here so the UI and every export see verification changes
        // immediately, without reparsing or changing raw event evidence.
        private static void CreateAnalysisViews(CollectionSqlite db)
        {
            db.Execute("DROP VIEW IF EXISTS visit_analysis");
            db.Execute("DROP VIEW IF EXISTS recording_analysis");
            db.Execute(@"CREATE VIEW recording_analysis AS
                SELECT sessionId,participantId,visitId,blocker,
                       CASE WHEN blocker<>'' THEN 'Excluded' ELSE COALESCE(json_extract(review,'$.analysisEligibility'),'Needs analysis') END AS analysisEligibility,
                       CASE WHEN blocker<>'' THEN blocker ELSE COALESCE(json_extract(review,'$.analysisReason'),'Refresh the index to inspect saved interval markers.') END AS analysisReason
                FROM (SELECT id AS sessionId,participantId,visitId,review,
                    CASE WHEN finalized=0 THEN 'The recording has no closure summary.'
                         WHEN integrityStatus='Failed' THEN 'A recording file failed checksum verification.'
                         ELSE COALESCE(json_extract(review,'$.analysisBlocker'),'') END AS blocker FROM sessions)");
            db.Execute(@"CREATE VIEW visit_analysis AS
                SELECT participantId,visitId,
                   CASE WHEN SUM(analysisEligibility='Excluded')=COUNT(*) THEN 'Excluded'
                        WHEN SUM(analysisEligibility='Excluded')>0 THEN 'Partially excluded' ELSE 'Needs analysis' END AS visitEligibility,
                   printf('%d of %d recordings excluded. %d require offline analysis.',
                          SUM(analysisEligibility='Excluded'),COUNT(*),SUM(analysisEligibility<>'Excluded')) AS visitAnalysisReason
                FROM recording_analysis GROUP BY participantId,visitId");
        }
        private static void CreateMetricView(CollectionSqlite db,string name,string[] columns,string detail)
        {
            string source=detail.Length==0?"a":"d";
            string fields=String.Join(",",columns.Select(c=>"json_extract("+source+".value,'$."+c+"') AS "+c));
            string context=detail.Length==0?"":",json_extract(a.value,'$.blockId') AS blockId,json_extract(a.value,'$.condition') AS condition";
            string analysis=detail.Length>0?"":",CASE WHEN r.blocker<>'' THEN 'Excluded' ELSE COALESCE(json_extract(a.value,'$.analysisEligibility'),'Needs analysis') END AS analysisEligibility"+
                ",CASE WHEN r.blocker<>'' THEN r.blocker ELSE COALESCE(json_extract(a.value,'$.analysisReason'),'Refresh the index to inspect saved interval markers.') END AS analysisReason";
            // Recreate so a changed metric column list always reaches existing databases.
            db.Execute("DROP VIEW IF EXISTS "+name);
            db.Execute("CREATE VIEW "+name+" AS SELECT p.code AS participantCode,s.visitId AS visitId,s.id AS sessionId,s.finalized AS recordingFinalized"+context+","+fields+analysis+
                " FROM sessions s JOIN participants p ON p.id=s.participantId"+(detail.Length==0?" JOIN recording_analysis r ON r.sessionId=s.id":"")+",json_each(s.review,'$.attempts') a"+
                (detail.Length==0?"":",json_each(a.value,'$."+detail+"') d"));
        }
        public string ExportsFolder()
        {
            string exports=Path.Combine(Root,"exports");Directory.CreateDirectory(exports);
            if((File.GetAttributes(exports)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export folder cannot be a link.");
            return exports;
        }
        /// <summary>Stream the selected recordings from one SQL snapshot into a workbook.</summary>
        public string ExportResults(string scope="all",string participant="",string visit="",string from="",string through="")
        {
            string destination=Path.Combine(ExportsFolder(),"elts-results-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".xlsx");
            using(var db=Open())
            {
                // One read transaction keeps every sheet from the same snapshot.
                db.Execute("BEGIN");
                try
                {
                    SelectExportSessions(db,scope,participant,visit,from,through);
                    var sheets=new List<CollectionWorkbook.Sheet>();
                    string[] context={"participantCode","visitId","sessionId","recordingFinalized"};
                    foreach(var spec in new[]{("Results","task_results",TaskDataReview.SummaryColumns),("Shots","task_shots",new[]{"blockId","condition"}.Concat(TaskDataReview.ShotColumns).ToArray()),("Timeline","task_seconds",new[]{"blockId","condition"}.Concat(TaskDataReview.SecondColumns).ToArray())})
                    {
                        var columns=context.Concat(spec.Item3).Concat(spec.Item1=="Results"?AnalysisColumns:Array.Empty<string>()).ToArray();
                        var rows=db.Rows("SELECT * FROM "+spec.Item2+" WHERE sessionId IN (SELECT id FROM export_sessions) ORDER BY participantCode COLLATE NOCASE,visitId,sessionId");
                        sheets.Add(new CollectionWorkbook.Sheet(spec.Item1,columns,rows.Select(r=>columns.Select(c=>r[c]).ToArray())));
                    }
                    sheets.Add(new CollectionWorkbook.Sheet("Metric guide",new[]{"Metric","Unit","Definition"},TaskDataReview.Definitions));
                    string[] manifest={"participantCode","visitId","sessionId","runId","recordingFinalized","modifiedUtc","integrityStatus","integrityCheckedUtc","metricsVersion","scope","fromDate","throughDate","analysisEligibility","analysisReason","visitEligibility","visitAnalysisReason"};
                    var sources=db.Rows("SELECT p.code,s.visitId,s.id,s.directory,s.finalized,s.modifiedUtc,s.integrityStatus,s.integrityCheckedUtc,r.analysisEligibility,r.analysisReason,v.visitEligibility,v.visitAnalysisReason FROM sessions s JOIN participants p ON p.id=s.participantId JOIN recording_analysis r ON r.sessionId=s.id JOIN visit_analysis v ON v.participantId=s.participantId AND v.visitId=s.visitId JOIN export_sessions e ON e.id=s.id ORDER BY p.code,s.visitId,s.id");
                    sheets.Add(new CollectionWorkbook.Sheet("Recordings",manifest,sources.Select(r=>new[]{r["code"],r["visitId"],r["id"],Path.GetFileName(r["directory"]),r["finalized"],r["modifiedUtc"],r["integrityStatus"],r["integrityCheckedUtc"],TaskDataReview.Version,scope,from,through,r["analysisEligibility"],r["analysisReason"],r["visitEligibility"],r["visitAnalysisReason"]})));
                    const string handoffSql="SELECT t.participantCode,t.visitId,t.sessionId,t.blockId,t.condition,s.directory FROM task_results t JOIN sessions s ON s.id=t.sessionId JOIN export_sessions e ON e.id=s.id ORDER BY t.participantCode,t.visitId,t.sessionId,t.blockId";
                    string[] handoff={"participantCode","visitId","sessionId","runId","blockId","condition","heartRateFile","eyeTrackingFile","questionnaireId","clockMappingStatus"};
                    // Collaborators populate these references on a working copy after collection.
                    // Missing files and an unmapped clock must never imply a synchronized observation.
                    sheets.Add(new CollectionWorkbook.Sheet("Handoff",handoff,db.Rows(handoffSql).Select(r=>new[]{r["participantCode"],r["visitId"],r["sessionId"],Path.GetFileName(r["directory"]),r["blockId"],r["condition"],"","","","Unmapped"})));
                    CollectionWorkbook.Write(destination,sheets);
                    db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
            return destination;
        }
        // Each export owns a connection-local list. All sheets read one snapshot.
        private static void SelectExportSessions(CollectionSqlite db,string scope,string participant,string visit,string from,string through)
        {
            db.Execute("CREATE TEMP TABLE export_sessions(id TEXT PRIMARY KEY)");
            const string insert = "INSERT INTO export_sessions SELECT id FROM sessions";
            switch(scope)
            {
                case "all": db.Execute(insert); return;
                case "participant":
                    if (String.IsNullOrEmpty(participant)) throw new ArgumentException("Select a participant.");
                    db.Execute(insert+" WHERE participantId=?",participant);return;
                case "visit":
                    if(String.IsNullOrEmpty(participant)||String.IsNullOrEmpty(visit))throw new ArgumentException("Select a visit.");
                    db.Execute(insert+" WHERE participantId=? AND visitId=?",participant,visit);return;
                case "dates":
                    if(!DateTime.TryParseExact(from,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var start) ||
                       !DateTime.TryParseExact(through,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var end) || end<start)
                        throw new ArgumentException("Choose a valid start and end date.");
                    db.Execute(insert+" WHERE substr(modifiedUtc,1,10) BETWEEN ? AND ?",from,through);return;
                default:throw new ArgumentException("Unknown export scope.");
            }
        }

        public string PreviewExport(string scope,string participant,string visit,string from,string through)
        {
            using(var db=Open())
            {
                db.Execute("BEGIN");
                SelectExportSessions(db,scope,participant,visit,from,through);
                string Count(string name) => db.Query("SELECT COUNT(*) AS n FROM "+name+" WHERE sessionId IN (SELECT id FROM export_sessions)")[0]["n"];
                return Count("task_results")+" result rows, "+Count("task_shots")+" shot rows, "+Count("task_seconds")+" timeline rows.";
            }
        }
        /// <summary>Reserve the participant code at intake before the first index refresh.</summary>
        public void SaveParticipant(string code)
        {
            Initialize();
            using(var db=Open())
            {
                db.Execute("INSERT OR IGNORE INTO participants (id,code,createdUtc) VALUES (?,?,?)",Guid.NewGuid().ToString("N"),code,DateTime.UtcNow.ToString("O"));
            }
        }
        /// <summary>Index one recording folder and store its derived review. Raw files are only read.
        /// An empty visit ID makes the recording its own visit.</summary>
        public void Import(string directory,string code,string visit,string review,string sourceStamp="",DateTime? modifiedUtc=null)
        {
            Initialize();directory=Path.GetFullPath(directory);
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recording folder cannot be a link.");
            using(var db=Open())
            {
                db.Execute("BEGIN IMMEDIATE");
                try
                {
                    // Recheck the exclusion under the write lock. A concurrent removal
                    // may have completed while this recording's review was being read.
                    if(db.Query("SELECT directory FROM removed_recordings WHERE directory=?",directory).Count>0)
                    { db.Execute("COMMIT"); return; }
                    db.Execute("INSERT OR IGNORE INTO participants (id,code,createdUtc) VALUES (?,?,?)",Guid.NewGuid().ToString("N"),code,DateTime.UtcNow.ToString("O"));
                    string participant=db.Query("SELECT id FROM participants WHERE code=?",code)[0]["id"];
                    db.Execute("INSERT OR IGNORE INTO sessions(id,participantId,directory,modifiedUtc) VALUES(?,?,?,?)",Guid.NewGuid().ToString("N"),participant,directory,DateTime.UtcNow.ToString("O"));
                    var session=db.Query("SELECT id,participantId FROM sessions WHERE directory=?",directory)[0];
                    db.Execute("UPDATE sessions SET integrityStatus='Unchecked',integrityCheckedUtc='',integrityStamp='' WHERE id=? AND integrityStamp<>'' AND integrityStamp<>?",session["id"],sourceStamp);
                    db.Execute("UPDATE sessions SET participantId=?,modifiedUtc=?,finalized=?,review=?,visitId=?,sourceStamp=? WHERE id=?",participant,(modifiedUtc??Directory.GetLastWriteTimeUtc(directory)).ToString("O"),
                        File.Exists(Path.Combine(directory,"session-summary.json"))?"1":"0",review,visit.Length>0?visit:session["id"],sourceStamp,session["id"]);
                    // Only a reassigned run can leave its previous profile empty. Profiles saved
                    // at intake but not yet imported must survive other recordings' refreshes.
                    if(session["participantId"]!=participant)
                        db.Execute("DELETE FROM participants WHERE id=? AND NOT EXISTS (SELECT 1 FROM sessions WHERE participantId=participants.id)",session["participantId"]);
                    db.Execute("COMMIT");
                }
                catch {db.Execute("ROLLBACK");throw;}
            }
        }
        internal static void CheckFile(string path)
        {if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Data import cannot follow linked files.");}

        internal (Dictionary<string,string> Cached,HashSet<string> Excluded) ReadIndexSources()
        {
            var cached = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var db = Open())
            {
                foreach (var row in db.Query("SELECT directory,sourceStamp FROM sessions"))
                    cached[row["directory"]] = row["sourceStamp"];
                foreach (var row in db.Query("SELECT directory FROM removed_recordings"))
                    excluded.Add(row["directory"]);
            }
            return (cached,excluded);
        }
        internal void SetVerification(string id,string status,string at,string stamp)
        {
            using(var db=Open())db.Execute("UPDATE sessions SET integrityStatus=?,integrityCheckedUtc=?,integrityStamp=? WHERE id=?",status,at,stamp,id);
        }
        public object Profiles()
        {
            using(var db=Open())return db.Query("SELECT p.id,p.code,COUNT(s.id) AS sessions,MAX(s.modifiedUtc) AS modifiedUtc FROM participants p LEFT JOIN sessions s ON s.participantId=p.id GROUP BY p.id ORDER BY modifiedUtc DESC");
        }
        public object Profile(string id)
        {
            using(var db=Open())return new { participant=db.Query("SELECT id,code,createdUtc FROM participants WHERE id=?",id).FirstOrDefault(),
                sessions=db.Query("SELECT s.id,s.visitId,s.modifiedUtc,s.finalized,s.review,s.directory,s.integrityStatus,s.integrityCheckedUtc,r.blocker AS analysisBlocker,r.analysisEligibility,r.analysisReason,v.visitEligibility,v.visitAnalysisReason FROM sessions s JOIN recording_analysis r ON r.sessionId=s.id JOIN visit_analysis v ON v.participantId=s.participantId AND v.visitId=s.visitId WHERE s.participantId=? ORDER BY s.modifiedUtc DESC,s.id",id) };
        }
        public string SessionDirectory(string id)
        {
            using(var db=Open())return db.Query("SELECT directory FROM sessions WHERE id=?",id).FirstOrDefault()?["directory"]??throw new IOException("Select a saved session.");
        }
        public bool IsRemoved(string directory)
        {
            using(var db=Open())return db.Query("SELECT directory FROM removed_recordings WHERE directory=?",Path.GetFullPath(directory)).Count>0;
        }
        /// <summary>Remove a participant or one whole run from the
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
            page=Math.Max(0,page);
            long skip=page*(long)RecordsPerPage;
            lock(pageOffsets)
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            {
                // Snapshot the length once. A trailing partial line is still being written.
                long limit=input.Length;var chunk=new byte[65536];var line=new MemoryStream();
                string source=path+"|"+limit+"|"+File.GetLastWriteTimeUtc(path).Ticks;
                if(pageSource!=source){pageSource=source;pageOffsets.Clear();pageOffsets.Add(0);}
                int knownPage=Math.Min(page,pageOffsets.Count-1);
                long position=pageOffsets[knownPage],lineStart=position,index=knownPage*(long)RecordsPerPage;
                input.Position=position;
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
                        if(index==pageOffsets.Count*(long)RecordsPerPage)pageOffsets.Add(lineStart);
                    }
                    position+=count;
                }
            }
            return rows;
        }
    }
}
