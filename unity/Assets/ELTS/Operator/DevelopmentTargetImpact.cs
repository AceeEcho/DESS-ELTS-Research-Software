#nullable enable
using Elts.Geometry;
using UnityEngine;

namespace Elts.Operator
{
    /// <summary>Presentation data from an accepted hit. It never awards damage or changes a target's recorded pose.</summary>
    public readonly struct DevelopmentTargetImpact
    {
        public readonly string TargetId;
        public readonly Vector3d Point, Direction;
        public readonly bool Killed;
        public DevelopmentTargetImpact(string targetId,Vector3d point,Vector3d direction,bool killed)
        {TargetId=targetId;Point=point;Direction=direction;Killed=killed;}
    }

    /// <summary>A brief, non-gory spark at the actual hitscan intersection.</summary>
    internal sealed class DevelopmentHitSpark : MonoBehaviour
    {
        private readonly LineRenderer[] streaks=new LineRenderer[5];
        private float started;
        public static void Create(Transform parent,DevelopmentTargetImpact impact,Material material)
        {
            var host=new GameObject("Confirmed target hit");host.transform.SetParent(parent,false);
            var spark=host.AddComponent<DevelopmentHitSpark>();
            var incoming=ToUnity(impact.Direction).normalized;
            // Follow the visible target from the point where the shot landed.
            host.transform.SetPositionAndRotation(ToUnity(impact.Point)-incoming*.015f,
                Quaternion.LookRotation(-incoming,Vector3.up));
            spark.started=Time.unscaledTime;
            for(int i=0;i<spark.streaks.Length;i++)
            {
                var part=new GameObject("Impact streak "+i);part.layer=DevelopmentView.StimulusLayer;part.transform.SetParent(host.transform,false);
                var line=part.AddComponent<LineRenderer>();line.sharedMaterial=material;line.positionCount=2;
                line.useWorldSpace=true;line.numCapVertices=2;
                line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
                spark.streaks[i]=line;
            }
            spark.LateUpdate();
        }
        private void LateUpdate()
        {
            const float duration=.16f;
            float t=Mathf.Clamp01((Time.unscaledTime-started)/duration);
            if(t>=1){Destroy(gameObject);return;}
            for(int i=0;i<streaks.Length;i++)
            {
                float angle=i*2*Mathf.PI/streaks.Length;
                var direction=transform.right*Mathf.Cos(angle)+transform.up*Mathf.Sin(angle);
                float reach=(i%2==0?.085f:.055f)*(1+t*.45f);
                streaks[i].startWidth=.012f*(1-t);streaks[i].endWidth=.002f*(1-t);
                streaks[i].SetPosition(0,transform.position+direction*(t*.025f));
                streaks[i].SetPosition(1,transform.position+direction*reach);
            }
        }
        private static Vector3 ToUnity(Vector3d p)=>new Vector3((float)p.X,(float)p.Y,(float)p.Z);
    }
}
