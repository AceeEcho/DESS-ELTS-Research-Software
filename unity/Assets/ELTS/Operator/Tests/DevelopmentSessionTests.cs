using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Elts.Clock;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class DevelopmentSessionTests
    {
        [Test]
        public void FullFourConditionSessionRunsThroughUnityRuntime()
        {
            var clock=new ManualSharedClock(DateTimeOffset.UnixEpoch);
            var sink=new Events();
            var link=new Link();
            var plan=new SessionPlan("synthetic-unity-test",new[]{"WE_MT","NE_MT","WE_FT","NE_FT"});
            var engine=new SessionEngine(clock,new SessionEventSequence(),plan,new SessionTiming(1,1,true),sink,link);
            var report=engine.StartSetup(new SessionReadinessInput { TwoDisplays=true,SteamVrRunning=true,
                TrackersBoundAndValidForTwoSeconds=true,ConfigurationHashesComputed=true,
                DiskFreeBytes=2L*1024*1024*1024,LogWriterAlive=true,WeLinkReachable=true });
            Assert.That(report.SetupAccepted,Is.True);
            Assert.That(report.StudyReady,Is.False);
            engine.Advance();engine.Advance();
            clock.Advance(TimeSpan.FromSeconds(1));engine.Tick();
            for(int index=0;index<4;index++)
            {
                Assert.That(engine.CurrentCondition,Is.EqualTo(plan.ConditionOrder[index]));
                engine.StartBlock();
                Assert.That(engine.TargetsActive,Is.True);
                engine.AddNote("synthetic runtime test");engine.EmitProvisionalSyncMarker("fixture");
                clock.Advance(TimeSpan.FromSeconds(300));engine.Tick();
                Assert.That(engine.State,Is.EqualTo(SessionState.BlockEnded));
                Assert.That(engine.TargetsActive,Is.False);Assert.That(link.Enabled,Is.False);
                engine.Advance();clock.Advance(TimeSpan.FromSeconds(1));engine.Advance();
            }
            Assert.That(engine.State,Is.EqualTo(SessionState.SessionComplete));
            Assert.That(sink.Items.FindAll(item=>item.EventType=="BlockEnded").Count,Is.EqualTo(4));
            for(int index=1;index<sink.Items.Count;index++)
            {
                Assert.That(sink.Items[index].Sequence,Is.GreaterThan(sink.Items[index-1].Sequence));
                Assert.That(sink.Items[index].Timestamp.Ticks,Is.GreaterThanOrEqualTo(sink.Items[index-1].Timestamp.Ticks));
            }
        }

        [UnityTest]
        public IEnumerator ToolkitControlsStartRecordingAndAbortPractice()
        {
            var host=new GameObject("Session UI test");
            host.AddComponent<DevelopmentView>();
            var panel=host.AddComponent<DevelopmentSessionPanel>();
            try
            {
                yield return null;
                var root=panel.PanelRoot;
                Assert.That(root.Q<Button>("createSession"),Is.Not.Null);
                Assert.That(root.Q<VisualElement>("sessionPanel").worldBound.width,Is.GreaterThan(100));
                Assert.That(root.Q<VisualElement>("sessionPanel").worldBound.height,Is.GreaterThan(100));
                root.Q<Foldout>("calibrationWizard").value=true;
                yield return null;
                Assert.That(root.Q<TextField>("eyeOffset").worldBound.yMax,
                    Is.LessThanOrEqualTo(root.Q<TextField>("calibrationThresholds").worldBound.yMin),
                    "Expanded calibration fields must retain separate layout space");
                root.Q<TextField>("participant").value="synthetic-ui-"+Guid.NewGuid().ToString("N");
                Submit(root.Q<Button>("createSession"));
                float deadline=Time.realtimeSinceStartup+15;
                while((panel.Engine==null || panel.Engine.State==SessionState.Idle || panel.IsBusy) && Time.realtimeSinceStartup<deadline)
                    yield return null;
                Assert.That(panel.Engine,Is.Not.Null);
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.SessionSetup));
                Submit(root.Q<Button>("advance"));
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.Calibration));
                Assert.That(root.Q<Button>("advance").enabledSelf,Is.False);
                root.Q<TextField>("calibrationThresholds").value="0.000001,5,0.5,1,2";
                Submit(root.Q<Button>("captureCalibration"));
                Assert.That(root.Q<Button>("acceptCalibration").enabledSelf,Is.False,"Out-of-limit fixture must not be accepted");
                Submit(root.Q<Button>("redoCalibration"));
                root.Q<TextField>("calibrationThresholds").value="2,5,0.5,1,2";
                Submit(root.Q<Button>("captureCalibration"));
                Assert.That(root.Q<Button>("acceptCalibration").enabledSelf,Is.True,root.Q<Label>("message").text);
                Submit(root.Q<Button>("acceptCalibration"));
                Assert.That(panel.AcceptedCalibrationPath,Is.Not.Null,root.Q<Label>("message").text);
                Assert.That(File.Exists(panel.AcceptedCalibrationPath),Is.True);
                var calibrationText=File.ReadAllText(panel.AcceptedCalibrationPath);
                StringAssert.Contains("\"studyReady\": false",calibrationText);
                StringAssert.Contains("\"configurationSha256\"",calibrationText);
                StringAssert.Contains("\"poses\"",calibrationText);
                Submit(root.Q<Button>("advance"));
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.Practice));
                root.Q<TextField>("note").value="Toolkit event path exercised";
                Submit(root.Q<Button>("addNote"));
                root.Q<TextField>("abortReason").value="synthetic UI test finished";
                Submit(root.Q<Button>("abort"));
                Assert.That(panel.Engine.State,Is.EqualTo(SessionState.Aborted));
                yield return null;
                deadline=Time.realtimeSinceStartup+15;
                while(panel.IsBusy && Time.realtimeSinceStartup<deadline) yield return null;
                Assert.That(panel.IsBusy,Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        private static void Submit(Button button)
        {
            using(var submit=NavigationSubmitEvent.GetPooled()) { submit.target=button;button.SendEvent(submit); }
        }
        private sealed class Events : ISessionEventSink
        {
            public readonly List<SessionEvent> Items=new List<SessionEvent>();
            public bool TryRecord(SessionEvent item) { Items.Add(item);return true; }
        }
        private sealed class Link : ISessionLink
        {
            public bool Enabled;
            public bool TrySetEnabled(bool enabled) { Enabled=enabled;return true; }
        }
    }
}
