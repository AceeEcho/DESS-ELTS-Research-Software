#nullable enable
using System;
using System.Collections.Generic;
using Elts.Geometry;
using Elts.Scenario;

namespace Elts.Operator
{
    /// <summary>Inspector-adjustable synthetic game values. These are development
    /// staging choices, not measured apparatus or approved study parameters.</summary>
    [Serializable]
    public sealed class DevelopmentGameSettings
    {
        public const int DefaultMagazineCapacity=20;
        public int magazineCapacity=DefaultMagazineCapacity;
        public float laneSpacingM=.9f, staticDepthOffsetM=1.1f, movingDepthOffsetM=1.95f, coverDepthOffsetM=1.6f;
        public float runInStartXM=2.9f, runInSeconds=2.2f, entryStaggerSeconds=.65f;
        public float peekCycleSeconds=3.2f, peekDistanceM=.53f;
        public DevelopmentGameSettings Copy()=> (DevelopmentGameSettings)MemberwiseClone();
        public void Validate()
        {
            if(magazineCapacity<1 || magazineCapacity>100 ||
                !Finite(laneSpacingM,.65f,1.25f) || !Finite(staticDepthOffsetM,.9f,1.4f) ||
                !Finite(movingDepthOffsetM,1.8f,2.4f) || !Finite(coverDepthOffsetM,1.4f,1.9f) ||
                coverDepthOffsetM<=staticDepthOffsetM+.3f || movingDepthOffsetM<=coverDepthOffsetM+.25f ||
                !Finite(runInStartXM,2.9f,4) || !Finite(runInSeconds,.5f,8) ||
                !Finite(entryStaggerSeconds,0,1.5f) ||
                !Finite(peekCycleSeconds,1.5f,8) || !Finite(peekDistanceM,.4f,.8f))
                throw new ArgumentException("Development game settings are outside their safe synthetic ranges.");
        }
        private static bool Finite(float value,float min,float max)=>!float.IsNaN(value) && !float.IsInfinity(value) && value>=min && value<=max;
    }

    /// <summary>Repeatable development-stage layout in virtual room metres.
    /// Static actors stand in front of cover; moving actors run behind it.</summary>
    internal static class DevelopmentScenarioLayout
    {
        public const int TargetCount=3;
        public static IReadOnlyList<HumanoidCover> Covers(Vector3d center,DevelopmentGameSettings settings)
            =>new[]{
                new HumanoidCover("crate-stack",new Vector3d(center.X-settings.laneSpacingM,-.43,center.Z+settings.coverDepthOffsetM),new Vector3d(.30,.58,.19)),
                new HumanoidCover("concrete-barrier",new Vector3d(center.X,-.43,center.Z+settings.coverDepthOffsetM),new Vector3d(.33,.58,.19)),
                new HumanoidCover("drums",new Vector3d(center.X+settings.laneSpacingM,-.43,center.Z+settings.coverDepthOffsetM),new Vector3d(.29,.58,.19))};
        public static Vector3d Position(TargetEntity target,Vector3d center,bool moving,double activeSeconds,DevelopmentGameSettings settings)
        {
            int number=1;
            string id=target.Id;
            int.TryParse(id.Substring(id.LastIndexOf('-')+1),out number);
            int lane=Math.Abs(number-1)%TargetCount;
            double x=center.X+(lane-1)*settings.laneSpacingM;
            if(!moving)return new Vector3d(x,HumanoidGeometry.StandingCenterY,center.Z+settings.staticDepthOffsetM);
            double age=Math.Max(0,activeSeconds-target.SpawnedAt.Ticks/(double)TimeSpan.TicksPerSecond-lane*settings.entryStaggerSeconds);
            if(age<settings.runInSeconds)
            {
                // Every new actor enters from outside the participant frustum.
                double start=center.X+(lane==2?settings.runInStartXM:-settings.runInStartXM);
                double progress=age/settings.runInSeconds;
                double bend=Math.Max(0,Math.Min(1,(age-(settings.runInSeconds-.4))/.4));
                return new Vector3d(start+(x-start)*progress,
                    HumanoidGeometry.StandingCenterY+(HumanoidGeometry.CrouchedCenterY-HumanoidGeometry.StandingCenterY)*bend,
                    center.Z+settings.movingDepthOffsetM);
            }
            double phase=(age-settings.runInSeconds)%settings.peekCycleSeconds;
            double normalized=phase/settings.peekCycleSeconds;
            double peek=normalized<.17?0:normalized<.31?(normalized-.17)/.14:normalized<.52?1:normalized<.66?1-(normalized-.52)/.14:0;
            // Smooth starts/stops make the movement readable while remaining a
            // pure function of active time and lane.
            peek=peek*peek*(3-2*peek);
            double side=lane==2?-1:1;
            return new Vector3d(x+side*settings.peekDistanceM*peek,
                HumanoidGeometry.CrouchedCenterY+(HumanoidGeometry.StandingCenterY-HumanoidGeometry.CrouchedCenterY)*peek,
                center.Z+settings.movingDepthOffsetM);
        }
    }
}
