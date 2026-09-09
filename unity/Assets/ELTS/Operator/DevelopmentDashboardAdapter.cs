#nullable enable
using System;
using System.IO;
using Elts.Session;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>
    /// The only dashboard/session bridge. New UI modules read a snapshot; existing
    /// buttons still enter the established Act/Begin paths and the session engine.
    /// No module can declare physical readiness or overwrite configuration.
    /// </summary>
    public sealed class DashboardSessionSnapshot
    {
        public SessionState? State;
        public bool Busy, Closed, WriterOpen, WriterHealthy, TrackingReady, Calibrated;
        public int BlockIndex;
        public double RemainingSeconds, PracticeSeconds, BreakSeconds, ObservedRateHz, DiskFreeGb;
        public long DroppedSamples;
        public string RunDirectory="", Condition="", ControllerState="Not created";
        public bool PreparationEditable => !Busy && (!State.HasValue || Closed);
        public bool Running => State==SessionState.BlockRunning;
    }

    public sealed partial class DevelopmentSessionPanel
    {
        private OperatorDashboard? dashboard;
        public OperatorDashboard Dashboard => dashboard!;

        private void InitializeDashboard()
        {
            // Test runners get their own workspace, never the administrator's settings.
            bool testing=Array.IndexOf(Environment.GetCommandLineArgs(),"-runTests")>=0;
            string workspace=testing
                ? Path.Combine(Application.temporaryCachePath,"dashboard-tests",Guid.NewGuid().ToString("N")+".json")
                : Path.Combine(Application.persistentDataPath,"operator-workspace-v1.json");
            dashboard=new OperatorDashboard(root,view,ReadDashboardSnapshot,workspace);
            Bind("openRecordingFolder",()=> {
                try
                {
                    string? directory=recording?.RunDirectory;
                    if(String.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                        throw new InvalidOperationException("Create a recording before opening its folder.");
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute=true });
                }
                catch(Exception exception) { lastMessage=exception.Message; }
            });
        }

        private DashboardSessionSnapshot ReadDashboardSnapshot()
        {
            var pair=acquisition?.Snapshot;
            long sampleAge=pair.HasValue && clock!=null?clock.Now.Ticks-pair.Value.Head.Acquisition.Timestamp.Ticks:long.MaxValue;
            return new DashboardSessionSnapshot {
                State=engine?.State, Busy=IsBusy, Closed=closing,
                WriterOpen=recording?.IsOpen==true, WriterHealthy=recording?.WriterHealthy==true,
                TrackingReady=!setupPending && engine!=null && acquisition!=null && acquisition.Failure==null
                    && sampleAge>=0 && sampleAge<=MaximumSampleAgeSeconds*TimeSpan.TicksPerSecond
                    && pair?.Head.HasValidPose==true && pair?.Weapon.HasValidPose==true,
                Calibrated=AcceptedCalibrationPath!=null, BlockIndex=engine?.CurrentBlockIndex??0,
                RemainingSeconds=engine?.RemainingSeconds??0,
                PracticeSeconds=view.Configuration.Session.PracticeDurationSeconds,
                BreakSeconds=view.Configuration.Session.BreakDurationSeconds,
                ObservedRateHz=acquisition?.ActualElapsedRateHz??0,
                DiskFreeGb=diskFreeBytes/(double)(1024L*1024*1024),
                DroppedSamples=recording?.DroppedSampleCount??0,
                RunDirectory=recording?.RunDirectory??"", Condition=engine?.CurrentCondition??"",
                ControllerState=mockLink?.State.ToString()??"Not created"
            };
        }
    }
}
