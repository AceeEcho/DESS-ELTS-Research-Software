#nullable enable
using System;
using System.Collections.Generic;
using Elts.Calibration;
using Elts.Geometry;

static class CalibrationChecks
{
 static int passed; static void True(bool v,string n){if(!v)throw new Exception("FAIL: "+n);passed++;} static void Throws(Action a,string n){try{a();}catch(ArgumentException){passed++;return;}throw new Exception("FAIL: "+n);}
 static void Main(){
  var tip=new Vector3d(.02,-.03,.11); var pivot=new Vector3d(1,2,3); var poses=new List<RigidPose>();
  for(int i=0;i<240;i++){var q=Quaterniond.FromAxisAngle(new Vector3d(1+i%3,2+(i%5),3+(i%7)),(i%37)*.13);poses.Add(new RigidPose(pivot-q.Rotate(tip),q));}
  var s=PivotCalibrationSolver.Solve(poses);True((s.TipOffsetMeters-tip).Length<1e-8&&s.RmsResidualMeters<1e-8,"pivot recovers known offset from 200 diversified poses");
  var noisy=new List<RigidPose>();for(int i=0;i<poses.Count;i++){var p=poses[i];noisy.Add(new RigidPose(p.Position+new Vector3d(Math.Sin(i)*.0002,Math.Cos(i)*.0002,0),p.Orientation));}var ns=PivotCalibrationSolver.Solve(noisy);True(ns.RmsResidualMeters>.00001&&ns.RmsResidualMeters<.001,"pivot reports bounded synthetic noise residual");
  var flat=new List<RigidPose>();for(int i=0;i<200;i++)flat.Add(new RigidPose(new Vector3d(i*.001,0,0),Quaterniond.Identity));Throws(()=>PivotCalibrationSolver.Solve(flat),"degenerate pivot poses reject");
  var c=CornerCalibrationSolver.Solve(Vector3d.Zero,new Vector3d(2,0,0),new Vector3d(.1,1,0),new Vector3d(2.1,1,0));True(Math.Abs(c.WidthMeters-2)<1e-9&&Math.Abs(c.HeightMeters-Math.Sqrt(1.01))<1e-9&&Math.Abs(Vector3d.Dot(c.U,c.V))<1e-9,"corner solver orthogonalizes while preserving V length");
  var z=WeaponZeroSolver.Solve(new Vector3d(0,0,1),new Vector3d(0,0,-1));True((z.Rotate(new Vector3d(0,0,1))-new Vector3d(0,0,-1)).Length<1e-9,"antiparallel zero is stable");
  var v=AngularVerification.Summarize(new double[]{.1,.2,.3,.4,.5,.6,.7,.8,.9});True(Math.Abs(v.MeanDegrees-.5)<1e-12&&Math.Abs(v.MaxDegrees-.9)<1e-12,"3x3 summary reports mean and max");
  var wizard=new CalibrationWizard();wizard.BeginCapture();wizard.Review();wizard.Redo();wizard.BeginCapture();wizard.Review();var r=wizard.Accept("synthetic");True(!r.StudyReady&&r.Mode=="synthetic","synthetic acceptance cannot grant study readiness");Console.WriteLine("PASS: "+passed+" calibration checks"); }
}
