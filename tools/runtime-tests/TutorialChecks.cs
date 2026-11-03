using System;
using System.Collections.Generic;
using Elts.Session;

static class TutorialChecks
{
    static int checks;
    static void Check(bool value,string message){if(!value)throw new Exception(message);checks++;}
    static void Reject(Action action,string message)
    {try{action();}catch(InvalidOperationException){checks++;return;}throw new Exception(message);}
    static void Continue(ParticipantTutorial flow)=>flow.Continue(flow.Revision);
    static void Explain(ParticipantTutorial flow)=>flow.Tick(60,7);
    static ParticipantTutorial ThroughSetup()
    {
        var flow=new ParticipantTutorial();flow.Begin(flow.Revision);Explain(flow);Explain(flow);
        Explain(flow);Check(flow.Stage==TutorialStage.ReadyForSetup,"readiness never times out");
        int revision=flow.Revision;Continue(flow);Reject(()=>flow.Continue(revision),"duplicate continue skipped equipment");
        Continue(flow);Explain(flow);Continue(flow);
        Check(flow.Stage==TutorialStage.HeadCalibration,"head calibration precedes replica handling");
        Explain(flow);Reject(()=>Continue(flow),"head calibration was accepted by elapsed time");
        flow.AcceptCalibration(flow.Revision);Continue(flow);flow.AcceptCalibration(flow.Revision);Explain(flow);Explain(flow);
        Check(flow.Stage==TutorialStage.Aim && flow.HeadAccepted && flow.GunAccepted,"both reviews are required before aiming");
        Continue(flow);Reject(()=>Continue(flow),"firing advance accepted without a shot");
        flow.ObserveReload();flow.ObserveFire();Continue(flow);
        Reject(()=>Continue(flow),"a reload from an earlier step counted");flow.ObserveReload();Continue(flow);
        Check(flow.Stage==TutorialStage.PracticeReady,"controls lead to manual practice start");
        return flow;
    }
    static ParticipantTutorial ThroughTutorial()
    {
        var flow=ThroughSetup();flow.PracticeStarted(flow.Revision);Explain(flow);
        Check(flow.Stage==TutorialStage.PracticeRunning,"reading time cannot finish practice");
        flow.PracticeFinished();Continue(flow);Explain(flow);
        Check(flow.CanRequestDemo(7),"ready operator permits one demonstration cue");flow.MarkDemoRequested();
        flow.Replay(flow.Revision);Explain(flow);Check(!flow.CanRequestDemo(7),"replay refired demonstration");
        flow.SetPaused(true,flow.Revision);flow.SetPaused(false,flow.Revision);
        Check(!flow.CanRequestDemo(7),"pause and resume refired demonstration");
        Explain(flow);Check(flow.Stage==TutorialStage.Demonstration,"animation time fabricated device completion");
        flow.DemonstrationEnded(true);int revision=flow.Revision;flow.DemonstrationEnded(true);
        Check(flow.Revision==revision,"duplicate completion advanced twice");
        Continue(flow);Explain(flow);Explain(flow);Explain(flow);
        Check(flow.Stage==TutorialStage.FirstReady && flow.CanArm,"overview leads to operator arm hold");
        return flow;
    }
    static void Main()
    {
        var paused=new ParticipantTutorial();paused.Begin(0);paused.SetPaused(true,paused.Revision);Explain(paused);
        Check(paused.Stage==TutorialStage.Welcome && paused.ElapsedSeconds==0,"explanation pause did not freeze progression");
        paused.Replay(paused.Revision);Check(paused.Paused,"replay unexpectedly resumed explanation");
        var flow=ThroughTutorial();
        string[] conditions={"NE_FT","WE_FT","NE_MT","WE_MT"};
        for(int i=0;i<4;i++)
        {
            string condition=conditions[i],attempt=condition+"-attempt-01";bool final=i==3;
            flow.ObserveSession(SessionState.BlockReady,true,condition,attempt,false,false,final);
            Check(flow.Stage==TutorialStage.Armed,"armed start prompt missing");
            flow.Replay(flow.Revision);Check(flow.Stage==TutorialStage.Armed,"replay started scenario");
            flow.ObserveSession(SessionState.BlockReady,false,condition,attempt,false,false,final);
            Check(flow.CanArm,"disarm did not return to readiness");
            flow.ObserveSession(SessionState.BlockRunning,false,condition,attempt,false,false,final);
            flow.ObserveSession(SessionState.BlockPaused,false,condition,attempt,false,false,final);
            Check(flow.Stage==TutorialStage.ScenarioPaused,"pause advanced the scenario");
            flow.ObserveSession(SessionState.BlockEnded,false,condition,attempt,false,false,final);
            Explain(flow);Check(flow.Stage==(final?TutorialStage.FinalEnded:TutorialStage.ScenarioEnded),"waiting-for-save screen timed into a break");
            flow.ObserveSession(SessionState.Break,false,condition,attempt,false,false,final);
            Check(flow.Stage==(final?TutorialStage.FinalBreak:TutorialStage.BreakRunning),"actual break did not select countdown");
            flow.ObserveSession(final?SessionState.SessionComplete:SessionState.BlockReady,false,condition,attempt,false,false,final);
            Check(flow.Stage==(final?TutorialStage.Form:TutorialStage.NextReady),"break end routed incorrectly");
        }
        Check(flow.AllScenariosCompleted,"four unique normal completions were not retained");
        Explain(flow);Check(flow.Stage==TutorialStage.Form,"form auto-confirmed");
        flow.ConfirmForm(flow.Revision);Explain(flow);Check(flow.Stage==TutorialStage.ThankYou,"final screen restarted");
        var exceptions=ThroughTutorial();
        foreach(string condition in conditions)
            exceptions.ObserveSession(SessionState.BlockEnded,false,condition,condition+"-attempt-01",false,true,true);
        Check(!exceptions.AllScenariosCompleted,"skips claimed four successful scenarios");
        exceptions.MarkBreakSkipped();exceptions.ObserveSession(SessionState.SessionComplete,false,"","",false,false,true);
        Check(exceptions.LastBreakSkipped,"skipped final break lost exception wording");
        exceptions.ObserveSession(SessionState.Aborted,false,"","",false,false,true);
        Check(exceptions.Stage==TutorialStage.Interrupted,"abort left normal completion prompts visible");
        var failedDemo=ThroughSetup();failedDemo.PracticeStarted(failedDemo.Revision);failedDemo.PracticeFinished();Continue(failedDemo);
        failedDemo.MarkDemoRequested();failedDemo.DemonstrationEnded(false);Explain(failedDemo);
        Check(failedDemo.Stage==TutorialStage.Demonstration && !failedDemo.CanRequestDemo(0),"failed demonstration silently retried");
        failedDemo.RepeatDemonstration(failedDemo.Revision);Explain(failedDemo);
        Check(failedDemo.CanRequestDemo(7),"explicit repeat did not permit a fresh cue");
        var rejected=new ParticipantTutorial((_,__)=>throw new InvalidOperationException("sink failed"));
        Reject(()=>rejected.Begin(0),"recording failure was ignored");Check(!rejected.Started,"failed record advanced tutorial");
        Console.WriteLine("PASS: "+checks+" tutorial progression checks");
    }
}
