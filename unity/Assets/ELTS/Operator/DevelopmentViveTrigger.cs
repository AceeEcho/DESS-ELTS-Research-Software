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
            string serial = view.Configuration.Machine.WeaponTriggerSerial;
            if (String.IsNullOrEmpty(serial)) return;
            viveTrigger = new ViveTriggerReader(serial);
            vivePressGate = new TriggerPressGate(TimeSpan.FromSeconds(view.Configuration.Runtime.TriggerLockoutSeconds));
        }

        private void PollViveTrigger()
        {
            if (viveTrigger == null || vivePressGate == null) return;
            viveTrigger.Poll(Time.realtimeSinceStartupAsDouble);
            string context = runId + ":" + engine?.State + ":" + ParticipantInputPhase;
            bool allowed = !IsBusy && !closing && engine != null &&
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
                    LogField.String("trackerSerial",view.Configuration.Machine.WeaponTriggerSerial),
                    LogField.Boolean("available",viveTrigger.Available), LogField.Boolean("pressed",viveTrigger.Pressed),
                    LogField.String("rawButtonsHex",viveTrigger.Buttons.ToString("X")),
                    LogField.NumberValue("packet",viveTrigger.Packet), LogField.Boolean("dispatchAllowed",allowed)})))
                { engine?.Abort("VIVE trigger observation could not be recorded."); allowed = false; }
                else lastViveObservation = observation;
            }
            if (!vivePressGate.Observe(viveTrigger.Available, viveTrigger.Pressed, allowed, context, ticks)) return;
            if (SeparateAdministrator)
                StationParticipantTrigger(ViveTriggerReader.SourceIdentity, "rising", view.Configuration.Machine.WeaponTriggerSerial);
            else
                SubmitDesktopTrigger(ViveTriggerReader.SourceIdentity, "rising", view.Configuration.Machine.WeaponTriggerSerial);
        }
    }
}
