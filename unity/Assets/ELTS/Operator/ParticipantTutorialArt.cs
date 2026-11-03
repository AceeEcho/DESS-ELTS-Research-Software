#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Resolution-independent diagrams. Equipment symbols identify a
    /// category; they intentionally do not prescribe device models or placement.</summary>
    public sealed class ParticipantTutorialArt : VisualElement
    {
        private readonly string kind;
        private readonly ParticipantTutorialSettings settings;
        private Painter2D painter=null!;
        private float sx,sy,offsetX;
        private readonly Color ink=new Color(.59f,.70f,.79f);
        private readonly Color neutral=new Color(.79f,.82f,.84f);
        public ParticipantTutorialArt(string kind,ParticipantTutorialSettings settings)
        {
            this.kind=kind;this.settings=settings;pickingMode=PickingMode.Ignore;
            generateVisualContent+=Draw;
        }
        private void Draw(MeshGenerationContext context)
        {
            if(contentRect.width<=0 || contentRect.height<=0)return;
            painter=context.painter2D;sx=contentRect.width/100;sy=contentRect.height/100;offsetX=0;
            if(kind=="holding")
            {sx=sy=Mathf.Min(contentRect.width/200,contentRect.height/100);offsetX=(contentRect.width-200*sx)/2;}
            painter.lineWidth=Mathf.Min(sx,sy)*2.2f;painter.strokeColor=ink;painter.fillColor=ink;
            painter.lineCap=LineCap.Round;painter.lineJoin=LineJoin.Round;
            switch(kind)
            {
                case "setup":case "tracking":
                    Circle(50,30,13);Line(50,45,50,70);Line(50,50,28,62);Line(50,50,72,62);
                    Line(50,70,35,92);Line(50,70,65,92);Box(8,16,14,20);Box(78,16,14,20);break;
                case "eye":
                    painter.BeginPath();painter.MoveTo(P(8,50));painter.BezierCurveTo(P(29,16),P(71,16),P(92,50));
                    painter.BezierCurveTo(P(71,84),P(29,84),P(8,50));painter.ClosePath();painter.Stroke();Circle(50,50,14);break;
                case "heart":
                    Line(5,55,26,55);Line(26,55,37,32);Line(37,32,50,76);Line(50,76,63,43);Line(63,43,72,55);Line(72,55,95,55);break;
                case "practice":case "target":
                    Circle(50,50,37);Circle(50,50,23);Circle(50,50,7);Line(78,17,91,17);Line(91,17,91,30);break;
                case "study":Box(8,10,35,34);Box(57,10,35,34);Box(8,57,35,34);Box(57,57,35,34);break;
                case "clock":Circle(50,50,37);Line(50,24,50,50);Line(50,50,70,61);break;
                case "questions":
                    Box(10,15,80,58);Line(27,73,27,88);Line(27,88,44,73);Circle(33,44,2,true);Circle(50,44,2,true);Circle(67,44,2,true);break;
                case "complete":Circle(50,50,38);Line(29,49,44,65);Line(44,65,73,33);break;
                case "form":Box(23,10,54,80);Line(37,28,65,28);Line(37,45,65,45);Line(37,62,65,62);break;
                case "ready":Circle(50,50,38);Line(36,50,64,50);Line(54,39,65,50);Line(65,50,54,61);break;
                case "posture":Posture();break;
                case "holding":Holding();break;
                case "arrow":
                    // Reference geometry is in 1920x1080 canvas pixels. The shaft
                    // begins at the center and points below the physical display.
                    painter.lineWidth=settings.arrowWidth;
                    float bottom=1080*settings.arrowEndY;
                    painter.BeginPath();painter.MoveTo(new Vector2(960,540));painter.LineTo(new Vector2(960,bottom-12));painter.Stroke();
                    painter.BeginPath();painter.MoveTo(new Vector2(960-settings.arrowHeadWidth/2,bottom-settings.arrowHeadWidth/2));
                    painter.LineTo(new Vector2(960,bottom));painter.LineTo(new Vector2(960+settings.arrowHeadWidth/2,bottom-settings.arrowHeadWidth/2));painter.Stroke();break;
            }
        }
        private void Posture()
        {
            // Side view: upright participant facing a display. No joint-angle or
            // foot-position prescription, gaze ray or tracking-success feedback.
            Circle(36,16,8);Line(35,26,35,64);Line(35,34,42,57);Line(35,64,29,92);Line(35,64,41,92);
            Box(70,12,3,66);Line(71.5f,78,71.5f,92);Line(62,92,81,92);
        }
        private void Holding()
        {
            // A schematic of contact only, not a replica-specific handling guide.
            painter.strokeColor=neutral;
            Circle(55,18,9);Line(55,28,58,40);Line(58,40,49,90);
            Line(58,44,79,68);Line(79,68,106,49);
            // Simple non-firing prop silhouette; no ammunition or operation detail.
            Line(58,40,143,40);Line(143,40,143,47);Line(143,47,88,47);Line(88,47,84,60);Line(84,60,79,58);
            Line(79,58,80,47);Line(80,47,58,53);Line(58,53,58,40);Line(143,43,164,43);
            painter.strokeColor=ink;painter.lineWidth=Mathf.Min(sx,sy)*3.2f;
            Line(58,40,58,53);Line(58,40,80,40);Line(58,53,80,47);
            painter.lineWidth=Mathf.Min(sx,sy)*1.3f;
            Line(76,40,100,24);Line(100,24,114,24);
            Line(58,47,35,70);Line(35,70,16,70);
        }
        private Vector2 P(float x,float y)=>new Vector2(offsetX+x*sx,y*sy);
        private void Line(float x,float y,float x2,float y2)
        {painter.BeginPath();painter.MoveTo(P(x,y));painter.LineTo(P(x2,y2));painter.Stroke();}
        private void Circle(float x,float y,float r,bool filled=false)
        {painter.BeginPath();painter.Arc(P(x,y),r*Mathf.Min(sx,sy),Angle.Degrees(0),Angle.Degrees(360));if(filled)painter.Fill();else painter.Stroke();}
        private void Box(float x,float y,float w,float h)
        {painter.BeginPath();painter.MoveTo(P(x,y));painter.LineTo(P(x+w,y));painter.LineTo(P(x+w,y+h));painter.LineTo(P(x,y+h));painter.ClosePath();painter.Stroke();}
    }
}
