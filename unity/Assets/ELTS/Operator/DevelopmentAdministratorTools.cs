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
        private readonly List<StationTestCheckpoint> stationCheckpoints=new List<StationTestCheckpoint>();

        /// <summary>Durable save marker for one test. Results are calculated once, from the
        /// recording's events, so the checkpoint deliberately stores no counts of its own.</summary>
        public sealed class StationTestCheckpoint
        {
            public string schemaVersion="elts.test-checkpoint.v2";
            public bool synthetic=true, finalized=false;
            public string runId="", participantId="", condition="", blockId="", status="", savedUtc="";
            public string scoreStatus="unavailable", scoreExplanation="";
            public double activeSeconds, durationSeconds;
            public long droppedSamples;
        }

        public object StationToolsState => new {
            breakSeconds=stationBreakSeconds, practiceSeconds=stationPracticeSeconds,
            saveStatus=stationSaveStatus, latestCheckpoint=stationCheckpoints.LastOrDefault(),
            schedule=engine?.ScheduledConditions??StationOrder,
            queueIndex=engine?.CurrentBlockIndex??0,
            repeatable=engine?.FinishedConditions.ToArray()??Array.Empty<string>(),
            canRepeat=!IsBusy && !TestArmed && engine!=null && (engine.CanScheduleRepeat || (engine.CanReopenForRepeat && StationCanStartParticipant)),
            canSkipTest=!IsBusy && !closing && !TestArmed && engine?.State==SessionState.BlockReady && (!SeparateAdministrator || tutorial.CanManageReadyScenario),
            automaticStudy=tutorial.Started && !tutorial.Manual,
            canStartBreak=StationBreakReady,
            canSkipBreak=!IsBusy && (engine?.State==SessionState.Break || StationBreakReady),
            canRestartPractice=!IsBusy && !TestArmed && engine!=null &&
                (engine.State==SessionState.Practice || (engine.State==SessionState.BlockReady && engine.CurrentBlockIndex==0)) &&
                (!SeparateAdministrator || (!tutorial.Paused && (tutorial.Stage==TutorialStage.PracticeRunning || tutorial.Stage==TutorialStage.PracticeComplete))),
            canFinishPractice=!IsBusy && engine?.State==SessionState.Practice,
            calibration=new {
                phase=calibration.State.ToString(), review=root.Q<Label>("calibrationReview").text,
                canGenerate=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Idle,
                canRedo=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Review,
                canAccept=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Review && calibrationWithinLimits,
                canPractice=!IsBusy && engine?.State==SessionState.Calibration && calibration.State==CalibrationWizardState.Accepted
            },
            readiness=StationReadiness()
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
                    detail=calibration.State==CalibrationWizardState.Accepted?"Calibration saved.":"Complete the calibration steps inside the participant walkthrough." },
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
            preparedOrder=order.ToArray();
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
            if(SeparateAdministrator)throw new InvalidOperationException("Use the current tutorial step for calibration and practice.");
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
            if(engine.State!=SessionState.Break && engine.State!=SessionState.BlockEnded)
                throw new InvalidOperationException("There is no current break to skip.");
            // An unstarted break can also be explicitly skipped, preserving its events.
            if(engine.State==SessionState.BlockEnded)StationStartBreak();
            engine.SkipDevelopmentBreak();
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
            tutorial.MarkBreakSkipped();
        }
        public void StationSkipTest(string reason)
        {
            if(IsBusy || TestArmed || engine==null)throw new InvalidOperationException("Disarm and wait for saving before skipping a test.");
            if(SeparateAdministrator && !tutorial.CanManageReadyScenario)throw new InvalidOperationException("Finish the walkthrough before managing scenario attempts.");
            engine.SkipDevelopmentTest(reason);
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }
        public void StationRepeat(string condition,string reason)
        {
            if(IsBusy || TestArmed || engine==null)throw new InvalidOperationException("Disarm and wait for saving before repeating a test.");
            // Validate before reserving a new recording for a post-visit repeat.
            if (String.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 256)
                throw new ArgumentException("Enter a reason of 1 to 256 characters.");
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
                RecordSessionDetail("Continuation of "+Path.GetFileName(previous));
                StartScenario();
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

        // Each PlayMode run starts with a fresh collection. Keep previous synthetic
        // recordings available for inspection without importing them during setup.
        private static readonly string automatedRecordingRun = Guid.NewGuid().ToString("N");
        private string StationDataRoot => Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0
            ?Path.Combine(Application.temporaryCachePath,"elts-automated-recordings",automatedRecordingRun)
            :view.Configuration.Machine.ResolveDataRoot(Path.Combine(Application.dataPath,".."));

        // Automated tests write synthetic participant calibrations to their own folder.
        private static readonly string automatedCalibrationRun = Guid.NewGuid().ToString("N");
        /// <summary>Where participant calibrations saved by the Calibration Environment are read.</summary>
        public string CalibrationProfileDirectory => Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0
            ?Path.Combine(Application.temporaryCachePath,"elts-automated-calibrations",automatedCalibrationRun)
            :Elts.Config.MachineConfiguration.ResolveCalibrationProfiles(Path.Combine(Application.dataPath,".."));

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


        public void StationOpenDataFolder()
        {
            string directory=StationDataRoot;Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\""+directory+"\""){UseShellExecute=true});
            collectionMessage="Opened data folder: "+directory;
        }
    }
}
