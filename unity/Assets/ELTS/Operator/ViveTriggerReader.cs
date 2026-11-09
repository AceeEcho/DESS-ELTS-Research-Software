#nullable enable
using System;
using System.Runtime.InteropServices;
using System.Text;
using Valve.VR;

namespace Elts.Operator
{
    /// <summary>Read only the configured tracker. No poses, role changes, haptics,
    /// compositor access or apparatus commands. Call from the Unity main thread.</summary>
    internal sealed class ViveTriggerReader : IDisposable
    {
        public const string SourceIdentity = "vive-tracker-trigger-v1";
        private readonly string serial;
        private CVRSystem? system;
        private bool ownsRuntime;
        private double retryAt;
        public bool Available { get; private set; }
        public bool Pressed { get; private set; }
        public ulong Buttons { get; private set; }
        public uint Packet { get; private set; }
        public string Status { get; private set; } = "Connecting to SteamVR";

        public ViveTriggerReader(string serial) { this.serial = serial; }

        public void Poll(double seconds)
        {
            Available = false; Pressed = false; Buttons = 0; Packet = 0;
            if (String.IsNullOrEmpty(serial)) { Status = "Mouse trigger"; return; }
            try
            {
                if (system == null)
                {
                    if (seconds < retryAt) return;
                    retryAt = seconds + 2;
                    system = OpenVR.System;
                    if (system == null)
                    {
                        var error = EVRInitError.None;
                        system = OpenVR.Init(ref error, EVRApplicationType.VRApplication_Background);
                        ownsRuntime = system != null && error == EVRInitError.None;
                        if (!ownsRuntime) { system = null; Status = "Start SteamVR"; return; }
                    }
                }
                if (system == null) return;
                Status = "Gun tracker disconnected";
                for (uint index = 0; index < OpenVR.k_unMaxTrackedDeviceCount; index++)
                {
                    var kind = system.GetTrackedDeviceClass(index);
                    if (kind != ETrackedDeviceClass.Controller && kind != ETrackedDeviceClass.GenericTracker) continue;
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    var name = new StringBuilder(128);
                    system.GetStringTrackedDeviceProperty(index, ETrackedDeviceProperty.Prop_SerialNumber_String, name, 128, ref error);
                    if (error != ETrackedPropertyError.TrackedProp_Success || name.ToString() != serial) continue;
                    var state = new VRControllerState_t();
                    Available = system.IsTrackedDeviceConnected(index) && system.GetControllerState(index, ref state,
                        (uint)Marshal.SizeOf(typeof(VRControllerState_t)));
                    if (Available)
                    {
                        Buttons = state.ulButtonPressed; Packet = state.unPacketNum;
                        Pressed = (Buttons & (1UL << (int)EVRButtonId.k_EButton_SteamVR_Trigger)) != 0;
                        Status = Pressed ? "Gun trigger pressed" : "Gun trigger released";
                        retryAt = seconds + 2;
                        return;
                    }
                    Status = "Gun input unavailable: assign Held in hand and Left/Right Hand in SteamVR";
                    break;
                }
                // Reacquire after runtime restarts, without shutting down a runtime
                // owned by another OpenVR consumer in this process.
                if (seconds >= retryAt && ownsRuntime) { Dispose(); retryAt = seconds + 2; }
            }
            catch (Exception error)
            { Status = "SteamVR input unavailable: " + error.GetType().Name; Dispose(); retryAt = seconds + 2; }
        }

        public void Dispose()
        {
            if (ownsRuntime) OpenVR.Shutdown();
            ownsRuntime = false; system = null; Available = false; Pressed = false;
        }
    }
}
