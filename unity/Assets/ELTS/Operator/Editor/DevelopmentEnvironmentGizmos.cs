using UnityEditor;
using UnityEngine;

namespace Elts.Operator.Editor
{
    /// <summary>Scene-view labels distinguish the physical display from virtual targets.</summary>
    internal static class DevelopmentEnvironmentGizmos
    {
        [DrawGizmo(GizmoType.Selected | GizmoType.NonSelected)]
        private static void DrawLabels(DevelopmentEnvironment environment, GizmoType gizmoType)
        {
            var screen=environment.DisplayReference?.Find("Physical screen position");
            if(screen!=null)Label(screen.position+screen.up*0.5f,"PHYSICAL DISPLAY · synthetic geometry",Color.white);
            var host=environment.transform;
            var head=host.Find("Participant head tracker");
            var weapon=host.Find("Weapon tracker and barrel");
            foreach(var child in environment.GetComponentsInChildren<Transform>())
            {
                if(child.name=="Default desktop head pose (edit preview)")head=child;
                if(child.name=="Default desktop weapon pose (edit preview)")weapon=child;
                if(child.name=="Configured target spawn volume (edit preview)")
                    Label(child.position+Vector3.up*0.55f,"VIRTUAL TARGET SPACE",new Color(1,0.7f,0.3f));
            }
            if(head!=null)Label(head.position+Vector3.up*0.25f,"HEAD + LOOK DIRECTION",Color.cyan);
            if(weapon!=null)Label(weapon.position+Vector3.down*0.32f,"WEAPON + BORE DIRECTION",new Color(1,0.7f,0.3f));
        }
        private static void Label(Vector3 position,string text,Color color)
        {
            var style=new GUIStyle(EditorStyles.boldLabel);style.normal.textColor=color;
            Handles.Label(position,text,style);
        }
    }
}
