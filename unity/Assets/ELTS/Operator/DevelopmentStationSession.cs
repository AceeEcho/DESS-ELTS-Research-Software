#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Elts.Geometry;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Administrator commands enter the same recording/session controller as the
    /// original workbench. Arming waits outside the timed block; the participant
    /// start trigger is observed and recorded before any scored shots are allowed.
    /// </summary>
    public sealed partial class DevelopmentSessionPanel
    {
        private bool stationPreparing;
        private string stationParticipant = "";
        private string stationParticipantName = "", stationInitialNotes = "";
        private double stationStartedAt;
        private readonly Dictionary<string,double> stationDurations = new Dictionary<string,double>(StringComparer.Ordinal) {
            ["WE_FT"]=300,["WE_MT"]=300,["NE_FT"]=300,["NE_MT"]=300
        };
        public bool SeparateAdministrator { get; private set; }
        public bool TestArmed { get; private set; }
        public bool StationCanArm => !IsBusy && !closing && !TestArmed && engine?.State==SessionState.BlockReady;
        public bool StationCanStartParticipant => !IsBusy && !stationPreparing &&
            (engine==null || (closing && recording?.IsOpen!=true));
        public string StationParticipant => stationParticipant;
        public string StationRunId => runId;
        public string StationMessage => engine?.Failure ?? lastMessage;
        public string StationRecordingPath => recording?.RunDirectory ?? "";
        public int StationShots => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.ShotCount??0 : 0;
        public int StationHits => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.HitCount??0 : 0;
        public int StationHeadHits => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.HeadHits??0 : 0;
        public int StationBodyHits => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.BodyHits??0 : 0;
        public int StationLimbHits => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.LimbHits??0 : 0;
        public int StationCoverHits => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.CoverHits??0 : 0;
        public int StationMisses => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.MissCount??0 : 0;
        public int StationTargetKills => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.TargetKills??0 : 0;
        public int StationDamageDealt => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.DamageDealt??0 : 0;
        public int StationReloads => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.Reloads??0 : 0;
        public int StationDryFires => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.DryFires??0 : 0;
        public int StationAmmo => scenario?.RecordedBlockId==engine?.CurrentBlockId ? scenario?.AmmoRemaining??view.GameSettings.magazineCapacity : view.GameSettings.magazineCapacity;
        public int StationMagazineCapacity=>scenario?.MagazineCapacity??view.GameSettings.magazineCapacity;
        public string[] StationOrder => Field("conditionOrder").value.Split(',');
        public double StationElapsed => Math.Max(0,(clock?.Now.Elapsed.TotalSeconds ?? 0)-stationStartedAt);
        public IReadOnlyDictionary<string,double> StationDurations => stationDurations;
        public bool StationCanPause => !IsBusy && !closing && engine?.State == SessionState.BlockRunning;
        public bool StationCanResume => !IsBusy && !closing && engine?.State == SessionState.BlockPaused;
        public bool StationCanStop => StationCanPause || StationCanResume;
        public bool StationCanSetDuration(string condition) => !IsBusy && stationDurations.ContainsKey(condition) &&
            (StationCanStartParticipant || (!closing && engine?.CanChangeDuration(condition) == true));

        public void StationSetDuration(string condition, double seconds)
        {
            SessionEngine.ValidateDevelopmentDuration(seconds);
            if(!StationCanSetDuration(condition))throw new InvalidOperationException("Pause the current test before editing its duration. Finished tests are locked.");
            if(!StationCanStartParticipant && engine != null)
            {
                engine.SetDevelopmentDuration(condition,seconds);
                if(engine.State == SessionState.Failed)throw new InvalidOperationException(engine.Failure);
            }
            stationDurations[condition] = seconds;
            lastMessage = "Test duration saved.";
        }

        public void StationPause()
        {
            if(!StationCanPause)throw new InvalidOperationException("No running test to pause.");
            engine!.PauseDevelopmentBlock();TestArmed=false;
            if(engine.State == SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }
        public void StationResume()
        {
            if(!StationCanResume)throw new InvalidOperationException("No paused test to resume.");
            engine!.ResumeDevelopmentBlock();
            if(engine.State == SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }
        public void StationStop()
        {
            if(!StationCanStop)throw new InvalidOperationException("No running or paused test to stop.");
            engine!.StopDevelopmentBlock();TestArmed=false;
            if(engine.State == SessionState.Failed)throw new InvalidOperationException(engine.Failure);
        }

        public void EnableSeparateAdministrator()
        {
            SeparateAdministrator=true;
            stationBreakSeconds=view.Configuration.Session.BreakDurationSeconds;
            stationPracticeSeconds=view.Configuration.Session.PracticeDurationSeconds;
            desktop!.SetOpen(false);
            root.style.display=DisplayStyle.None;
            // Keep the existing session-to-view feed alive while hiding its UI.
            view.SessionControlsVisible=true;
        }

        public void StationStartParticipant(string participant, IEnumerable<string> order, string participantName="", string initialNotes="")
        {
            if(!StationCanStartParticipant)throw new InvalidOperationException("Finish the current participant first.");
            participant=participant.Trim();
            if(!Regex.IsMatch(participant,@"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$"))
                throw new ArgumentException("Use 1–40 letters, numbers, hyphens or underscores for the participant ID.");
            var conditions=order.ToArray();
            // Validate through the canonical session plan before opening a writer.
            _=new SessionPlan(participant,conditions);
            if(participantName.Length>120 || initialNotes.Length>2000)
                throw new ArgumentException("Name is limited to 120 characters and initial notes to 2000 characters.");
            stationParticipant=participant;
            stationNotes.Clear(); stationCheckpoints.Clear(); stationCheckpointId="";
            stationSaveStatus="Recording will start after setup.";
            stationParticipantName=participantName.Trim();stationInitialNotes=initialNotes.Trim();
            stationStartedAt=clock?.Now.Elapsed.TotalSeconds ?? 0;
            Field("participant").value=participant+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6);
            Field("conditionOrder").value=String.Join(",",conditions);
            root.Q<DropdownField>("inputSource").value=DesktopInputOption;
            TestArmed=false;stationPreparing=true;
            Begin(CreateSessionAsync);
        }

        public void StationTick()
        {
            if(IsBusy)return;
            if(stationPreparing)
            {
                try
                {
                    if(engine?.State==SessionState.SessionSetup)
                    {
                        StationNote("Participant ID: "+stationParticipant+"; desktop synthetic rehearsal.");
                        // Use the existing timestamped writer; identity never enters
                        // filenames or browser preferences beyond the participant ID.
                        StationNote("Participant details: "+Newtonsoft.Json.JsonConvert.SerializeObject(new {
                            participantId=stationParticipant,participantName=stationParticipantName,initialNotes=stationInitialNotes }));
                        AdvanceWithCalibration();
                        stationPreparing=false;
                        lastMessage="Review the synthetic calibration fixture, then start practice.";
                    }
                    if(engine?.State==SessionState.BlockReady)
                    { stationPreparing=false;lastMessage="Ready. Arm the test when the participant is ready."; }
                    if(engine==null || engine.State==SessionState.Failed || engine.State==SessionState.Aborted || engine.State==SessionState.SessionComplete)
                        stationPreparing=false;
                }
                catch(Exception exception)
                {
                    stationPreparing=false;lastMessage=exception.Message;
                    engine?.Abort("Synthetic station preparation failed: "+exception.Message);
                }
            }
            if(engine?.State==SessionState.BlockEnded && stationCheckpointId!=engine.CurrentBlockId)
            {
                // End-of-test target cleanup belongs before the durable boundary.
                scenario?.Refresh(0,acquisition?.Snapshot?.Head.Pose);
                stationCheckpointId=engine.CurrentBlockId!;
                var checkpoint=CreateStationCheckpoint();
                // Save immediately; the administrator starts the break separately.
                stationSaveStatus="Saving "+checkpoint.condition+"…";
                Begin(()=>SaveStationCheckpointAsync(checkpoint));
            }
            if(!IsBusy && engine?.State==SessionState.Break && engine.RemainingSeconds<=0)
                engine.Advance();
            if(engine?.State!=SessionState.BlockReady)TestArmed=false;
        }

        public void StationArm()
        {
            if(!StationCanArm)throw new InvalidOperationException("The test is not ready to arm.");
            StationEvent("TestArmed");TestArmed=true;
            lastMessage="Armed. Waiting for the participant to shoot to begin.";
        }
        public void StationDisarm()
        {
            if(!TestArmed)return;
            StationEvent("TestDisarmed");TestArmed=false;
            lastMessage="Disarmed. The participant cannot start the test.";
        }
        public void StationAbort(string reason)
        {
            if(engine==null || closing)throw new InvalidOperationException("No active session to abort.");
            if(String.IsNullOrWhiteSpace(reason))throw new ArgumentException("Enter a reason for stopping this session.");
            TestArmed=false;stationPreparing=false;engine.Abort(reason.Trim());
        }
        public void StationStartBreak()
        {
            if(IsBusy || engine?.State!=SessionState.BlockEnded)
                throw new InvalidOperationException("Wait for the test to finish saving before starting its break.");
            engine.Advance();
        }
        public void StationNext()
        {
            if(IsBusy || engine==null || !engine.CanAdvance ||
                (engine.State!=SessionState.BlockEnded && engine.State!=SessionState.Break))
                throw new InvalidOperationException("The next phase is not available yet.");
            engine.Advance();
        }
        public void StationNote(string text)
        {
            if(engine==null || closing || String.IsNullOrWhiteSpace(text))throw new ArgumentException("Enter a note for an active session.");
            engine.AddNote(text.Trim());lastMessage="Note saved.";
            if(engine.State==SessionState.Failed)throw new InvalidOperationException(engine.Failure);
            stationNotes.Add(new StationNoteEntry { seconds=StationElapsed,text=text.Trim() });
        }
        private void StationEvent(string type)
        {
            if(!recording!.TryRecord(new SessionEvent(sequence!.Next(),clock!.Now,type,new[]{
                LogField.String("blockId",engine!.CurrentBlockId!),LogField.Boolean("synthetic",true)})))
                throw new InvalidOperationException("The administrator action could not be recorded.");
        }

        public void StationPublishPose(RigidPose headPose,RigidPose weaponPose)
        {
            desktopSource?.Publish(headPose,weaponPose);
            if(desktopSource==null)view.SetSessionFrame(headPose,weaponPose,EmptyDesktopTargets);
        }

        public bool StationParticipantTrigger()
        {
            if(IsBusy || closing || engine==null)return false;
            if(TestArmed && engine.State==SessionState.BlockReady)
            {
                if(acquisition==null || !acquisition.TryCaptureForTrigger(out var pair) || !pair.IsValid)
                {lastMessage="Waiting for a valid recorded pose before starting.";return false;}
                TestArmed=false;
                // The start click is consumed by the prompt, not counted as a
                // target shot. Its source observation remains auditable.
                if(!recording!.TryRecord(new SessionEvent(sequence!.Next(),clock!.Now,"ParticipantStartTrigger",new[]{
                    LogField.String("blockId",engine.CurrentBlockId!),
                    LogField.NumberValue("acquisitionSequence",pair.Sequence),
                    LogField.NumberValue("observedMonotonicTicks",pair.Timestamp.Ticks),
                    LogField.String("edge","falling")})))
                {engine.Abort("Participant start could not be recorded.");return false;}
                engine.StartBlock();
                if(engine.State!=SessionState.BlockRunning)return false;
                scenario!.Refresh(0,pair.Head.Pose);
                view.SetSessionFrame(pair.Head.Pose,pair.Weapon.Pose,scenario.Positions);
                lastMessage="Test running.";return true;
            }
            if(engine.State!=SessionState.BlockRunning)return false;
            int before=StationShots;SubmitDesktopTrigger();return StationShots>before;
        }

        public bool StationReload()
        {
            if(IsBusy || closing || engine?.State!=SessionState.BlockRunning)return false;
            try{return scenario?.Reload()==true;}
            catch(Exception error)
            {
                engine.Abort("Weapon reload could not be recorded: "+error.Message);
                return false;
            }
        }
    }
}
