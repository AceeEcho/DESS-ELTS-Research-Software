using System;
using Elts.Tracking;

static class TriggerChecks
{
    static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    static void Main()
    {
        var gate = new TriggerPressGate(TimeSpan.FromMilliseconds(20));
        long ms = TimeSpan.TicksPerMillisecond;
        Check(!gate.Observe(true,true,true,"practice",0),"Held on connect must not fire");
        Check(!gate.Observe(true,false,true,"practice",ms),"Release arms without firing");
        Check(gate.Observe(true,true,true,"practice",30*ms),"Fresh pull fires");
        Check(!gate.Observe(true,true,true,"practice",3000*ms),"Long hold never repeats");
        Check(!gate.Observe(false,false,true,"practice",3001*ms),"Disconnect never fires");
        Check(!gate.Observe(true,true,true,"practice",3002*ms),"Reconnect held never fires");
        gate.Observe(true,false,true,"practice",3003*ms);
        Check(gate.Observe(true,true,true,"practice",3004*ms),"Pull after reconnect release fires");
        gate.Observe(true,false,true,"practice",3005*ms);
        Check(!gate.Observe(true,true,true,"practice",3006*ms),"Lockout rejects rapid repeated pull");
        Check(!gate.Observe(true,true,true,"scored",4000*ms),"A held start shot cannot score next phase");
        gate.Observe(true,false,false,"scored",4001*ms);
        Check(!gate.Observe(true,true,true,"scored",4002*ms),"Pull while paused cannot fire on resume");
        gate.Observe(true,false,true,"scored",4003*ms);
        Check(gate.Observe(true,true,true,"scored",4004*ms),"Fresh pull after resume fires");
        Console.WriteLine("PASS: trigger hold, release, lockout, disconnect, phase and pause checks");
    }
}
