#nullable enable
using System;
using Elts.Logging;
using Elts.Session;
using Elts.Tracking;
using UnityEngine;

namespace Elts.Operator
{
    public sealed partial class DevelopmentSessionPanel
    {
        private ViveTriggerReader? viveTrigger;
        private TriggerPressGate? vivePressGate;
        private string lastViveObservation = "";
        public string GunTriggerStatus => viveTrigger?.Status ?? "Mouse trigger";

        private void InitializeViveTrigger()
        {
            // Automated Unity runs use scripted inputs and must not read lab devices.
            if (Application.isBatchMode) return;
            // weaponTriggerSerial in config/local.json is optional. That file is not shared between
            // machines, so without it the gun is found as SteamVR's hand-held tracker (LiveGunSerial).
            viveTrigger = new ViveTriggerReader(view.Configuration.Machine.WeaponTriggerSerial ?? "");
            vivePressGate = new TriggerPressGate(TimeSpan.FromSeconds(view.Configuration.Runtime.TriggerLockoutSeconds));
            // The same tracker's pose drives live aiming; see DevelopmentLiveTracking.
            InitializeLiveTracking();
        }

        private void PollViveTrigger()
        {
            if (viveTrigger == null || vivePressGate == null) return;
            // Poses first: the pose reader then owns the SteamVR connection that the trigger reader shares.
            PollLiveTrackers();
            viveTrigger.UseSerial(LiveGunSerial);
            viveTrigger.Poll(Time.realtimeSinceStartupAsDouble);
            UpdateLiveTracking();
            string context = runId + ":" + engine?.State + ":" + ParticipantInputPhase;
            // A pull that captures a calibration dot must never also start a test or fire a shot.
            bool allowed = !IsBusy && !closing && engine != null && liveProcedure == null &&
                (TutorialCanInteract || (TestArmed && engine.State == SessionState.BlockReady && tutorial.CanStartScenario)
                 || CanDesktopFire);
            long ticks = clock?.Now.Ticks ?? 0;
            // Log observed changes even while firing is paused. Unavailable remains
            // distinct from released; the timestamp is host observation time.
            string observation = runId + ":" + viveTrigger.Available + ":" + viveTrigger.Buttons;
            if (!IsBusy && !closing && recording?.IsOpen == true && sequence != null && clock != null && observation != lastViveObservation)
            {
                if (!recording.TryRecord(new SessionEvent(sequence.Next(), clock.Now, "ViveTriggerState", new[] {
                    LogField.String("source",ViveTriggerReader.SourceIdentity),
                    LogField.String("trackerSerial",LiveGunSerial.Length>0?LiveGunSerial:"none"),
                    LogField.Boolean("available",viveTrigger.Available), LogField.Boolean("pressed",viveTrigger.Pressed),
                    LogField.String("rawButtonsHex",viveTrigger.Buttons.ToString("X")),
                    LogField.NumberValue("packet",viveTrigger.Packet), LogField.Boolean("dispatchAllowed",allowed)})))
                { engine?.Abort("VIVE trigger observation could not be recorded."); allowed = false; }
                else lastViveObservation = observation;
            }
            if (!vivePressGate.Observe(viveTrigger.Available, viveTrigger.Pressed, allowed, context, ticks)) return;
            // Keep the raw tracker poses that belong to this pull beside the shot they produce.
            LogLiveTrackerPoses(Time.realtimeSinceStartupAsDouble, "trigger");
            if (SeparateAdministrator)
                StationParticipantTrigger(ViveTriggerReader.SourceIdentity, "rising", LiveGunSerial);
            else
                SubmitDesktopTrigger(ViveTriggerReader.SourceIdentity, "rising", LiveGunSerial);
        }
    }
}
