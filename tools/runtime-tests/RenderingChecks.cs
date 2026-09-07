using System;
using Elts.Geometry;
using Elts.Rendering;
internal static class RenderingChecks
{
    private static int checks;
    private static void Check(bool ok) { if (!ok) throw new Exception("Rendering invariant failed at " + checks); checks++; }
    private static void Near(double a, double b) => Check(Math.Abs(a-b) < 1e-10);
    private static void Main()
    {
        var random = new Random(20260907);
        // Rotated screens prevent a hard-coded axis/sign solution from passing.
        for (int i=0;i<100;i++)
        {
            var rotation=Quaterniond.FromAxisAngle(new Vector3d(0,1,0),random.NextDouble()-0.5);
            var screen=new ScreenPlane(new Vector3d(-1,0.5,3),rotation.Rotate(new Vector3d(1,0,0)),new Vector3d(0,1,0),2,1.2);
            var eye=screen.Origin + screen.U*random.NextDouble()*2 + screen.V*random.NextDouble() - screen.Normal*(1+random.NextDouble()*2);
            var projection=new OffAxisProjection(screen,eye,0.05,100);
            foreach(var c in new[]{(0.0,0.0,-1.0,-1.0),(2.0,0.0,1.0,-1.0),(0.0,1.2,-1.0,1.0),(2.0,1.2,1.0,1.0)})
            {var p=projection.ProjectNdc(screen.Origin+screen.U*c.Item1+screen.V*c.Item2);Near(p.X,c.Item3);Near(p.Y,c.Item4);}
        }
        var raw=new RigidPose(new Vector3d(1,2,3),Quaterniond.Identity);
        var predicted=RenderHeadPrediction.Predict(raw,new Vector3d(2,0,0),0.02);
        Near(predicted!.Value.Position.X,1.04);Near(raw.Position.X,1);
        Check(!RenderHeadPrediction.Predict(null,Vector3d.Zero,0.02).HasValue);
        var t=new ReplayTransport(10);t.Play();t.Advance(3);Near(t.PositionSeconds,3);t.Pause();t.Advance(3);Near(t.PositionSeconds,3);
        t.Scrub(8);t.Play();t.Advance(4);Near(t.PositionSeconds,10);Check(!t.IsPlaying);t.Play();Near(t.PositionSeconds,0);
        bool failed=false;try {new OffAxisProjection(new ScreenPlane(Vector3d.Zero,new Vector3d(1,0,0),new Vector3d(0,1,0),1,1),new Vector3d(0,0,1),0.1,100);} catch(ArgumentException){failed=true;}Check(failed);
        Console.WriteLine("PASS: "+checks+" rendering checks");
    }
}
