#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Elts.Calibration;
using Elts.Scenario;
using Elts.Session;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Browser administrator preparation, save checkpoints and local recording review.
    /// Raw streams remain owned exclusively by SessionLogWriter. These small review
    /// documents are supplementary; only session-summary.json finalizes a raw run.
    /// </summary>
    public sealed partial class DevelopmentSessionPanel
    {
        private double stationBreakSeconds, stationPracticeSeconds;
        private string stationCheckpointId="", stationSaveStatus="No recording yet.";
        private readonly List<StationNoteEntry> stationNotes=new List<StationNoteEntry>();
        private readonly List<StationTestCheckpoint> stationCheckpoints=new List<StationTestCheckpoint>();
        private Task? archiveOperation;
        private object[] stationRecordings=Array.Empty<object>();
        private object? stationReview;
        private string archiveMessage="", exportPath="";

        public sealed class StationNoteEntry
        {
            public double seconds;
            public string text="";
        }
        public sealed class StationTestCheckpoint
        {
            public string schemaVersion="elts.test-checkpoint.v1";
            public bool synthetic=true, finalized=false;
            public string runId="", participantId="", condition="", blockId="", status="", savedUtc="";
            public string scoreStatus="unavailable", scoreExplanation="";
            public double activeSeconds, durationSeconds;
            public int hits, shots, headHits, bodyHits, limbHits, coverHits, misses, targetKills, damageDealt, reloads, dryFires;
            public long droppedSamples;
        }

        public object StationToolsState => new {
            breakSeconds=stationBreakSeconds, practiceSeconds=stationPracticeSeconds,
            saveStatus=stationSaveStatus, checkpoints=stationCheckpoints, notes=stationNotes,
            schedule=engine?.ScheduledConditions??StationOrder,
            queueIndex=engine?.CurrentBlockIndex??0,
            repeatable=engine?.FinishedConditions.ToArray()??Array.Empty<string>(),
            canRepeat=!IsBusy && !TestArmed && engine!=null && (engine.CanScheduleRepeat || (engine.CanReopenForRepeat && StationCanStartParticipant)),
            canSkipTest=!IsBusy && !closing && !TestArmed && engine?.State==SessionState.BlockReady,
            canStartBreak=!IsBusy && engine?.State==SessionState.BlockEnded,
            canSkipBreak=!IsBusy && (engine?.State==SessionState.Break || engine?.State==SessionState.BlockEnded),
            canRestartPractice=!IsBusy && !TestArmed && engine!=null &&
                (engine.State==SessionState.Practice || (engine.State==SessionState.BlockReady && engine.CurrentBlockIndex==0)),
            canFinishPractice=!IsBusy && engine?.State==SessionState.Practice,
            calibration=new {
                phase=calibration.State.ToString(), review=root.Q<Label>("calibrationReview").text,
                canGenerate=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Idle,
                canRedo=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Review,
                canAccept=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Review && calibrationWithinLimits,
                canPractice=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Accepted
            },
            readiness=StationReadiness(), recordings=stationRecordings, review=stationReview,
            archiveBusy=archiveOperation!=null && !archiveOperation.IsCompleted, archiveMessage, exportPath
        };

        private object[] StationReadiness()
        {
            var sample=acquisition?.Snapshot;
            bool fresh=sample.HasValue && clock!=null &&
                clock.Now.Ticks-sample.Value.Head.Acquisition.Timestamp.Ticks>=0 &&
                clock.Now.Ticks-sample.Value.Head.Acquisition.Timestamp.Ticks<=MaximumSampleAgeSeconds*TimeSpan.TicksPerSecond;
            bool valid=fresh && sample!.Value.Head.HasValidPose && sample.Value.Weapon.HasValidPose;
            bool open=recording?.IsOpen==true;
            return new object[] {
                new { name="Recording",status=open?(recording!.WriterHealthy?"Ready":"Blocked"):"Waiting",
                    detail=open?(recording!.WriterHealthy?"Writer active; dropped samples: "+recording.DroppedSampleCount:"Writer failed. Retain this recording and start a new attempt."):"Create a recording to check the writer." },
                new { name="Synthetic tracking",status=valid?"Ready":"Waiting",
                    detail=valid?"Fresh paired observations; "+(acquisition?.ActualElapsedRateHz??0).ToString("F1")+" Hz observed.":"Waiting for fresh virtual poses; keep the participant app open." },
                new { name="Storage",status=diskFreeBytes>=MinimumDiskBytes?"Ready":diskFreeBytes>0?"Blocked":"Waiting",
                    detail=diskFreeBytes>0?(diskFreeBytes/(double)(1024*1024*1024)).ToString("F1")+" GB free at recording setup.":"Free space is checked when creating the recording (2 GB required)." },
                new { name="Configuration",status="Ready",detail="Loaded and validated: "+view.Configuration.EffectiveSha256.Substring(0,12) },
                new { name="Calibration",status=calibration.State==CalibrationWizardState.Accepted?"Ready":"Waiting",
                    detail=calibration.State==CalibrationWizardState.Accepted?"Synthetic fixture accepted; startup rig configuration remains active.":"Generate, review and accept the synthetic fixture before practice." },
                new { name="Devices and displays",status="Emulated",detail="Controller: "+(mockLink?.State.ToString()??"not created")+". Display and SteamVR readiness are emulated; physical study readiness is unavailable." }
            };
        }

        public void StationApplySetup(string[] order, Dictionary<string,double> durations, double practice, double rest)
        {
            if(!StationCanStartParticipant)throw new InvalidOperationException("Finish the active participant before replacing the setup.");
            _=new SessionPlan("setup-preview",order);
            SessionEngine.ValidateDevelopmentPhaseDuration(practice);SessionEngine.ValidateDevelopmentPhaseDuration(rest);
            if(durations.Count!=stationDurations.Count || stationDurations.Keys.Any(key=>!durations.ContainsKey(key)))
                throw new ArgumentException("Supply a duration for each of the four conditions.");
            foreach(double value in durations.Values)SessionEngine.ValidateDevelopmentDuration(value);
            // Validate the entire preset before changing any station setting.
            Field("conditionOrder").value=String.Join(",",order);
            foreach(var value in durations)stationDurations[value.Key]=value.Value;
            stationPracticeSeconds=practice;stationBreakSeconds=rest;
            lastMessage="Test setup applied.";
        }

        public void StationSetPhaseDurations(double practice, double rest)
        {
            if(IsBusy)throw new InvalidOperationException("Wait for the save operation to finish.");
            SessionEngine.ValidateDevelopmentPhaseDuration(practice);SessionEngine.ValidateDevelopmentPhaseDuration(rest);
            if(!StationCanStartParticipant && engine!=null)
            {
                engine.SetDevelopmentPhaseDurations(practice,rest);
                if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
            }
            stationPracticeSeconds=practice;stationBreakSeconds=rest;
        }

        public void StationPreparationAction(string action)
        {
            if(IsBusy || engine==null || closing)throw new InvalidOperationException("No active preparation step is available.");
            switch(action)
            {
                case "generateCalibration":CaptureCalibrationFixture();break;
                case "redoCalibration":
                    if(engine.State!=SessionState.Calibration || calibration.State!=CalibrationWizardState.Review)
                        throw new InvalidOperationException("Only an unaccepted fixture can be redone.");
                    ResetCalibration();break;
                case "acceptCalibration":AcceptCalibrationFixture();break;
                case "startPractice":
                    if(engine.State!=SessionState.Calibration)throw new InvalidOperationException("Practice starts after calibration review.");
                    AdvanceWithCalibration();break;
                case "restartPractice":
                    if(TestArmed)throw new InvalidOperationException("Disarm before restarting practice.");
                    engine.RestartDevelopmentPractice();break;
                case "finishPractice":engine.FinishDevelopmentPractice();break;
                default:throw new ArgumentException("Unknown preparation action.");
            }
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }

        public void StationSkipBreak()
        {
            if(IsBusy || engine==null)throw new InvalidOperationException("Wait for the current test to finish saving.");
            // An unstarted break can also be explicitly skipped, preserving its events.
            if(engine.State==SessionState.BlockEnded)engine.Advance();
            engine.SkipDevelopmentBreak();
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }
        public void StationSkipTest(string reason)
        {
            if(IsBusy || TestArmed || engine==null)throw new InvalidOperationException("Disarm and wait for saving before skipping a test.");
            engine.SkipDevelopmentTest(reason);
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }
        public void StationRepeat(string condition,string reason)
        {
            if(IsBusy || TestArmed || engine==null)throw new InvalidOperationException("Disarm and wait for saving before repeating a test.");
            if(engine.CanReopenForRepeat)
            {
                if(!StationCanStartParticipant || !engine.FinishedConditions.Contains(condition) || String.IsNullOrWhiteSpace(reason))
                    throw new InvalidOperationException("Choose a finished test and enter a reason after the recording closes.");
                Begin(()=>ReopenStationRepeatAsync(condition,reason));return;
            }
            engine.QueueDevelopmentRepeat(condition,reason);
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }

        private async Task ReopenStationRepeatAsync(string condition,string reason)
        {
            string previous=recording!.RunDirectory!;
            try
            {
                await CloseRecordingAsync();
                if(!await recording.ReserveAndStartAsync(runId))throw new IOException("A separate repeat recording could not be created.");
                await SaveParticipantIdentityAsync();
                engine!.ReopenDevelopmentRepeat(condition,reason);
                engine.SetDevelopmentDuration(condition,stationDurations[condition]);
                engine.SetDevelopmentPhaseDurations(stationPracticeSeconds,stationBreakSeconds);
                if(engine.State==SessionState.Failed)throw new IOException(engine.Failure);
                closing=false;
                StationNote("Continuation of "+Path.GetFileName(previous)+"; reason: "+reason);
                scenario=new DevelopmentSessionScenario(view.Configuration,clock!,engine,recording,sequence!,view.GameSettings);
                RecordInputSource();StartAcquisition(view.Configuration);
                stationSaveStatus="Recording repeated test in a new folder; earlier data retained.";
            }
            catch
            {
                engine?.Abort("Repeat recording setup failed.");
                closing=true;await CloseRecordingAsync();throw;
            }
        }

        private StationTestCheckpoint CreateStationCheckpoint() => new StationTestCheckpoint {
            runId=runId,participantId=stationParticipant,condition=engine!.CurrentCondition!,blockId=engine.CurrentBlockId!,
            status=engine.CurrentBlockSkipped?"Skipped":engine.CurrentBlockStoppedEarly?"Stopped early":"Completed",
            hits=engine.CurrentBlockSkipped?0:StationHits,shots=engine.CurrentBlockSkipped?0:StationShots,
            headHits=engine.CurrentBlockSkipped?0:StationHeadHits,bodyHits=engine.CurrentBlockSkipped?0:StationBodyHits,
            limbHits=engine.CurrentBlockSkipped?0:StationLimbHits,coverHits=engine.CurrentBlockSkipped?0:StationCoverHits,
            misses=engine.CurrentBlockSkipped?0:StationMisses,targetKills=engine.CurrentBlockSkipped?0:StationTargetKills,
            damageDealt=engine.CurrentBlockSkipped?0:StationDamageDealt,
            reloads=engine.CurrentBlockSkipped?0:StationReloads,dryFires=engine.CurrentBlockSkipped?0:StationDryFires,
            activeSeconds=engine.CurrentActiveSeconds,durationSeconds=engine.CurrentDurationSeconds,
            scoreExplanation=engine.CurrentBlockSkipped?"Skipped; no scored attempt.":engine.CurrentBlockModifiedTiming?
                "Flexible or interrupted timing; excluded from the uninterrupted 300-second score.":"Synthetic counts only; use validated analysis on the finalized raw recording.",
            droppedSamples=recording?.DroppedSampleCount??0
        };

        private async Task SaveStationCheckpointAsync(StationTestCheckpoint checkpoint)
        {
            try
            {
                if(recording==null || !await recording.CheckpointAsync())
                    throw new IOException("Raw data could not be durably saved.");
                checkpoint.savedUtc=DateTimeOffset.UtcNow.ToString("O");
                string path=Path.Combine(recording.RunDirectory!,"test-checkpoint-"+checkpoint.blockId+".json");
                string content=JsonConvert.SerializeObject(checkpoint,Formatting.Indented);
                await Task.Run(()=>PublishReviewFile(path,content));
                stationCheckpoints.Add(checkpoint);
                await SyncCollectionRecording();
                stationSaveStatus="Saved "+checkpoint.condition+" · "+checkpoint.blockId+" · "+checkpoint.savedUtc;
            }
            catch(Exception error)
            {
                stationSaveStatus="SAVE FAILED: "+error.Message;
                engine?.Abort("Test save checkpoint failed: "+error.Message);
                throw;
            }
        }

        private static void PublishReviewFile(string path,string content)
        {
            string pending=path+".pending";
            using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))
            { writer.Write(content);writer.Flush();stream.Flush(true); }
            File.Move(pending,path); // No overwrite, including repeated attempts.
        }

        private string StationDataRoot => Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0
            ?Path.Combine(Application.temporaryCachePath,"elts-automated-recordings")
            :view.Configuration.Machine.ResolveDataRoot(Path.Combine(Application.dataPath,".."));

        public void StationArchiveAction(string action,string id="")
        {
            if(archiveOperation!=null && !archiveOperation.IsCompleted)throw new InvalidOperationException("Wait for recording review to finish.");
            string rootPath=StationDataRoot;
            string? activePath=recording?.IsOpen==true?recording.RunDirectory:null;
            archiveMessage="Loading…";
            if(action=="reviewRecording")stationReview=null;
            if(action=="exportRecording")exportPath="";
            archiveOperation=RunArchiveActionAsync(action,id,rootPath,activePath);
        }

        private async Task RunArchiveActionAsync(string action,string id,string rootPath,string? activePath)
        {
            try
            {
                if(action=="listRecordings")
                {
                    stationRecordings=await Task.Run(()=>Directory.Exists(rootPath)?Directory.GetDirectories(rootPath)
                        .Where(path=>(File.GetAttributes(path)&FileAttributes.ReparsePoint)==0 && File.Exists(Path.Combine(path,"events.ndjson")))
                        .OrderByDescending(Directory.GetLastWriteTimeUtc).Select(path=>(object)new {
                            id=Path.GetFileName(path),finalized=File.Exists(Path.Combine(path,"session-summary.json")),
                            active=String.Equals(path,activePath,StringComparison.OrdinalIgnoreCase),
                            modifiedUtc=Directory.GetLastWriteTimeUtc(path).ToString("O") }).ToArray():Array.Empty<object>());
                    archiveMessage=stationRecordings.Length+" local recordings.";
                }
                else
                {
                    string directory=ResolveRecording(rootPath,id);
                    if(action=="reviewRecording")
                    {
                        stationReview=await Task.Run(()=>ReadRecordingReview(directory));
                        archiveMessage="Review loaded. Counts are descriptive synthetic results.";
                    }
                    else if(action=="exportRecording")
                    {
                        if(String.Equals(directory,activePath,StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(directory,"session-summary.json")))
                            throw new InvalidOperationException("Finish the participant session before exporting the raw recording.");
                        string outputDirectory=Path.Combine(rootPath,"exports");
                        Directory.CreateDirectory(outputDirectory);
                        if((File.GetAttributes(outputDirectory)&FileAttributes.ReparsePoint)!=0)throw new IOException("Export folder cannot be a link.");
                        string destination=Path.Combine(outputDirectory,id+"-"+Guid.NewGuid().ToString("N")+".zip");
                        await Task.Run(()=>ExportRecording(directory,destination));
                        exportPath=destination;archiveMessage="Raw recording exported to "+destination;
                    }
                    else throw new ArgumentException("Unknown recording action.");
                }
            }
            catch(Exception error){archiveMessage="Recording action failed: "+error.Message;}
        }

        private static string ResolveRecording(string rootPath,string id)
        {
            Elts.Logging.LoggingRunDirectory.ValidateRunId(id);
            string directory=Path.Combine(rootPath,id);
            if(!Directory.Exists(directory) || (File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Choose an existing recording directory.");
            return directory;
        }

        private static void ExportRecording(string directory,string destination)
        {
            // Only regular files directly owned by the recording are exported.
            // Refuse links instead of following them to unrelated local files.
            if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0 || (File.GetAttributes(Path.GetDirectoryName(destination)!)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Raw export folders cannot be links.");
            string pending=destination+".pending";
            using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write))
            {
                using(var archive=new ZipArchive(stream,ZipArchiveMode.Create,true))
                    foreach(string path in Directory.GetFiles(directory))
                    {
                        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Recording contains a linked file.");
                        archive.CreateEntryFromFile(path,Path.GetFileName(path),System.IO.Compression.CompressionLevel.Fastest);
                    }
                stream.Flush(true);
            }
            File.Move(pending,destination);
        }

        private static object ReadRecordingReview(string directory) => TaskDataReview.Read(directory);

        public void StationOpenDataFolder()
        {
            string directory=StationDataRoot;Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\""+directory+"\""){UseShellExecute=true});
            collectionMessage="Opened data folder: "+directory;
        }
    }
}
