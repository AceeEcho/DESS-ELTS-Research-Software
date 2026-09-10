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
        private double stationStartedAt;
        public bool SeparateAdministrator { get; private set; }
        public bool TestArmed { get; private set; }
        public bool StationCanArm => !IsBusy && !closing && !TestArmed && engine?.State==SessionState.BlockReady;
        public bool StationCanStartParticipant => !IsBusy && !stationPreparing &&
            (engine==null || (closing && recording?.IsOpen!=true));
        public string StationParticipant => stationParticipant;
        public string StationRunId => runId;
        public string StationMessage => engine?.Failure ?? lastMessage;
        public string StationRecordingPath => recording?.RunDirectory ?? "";
        public int StationShots => scenario?.ShotCount ?? 0;
        public int StationHits => scenario?.HitCount ?? 0;
        public string[] StationOrder => Field("conditionOrder").value.Split(',');
        public double StationElapsed => Math.Max(0,(clock?.Now.Elapsed.TotalSeconds ?? 0)-stationStartedAt);

        public void EnableSeparateAdministrator()
        {
            SeparateAdministrator=true;
            desktop!.SetOpen(false);
            root.style.display=DisplayStyle.None;
            // Keep the existing session-to-view feed alive while hiding its UI.
            view.SessionControlsVisible=true;
        }

        public void StationStartParticipant(string participant, IEnumerable<string> order)
        {
            if(!StationCanStartParticipant)throw new InvalidOperationException("Finish the current participant first.");
            participant=participant.Trim();
            if(!Regex.IsMatch(participant,@"^[A-Za-z0-9][A-Za-z0-9_-]{0,39}$"))
                throw new ArgumentException("Use 1–40 letters, numbers, hyphens or underscores for the participant ID.");
            var conditions=order.ToArray();
            // Validate through the canonical session plan before opening a writer.
            _=new SessionPlan(participant,conditions);
            stationParticipant=participant;
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
                        engine.AddNote("Participant ID: "+stationParticipant+"; desktop synthetic rehearsal.");
                        AdvanceWithCalibration();
                        CaptureCalibrationFixture();AcceptCalibrationFixture();AdvanceWithCalibration();
                        lastMessage="Preparing the synthetic test. The administrator can arm when the practice timer finishes.";
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
    }
}
