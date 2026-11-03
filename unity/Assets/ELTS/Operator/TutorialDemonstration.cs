#nullable enable
using System;
using Elts.Clock;
using Elts.EltsLink;

namespace Elts.Operator
{
    /// <summary>
    /// In-memory lifecycle rehearsal only. A concrete MockEltsLink (not a supplied
    /// transport) makes it impossible for this tutorial to energize hardware.
    /// Completion requires acknowledged STOP and DISARM, not an animation timeout.
    /// </summary>
    public sealed class TutorialDemonstration
    {
        private readonly ISharedClock clock;
        private readonly MockEltsLink link;
        private readonly Action<string> record;
        private long sequence;
        private double endAt;
        public bool Active { get; private set; }
        public TutorialDemonstration(ISharedClock clock,Action<string> record)
        { this.clock=clock;this.record=record;link=new MockEltsLink(new SimulatedEltsController(clock)); }
        public void Start(string participant,int attempt,double simulationSeconds)
        {
            if(Active)throw new InvalidOperationException("The simulated demonstration is already active.");
            if(simulationSeconds<=0 || double.IsNaN(simulationSeconds) || double.IsInfinity(simulationSeconds))throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
            Send(EltsMessage.Command(EltsMessageType.Hello,++sequence,clock.Now.Ticks,applicationVersion:"tutorial-simulation-v1"));
            // Own cleanup before ARM: even a failed event write after an ACK
            // must stop/disarm this in-memory controller.
            Active=true;
            try
            {
                Send(EltsMessage.Command(EltsMessageType.Arm,++sequence,clock.Now.Ticks));
                Send(EltsMessage.Command(EltsMessageType.Start,++sequence,clock.Now.Ticks,blockContext:
                    new EltsBlockContext(participant,"tutorial-demo-"+attempt,"WE_TUTORIAL",TimeSpan.FromSeconds(simulationSeconds).Ticks)));
                endAt=clock.Now.Elapsed.TotalSeconds+simulationSeconds;
            }
            catch { Stop();throw; }
        }
        // Null means still running. False means interrupted/failed, never complete.
        public bool? Tick()
        {
            if(!Active)return null;
            link.Advance();
            if(link.State!=EltsLinkState.Active || !link.LedPermission){Stop();return false;}
            if(clock.Now.Elapsed.TotalSeconds>=endAt){Stop();return true;}
            Send(EltsMessage.Command(EltsMessageType.Ping,++sequence,clock.Now.Ticks),false);
            return null;
        }
        public void Stop()
        {
            if(!Active)return;
            // Stop locally even if recording the acknowledgement fails. The
            // caller then interrupts the tutorial and retains its raw recording.
            try { Send(EltsMessage.Command(EltsMessageType.Stop,++sequence,clock.Now.Ticks,reason:"operator")); }
            finally { Active=false;Send(EltsMessage.Command(EltsMessageType.Disarm,++sequence,clock.Now.Ticks)); }
        }
        private void Send(EltsMessage command,bool log=true)
        {
            var response=link.Send(command);
            if(response.Type!=EltsMessageType.Ack)throw new InvalidOperationException("Simulated ELTS rejected "+command.Type+": "+response.Error);
            if(log)record(command.Type.ToString());
        }
    }
}
