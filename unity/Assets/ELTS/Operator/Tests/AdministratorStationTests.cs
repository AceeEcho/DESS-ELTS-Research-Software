using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Elts.Clock;
using Elts.Session;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Elts.Operator.Tests
{
    public sealed class AdministratorStationTests
    {
        [Test]
        public void CanceledAdministratorCommandsCannotRunLater()
        {
            var pending=new AdministratorServer.PendingCommand(new JObject{["action"]="abort"});
            pending.CancelIfQueued();
            Assert.That(pending.TryBegin(),Is.False);
            Assert.That((bool)JObject.Parse(pending.Completion.Task.Result)["ok"],Is.False);
        }
        private sealed class Input : IDesktopControls
        {
            public DesktopControlFrame Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero);
            public DesktopControlFrame Read(IPanel panel)
            {var value=Frame;Frame=new DesktopControlFrame(value.Focused,value.Pointer,value.Movement);return value;}
        }
        [UnityTest]
        public IEnumerator ArmedParticipantStartsAllFourRecordedBlocks()
        {
            var host=new GameObject("Separate administrator station fixture");
            var view=host.AddComponent<DevelopmentView>();var session=host.AddComponent<DevelopmentSessionPanel>();
            var clock=new ManualSharedClock(DateTimeOffset.UtcNow);
            typeof(DevelopmentSessionPanel).GetField("clock",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(session,clock);
            var station=host.AddComponent<DevelopmentTestStation>();station.OpenAdministratorAutomatically=false;
            var input=new Input();station.Controls=input;
            try
            {
                for(int i=0;i<5;i++)yield return null;
                Assert.That(view.ParticipantOnly,Is.True);
                Assert.That(view.ParticipantCamera.targetTexture,Is.Null);
                Assert.That(view.OperatorCamera.targetTexture,Is.Not.Null);
                Assert.That(session.PanelRoot.resolvedStyle.display,Is.EqualTo(DisplayStyle.None));
                Assert.That(station.ParticipantRoot.Query<Button>().ToList(),Is.Empty,"Participant view must contain no administrator buttons");
                station.ApplyCommand(new JObject{["action"]="startParticipant",["participant"]="station-test",["conditionOrder"]=new JArray("WE_FT","WE_MT","NE_FT","NE_MT")});
                float limit=Time.realtimeSinceStartup+20;
                while(session.Engine?.State!=SessionState.BlockReady && Time.realtimeSinceStartup<limit)
                {clock.Advance(TimeSpan.FromSeconds(0.1));yield return null;}
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),session.StationMessage);
                string run=session.StationRecordingPath;
                for(int block=0;block<4;block++)
                {
                    Assert.That(session.StationParticipantTrigger(),Is.False,"Unarmed participant cannot begin");
                    // A mouse press made before arming cannot later start a block.
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    station.ApplyCommand(new JObject{["action"]="arm"});yield return null;
                    clock.Advance(TimeSpan.FromSeconds(30));yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"Armed waiting must not start the block clock");
                    Assert.That(station.ParticipantRoot.Q<Label>("participantTitle").text,Is.EqualTo("Shoot to begin"));
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady));
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    input.Frame=new DesktopControlFrame(false,new Vector2(300,300),Vector3.zero);yield return null;
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockReady),"Focus loss cancels a pending start click");
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,pressed:true);yield return null;
                    input.Frame=new DesktopControlFrame(true,new Vector2(300,300),Vector3.zero,released:true);yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockRunning),session.StationMessage);
                    Assert.That(session.Engine.RemainingSeconds,Is.EqualTo(300).Within(0.001));
                    Assert.That(session.StationShots,Is.Zero,"The start prompt consumes its click");
                    Assert.That(station.ParticipantRoot.Q("participantPrompt").ClassListContains("participant-hidden"),Is.True);
                    var scenario=(DevelopmentSessionScenario)typeof(DevelopmentSessionPanel).GetField("scenario",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(session);
                    var point=scenario.Positions.First().Value;
                    var viewport=view.ParticipantCamera.WorldToViewportPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                    var bounds=station.ParticipantRoot.worldBound;
                    var pointer=new Vector2(bounds.xMin+viewport.x*bounds.width,bounds.yMax-viewport.y*bounds.height);
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,pressed:true);yield return null;
                    clock.Advance(TimeSpan.FromSeconds(0.03));
                    input.Frame=new DesktopControlFrame(true,pointer,Vector3.zero,released:true);yield return null;
                    Assert.That(session.StationShots,Is.EqualTo(1));Assert.That(session.StationHits,Is.EqualTo(1));
                    clock.Advance(TimeSpan.FromSeconds(300));yield return null;
                    Assert.That(session.Engine.State,Is.EqualTo(SessionState.BlockEnded));
                    station.ApplyCommand(new JObject{["action"]="next"});
                    clock.Advance(TimeSpan.FromSeconds(5));yield return null;
                    station.ApplyCommand(new JObject{["action"]="next"});yield return null;
                }
                Assert.That(session.Engine.State,Is.EqualTo(SessionState.SessionComplete));
                limit=Time.realtimeSinceStartup+15;
                while(session.IsBusy && Time.realtimeSinceStartup<limit)yield return null;
                Assert.That(session.IsBusy,Is.False);
                var events=File.ReadAllLines(Path.Combine(run,"events.ndjson")).Select(JObject.Parse).ToArray();
                Assert.That(events.Count(e=>(string)e["eventType"]=="TestArmed"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ParticipantStartTrigger"),Is.EqualTo(4));
                Assert.That(events.Count(e=>(string)e["eventType"]=="ShotFired"),Is.EqualTo(4));
                foreach(var start in events.Where(e=>(string)e["eventType"]=="ParticipantStartTrigger"))
                {
                    string blockId=(string)start["payload"]["blockId"];
                    var blockStart=events.Single(e=>(string)e["eventType"]=="BlockStarted" && (string)e["payload"]["blockId"]==blockId);
                    Assert.That((long)start["sequence"],Is.LessThan((long)blockStart["sequence"]),"Participant trigger must precede the timed block transition");
                }
                for(int i=1;i<events.Length;i++)Assert.That((long)events[i]["monotonicTicks"],Is.GreaterThanOrEqualTo((long)events[i-1]["monotonicTicks"]));
                var state=JObject.Parse(station.StateJson());Assert.That(state["blocks"].Count(),Is.EqualTo(4));
                Assert.That(state["blocks"].All(row=>(string)row["status"]=="Completed"),Is.True);
                File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath,"../../diagnostics/station-pipeline-run.txt")),run);
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
        }
    }
}
