#nullable enable
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>Explanatory drawings, not equipment models or live sensor
    /// readings. Motion draws a relationship once, then leaves it readable.
    /// Drawings use a 100 x 100 logical grid (200 x 100 for wide scenes).</summary>
    public sealed class ParticipantTutorialArt : VisualElement
    {
        private string kind;
        private readonly ParticipantTutorialSettings settings;
        private Painter2D painter = null!;
        private float scale, offsetX, offsetY, progress = 1, strokeBoost = 1;
        private Color ink = new Color(.59f, .70f, .79f);
        private readonly Color neutral = new Color(.79f, .82f, .84f);
        private readonly Color muted = new Color(.27f, .34f, .40f);
        // Page background, used to mask overlaps in outline drawings.
        private static readonly Color Backdrop = new Color(.098f, .118f, .141f);

        public ParticipantTutorialArt(string kind, ParticipantTutorialSettings settings)
        {
            this.kind = kind;
            this.settings = settings;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        // The cue badge reuses one element and swaps its symbol and colour.
        public string Kind { get => kind; set { if (kind == value) return; kind = value; MarkDirtyRepaint(); } }
        public Color Ink { get => ink; set { if (ink == value) return; ink = value; MarkDirtyRepaint(); } }

        public void SetElapsed(double elapsed)
        {
            // This clock freezes on pause and resets on instruction replay.
            // A held instruction must never loop a decorative animation.
            float next = settings.reducedMotion ? 1 : Mathf.Clamp01((float)elapsed / settings.diagramSeconds);
            if (Mathf.Approximately(next, progress)) return;
            progress = next;
            MarkDirtyRepaint();
        }

        private bool Wide => kind is "holding" or "holding-pose" or "equipment-check" or "trigger" or "reload" ||
            kind.StartsWith("tell-", StringComparison.Ordinal) || kind.StartsWith("head-", StringComparison.Ordinal) ||
            kind.StartsWith("step-", StringComparison.Ordinal);

        private void Draw(MeshGenerationContext context)
        {
            if (contentRect.width <= 0 || contentRect.height <= 0) return;
            painter = context.painter2D;
            float logicalWidth = Wide ? 200 : 100;
            // Uniform scaling keeps people, clocks and equipment from stretching.
            scale = Mathf.Min(contentRect.width / logicalWidth, contentRect.height / 100);
            offsetX = (contentRect.width - logicalWidth * scale) / 2;
            offsetY = (contentRect.height - 100 * scale) / 2;
            strokeBoost = 1;
            Stroke(ink, 1.8f);
            painter.fillColor = ink;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            switch (kind)
            {
                case "setup": Setup(); break;
                case "tracking": Tracker(); break;
                case "eye": Eye(); break;
                case "heart": Heart(); break;
                case "target": Target(); break;
                case "study":
                    for (int i = 0; i < 4; i++) Box(8 + i * 23, 25, 16, 50);
                    break;
                case "clock": Clock(); break;
                case "equipment-check": EquipmentCheck(); break;
                case "complete": Stroke(ink, 5); Check(26, 52, 1); break;
                case "check-circle": Circle(50, 50, 42); Stroke(ink, 3.2f); Check(29, 52, .9f); break;
                case "form": Form(); break;
                case "pause": Box(30, 22, 12, 56, true); Box(58, 22, 12, 56, true); break;
                case "stop": Box(27, 27, 46, 46); break;
                case "holding": case "holding-pose": Holding(); break;
                case "arrow": DownArrow(); break;
                case "trigger": case "reload": Replica(kind == "trigger"); break;
                // Cue-badge symbols: listen, act, tell, wait, watch, hold.
                case "speaker": Speaker(); break;
                case "hand": Hand(); break;
                case "bubble": Stroke(ink, 5); Bubble(10, 16, 80, 52); Dots(50, 42, 14, 4.5f); break;
                case "hourglass": Stroke(ink, 5); Hourglass(); break;
                case "watch": Stroke(ink, 5); Eye(); break;
                case "rest": Stroke(ink, 5); Clock(); break;
                case "look": Stroke(ink, 5); Circle(50, 50, 38); Circle(50, 50, 12, true); break;
                default:
                    if (kind.StartsWith("tell-", StringComparison.Ordinal)) Tell(kind.Substring(5));
                    else if (kind.StartsWith("head-", StringComparison.Ordinal) || kind.StartsWith("step-", StringComparison.Ordinal)) Pose(kind);
                    break;
            }
        }

        private void Setup()
        {
            // A person with a head-worn tracker: equipment is fitted to you.
            Stroke(neutral, 1.8f); Person(50, 14);
            Stroke(ink, 2.2f); Box(43, 9, 14, 6); Line(36, 48, 64, 48);
        }

        private void Tracker()
        {
            // A category symbol; no device model, mounting position or live pose.
            Box(32, 31, 36, 38); Circle(50, 50, 6);
            Stroke(muted, 1.4f);
            Line(50, 12, 50, 23); Line(50, 77, 50, 88);
            Line(12, 50, 24, 50); Line(76, 50, 88, 50);
            Stroke(ink, 1.8f);
            Line(18, 29, 18, 18); Line(18, 18, 29, 18);
            Line(71, 82, 82, 82); Line(82, 82, 82, 71);
        }

        private void Eye()
        {
            painter.BeginPath(); painter.MoveTo(P(8, 50));
            painter.BezierCurveTo(P(29, 16), P(71, 16), P(92, 50));
            painter.BezierCurveTo(P(71, 84), P(29, 84), P(8, 50));
            painter.ClosePath(); painter.Stroke(); Circle(50, 50, 14);
            Circle(50, 50, 4, true);
        }

        private void Heart()
        {
            // Static explanatory signal, never a fabricated heart-rate reading.
            Stroke(muted, 1); Line(6, 76, 94, 76);
            Stroke(ink, 2);
            Line(5, 55, 26, 55); Line(26, 55, 37, 32); Line(37, 32, 50, 76);
            Line(50, 76, 63, 43); Line(63, 43, 72, 55); Line(72, 55, 95, 55);
        }

        private void Target()
        {
            // Generic practice symbol only; never drawn over the live target.
            Circle(50, 50, 40); Circle(50, 50, 26); Circle(50, 50, 12); Circle(50, 50, 3.5f, true);
        }

        private void Clock()
        {
            Circle(50, 52, 34); Line(50, 28, 50, 52); Line(50, 52, 69, 62);
            Line(41, 8, 59, 8);
        }

        private void Tell(string message)
        {
            // You (left) speak to the operator (right, holding a clipboard).
            // The bubble's symbol names what to say; the text says it exactly.
            Stroke(neutral, 1.8f); Person(40, 20); Person(160, 20);
            Stroke(muted, 1.6f); Box(168, 52, 15, 20); Box(172, 50, 7, 4); Line(171, 59, 180, 59); Line(171, 65, 180, 65);
            Stroke(ink, 2); Bubble(62, 2, 76, 34);
            Stroke(ink, 2.4f);
            switch (message)
            {
                case "ready": Check(88, 20, .5f); break;
                case "question": QuestionMark(100, 7, .8f); break;
                case "adjust": Arrow(100, 19, 82, 19, 6); Arrow(100, 19, 118, 19, 6); break;
                default: Dots(100, 19, 12, 2.2f); break;
            }
        }

        private void EquipmentCheck()
        {
            // Three paths converge on operator review. No ticks, percentage or
            // green lights that could be mistaken for sensor-health feedback.
            for (int i = 0; i < 3; i++)
            {
                float y = 19 + i * 31;
                Stroke(ink, 1.8f);
                if (i == 0) Inset(4, y - 13, 26, Tracker);
                else if (i == 1) Inset(4, y - 13, 26, Eye);
                else Inset(4, y - 13, 26, Heart);
                Stroke(muted, 1); Line(39, y, 111, y);
                Stroke(ink, 1.8f); DrawnLine(39, y, 111, y, Mathf.Clamp01(progress * 3 - i));
            }
            Stroke(ink, 1.6f); Line(111, 19, 111, 81); Line(111, 50, 135, 50);
            Box(143, 20, 48, 62); Box(155, 15, 24, 10);
            for (int i = 0; i < 3; i++) Line(153, 38 + i * 15, 180, 38 + i * 15);
        }

        private void Holding()
        {
            // Contact schematic only. The operator demonstrates actual stance
            // and hand placement; the prop never slides into the participant.
            Stroke(neutral, 1.5f);
            Circle(55, 18, 9); Line(55, 28, 58, 40); Line(58, 40, 49, 90);
            Line(58, 44, 79, 68); Line(79, 68, 106, 49);
            Line(58, 40, 143, 40); Line(143, 40, 143, 47); Line(143, 47, 88, 47);
            Line(88, 47, 84, 60); Line(84, 60, 79, 58); Line(79, 58, 80, 47);
            Line(80, 47, 58, 53); Line(58, 53, 58, 40); Line(143, 43, 164, 43);
            Stroke(ink, 2.8f);
            Line(58, 40, 58, 53); Line(58, 40, 80, 40); Line(58, 53, 80, 47);
            // The calibration reminder reuses the settled pose, with no empty
            // leader lines where the full lesson's labels would have been.
            if (kind == "holding-pose") return;
            Stroke(ink, 1.1f);
            DrawnLine(76, 40, 103, 24, progress); DrawnLine(103, 24, 119, 24, progress);
            DrawnLine(58, 47, 33, 73, progress); DrawnLine(33, 73, 9, 73, progress);
        }

        private void Replica(bool trigger)
        {
            // Generic side-view silhouette of the non-firing study replica. It
            // only locates the trigger or magazine; it is not a model drawing.
            Stroke(neutral, 1.8f);
            Polygon(new[] { new Vector2(58, 34), new Vector2(16, 39), new Vector2(14, 60), new Vector2(26, 60), new Vector2(58, 48) });
            Box(58, 32, 70, 16); Box(128, 35, 42, 10); Line(170, 40, 188, 40);
            Polygon(new[] { new Vector2(86, 48), new Vector2(80, 74), new Vector2(93, 74), new Vector2(98, 48) }, false);
            Polygon(new[] { new Vector2(98, 48), new Vector2(100, 60), new Vector2(116, 60), new Vector2(118, 48) }, false);
            Stroke(trigger ? ink : neutral, trigger ? 3 : 1.8f); Line(107, 48, 105, 56);
            Stroke(trigger ? neutral : ink, trigger ? 1.8f : 3);
            Polygon(new[] { new Vector2(121, 48), new Vector2(124, 78), new Vector2(136, 77), new Vector2(133, 48) }, false);
            Stroke(ink, 2.4f);
            if (trigger)
            {
                // Ring the trigger, then show the pull toward the grip.
                Circle(107, 54, 14);
                if (progress > .3f) Arrow(122, 84, 98, 84, 6);
            }
            else if (progress > .3f) Arrow(128, 98, 128, 84, 6);
        }

        private void Pose(string pose)
        {
            // Calibration direction pictograms, seen from above with the screen
            // along the top edge (look up is a side view). Outlines only: the
            // configured reference dot stays the single dot on screen.
            if (pose == "head-up") { LookUp(); return; }
            Stroke(muted, 2.4f); Line(24, 6, 176, 6);
            switch (pose)
            {
                case "head-center": TopHead(100, 60, -90, neutral); Stroke(ink, 3); Arrow(100, 38, 100, 14, 7); break;
                case "head-left": TopHead(100, 60, -130, neutral); TurnArrow(100, 60, 31, -70, -165); break;
                case "head-right": TopHead(100, 60, -50, neutral); TurnArrow(100, 60, 31, -110, -15); break;
                case "step-left": TopHead(100, 60, -90, muted); TopHead(46, 60, -90, neutral); Stroke(ink, 3); Arrow(90, 90, 56, 90, 7); break;
                case "step-right": TopHead(100, 60, -90, muted); TopHead(154, 60, -90, neutral); Stroke(ink, 3); Arrow(110, 90, 144, 90, 7); break;
                case "step-center": TopHead(154, 60, -90, muted); TopHead(100, 60, -90, neutral); Stroke(ink, 3); Arrow(144, 90, 110, 90, 7); break;
            }
        }

        private void TopHead(float x, float y, float facing, Color color)
        {
            // Shoulders stay square to the screen; only the nose shows where
            // the head is facing.
            Stroke(color, 9); Line(x - 21, y + 6, x + 21, y + 6);
            painter.fillColor = Backdrop; Circle(x, y, 12, true);
            Stroke(color, 2.4f); Circle(x, y, 12);
            Nose(x, y, 12, facing);
            painter.fillColor = ink;
        }

        private void LookUp()
        {
            // Side view facing the screen on the right: the head tips upward.
            Stroke(muted, 2.4f); Line(176, 8, 176, 94);
            Stroke(neutral, 9); Line(92, 76, 92, 98);
            painter.fillColor = Backdrop; Circle(96, 60, 14, true);
            Stroke(neutral, 2.4f); Circle(96, 60, 14);
            Nose(96, 60, 14, -28);
            painter.fillColor = ink;
            TurnArrow(96, 60, 32, 5, -62);
        }

        private void Nose(float x, float y, float r, float facing)
        {
            float a = facing * Mathf.Deg2Rad, spread = 26 * Mathf.Deg2Rad;
            Polygon(new[] {
                new Vector2(x + Mathf.Cos(a - spread) * r, y + Mathf.Sin(a - spread) * r),
                new Vector2(x + Mathf.Cos(a) * (r + 8), y + Mathf.Sin(a) * (r + 8)),
                new Vector2(x + Mathf.Cos(a + spread) * r, y + Mathf.Sin(a + spread) * r) }, false);
        }

        private void TurnArrow(float x, float y, float r, float from, float to)
        {
            // A curved arrow around the head, ending in an arrowhead tangent to
            // the arc. Angles are degrees, 0 = right, -90 = toward the screen.
            Stroke(ink, 3);
            painter.BeginPath();
            painter.Arc(P(x, y), r * scale, Angle.Degrees(from), Angle.Degrees(to),
                to > from ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
            painter.Stroke();
            float end = to * Mathf.Deg2Rad, sign = to > from ? 1 : -1;
            var tip = new Vector2(x + Mathf.Cos(end) * r, y + Mathf.Sin(end) * r);
            var tangent = new Vector2(-Mathf.Sin(end), Mathf.Cos(end)) * sign;
            var back = -tangent * 8; var side = new Vector2(-back.y, back.x) * .8f;
            Line(tip.x, tip.y, tip.x + back.x + side.x, tip.y + back.y + side.y);
            Line(tip.x, tip.y, tip.x + back.x - side.x, tip.y + back.y - side.y);
        }

        private void Speaker()
        {
            Polygon(new[] { new Vector2(14, 38), new Vector2(30, 38), new Vector2(50, 20), new Vector2(50, 80), new Vector2(30, 62), new Vector2(14, 62) }, true, true);
            Stroke(ink, 6);
            ArcStroke(52, 50, 16, -45, 45); ArcStroke(52, 50, 31, -45, 45);
        }

        private void Hand()
        {
            // Raised open hand: "your turn". Thick round strokes read as fingers.
            Box(30, 50, 40, 34, true);
            Stroke(ink, 10);
            Line(35, 52, 35, 26); Line(45, 50, 45, 16); Line(55, 50, 55, 18); Line(65, 52, 65, 28);
            Line(33, 70, 18, 54); Line(34, 84, 66, 84);
        }

        private void Hourglass()
        {
            Line(26, 10, 74, 10); Line(26, 90, 74, 90);
            Polygon(new[] { new Vector2(34, 10), new Vector2(66, 10), new Vector2(53, 50), new Vector2(66, 90), new Vector2(34, 90), new Vector2(47, 50) });
            Polygon(new[] { new Vector2(40, 86), new Vector2(60, 86), new Vector2(50, 72) }, true, true);
        }

        private void Form()
        {
            Stroke(neutral, 1.6f); Box(23, 10, 54, 80); Box(37, 6, 26, 10);
            Stroke(ink, 1.6f);
            for (int i = 0; i < 3; i++)
            { Circle(35, 33 + i * 18, 2); Line(44, 33 + i * 18, 66, 33 + i * 18); }
        }

        private void DownArrow()
        {
            // Canvas pixels, not device coordinates. This fixed pointer never
            // simulates light or apparatus operation.
            painter.strokeColor = ink; painter.lineWidth = settings.arrowWidth;
            float bottom = 1080 * settings.arrowEndY;
            painter.BeginPath(); painter.MoveTo(new Vector2(960, 540));
            painter.LineTo(new Vector2(960, bottom - 12)); painter.Stroke();
            painter.BeginPath();
            painter.MoveTo(new Vector2(960 - settings.arrowHeadWidth / 2, bottom - settings.arrowHeadWidth / 2));
            painter.LineTo(new Vector2(960, bottom));
            painter.LineTo(new Vector2(960 + settings.arrowHeadWidth / 2, bottom - settings.arrowHeadWidth / 2)); painter.Stroke();
        }

        private void Person(float x, float y)
        {
            Circle(x, y + 10, 9); Line(x, y + 22, x, y + 51);
            Line(x, y + 28, x - 15, y + 43); Line(x, y + 28, x + 15, y + 43);
            Line(x, y + 51, x - 12, y + 76); Line(x, y + 51, x + 12, y + 76);
        }
        private void Bubble(float x, float y, float w, float h)
        {
            painter.BeginPath(); painter.MoveTo(P(x, y)); painter.LineTo(P(x + w, y));
            painter.LineTo(P(x + w, y + h)); painter.LineTo(P(x + 29, y + h));
            painter.LineTo(P(x + 15, y + h + 10)); painter.LineTo(P(x + 15, y + h));
            painter.LineTo(P(x, y + h)); painter.ClosePath(); painter.Stroke();
        }
        private void QuestionMark(float x, float top, float size)
        {
            float r = 6 * size, cy = top + r;
            painter.BeginPath(); painter.Arc(P(x, cy), r * scale, Angle.Degrees(180), Angle.Degrees(400));
            painter.LineTo(P(x, cy + r + 4 * size)); painter.Stroke();
            Circle(x, cy + r + 10 * size, 1.4f * size, true);
        }
        private void Dots(float x, float y, float gap, float r)
        { for (int i = -1; i <= 1; i++) Circle(x + i * gap, y, r, true); }
        private void Arrow(float x, float y, float x2, float y2, float head)
        {
            Line(x, y, x2, y2);
            var back = new Vector2(x - x2, y - y2).normalized * head;
            var side = new Vector2(-back.y, back.x);
            Line(x2, y2, x2 + back.x + side.x, y2 + back.y + side.y);
            Line(x2, y2, x2 + back.x - side.x, y2 + back.y - side.y);
        }
        private void ArcStroke(float x, float y, float r, float from, float to)
        { painter.BeginPath(); painter.Arc(P(x, y), r * scale, Angle.Degrees(from), Angle.Degrees(to)); painter.Stroke(); }
        private void Polygon(Vector2[] points, bool closed = true, bool filled = false)
        {
            painter.BeginPath(); painter.MoveTo(P(points[0].x, points[0].y));
            for (int i = 1; i < points.Length; i++) painter.LineTo(P(points[i].x, points[i].y));
            if (closed) painter.ClosePath();
            if (filled) painter.Fill(); else painter.Stroke();
        }
        private void Inset(float x, float y, float size, Action draw)
        {
            // Draw a 100-unit symbol inside a smaller box of this drawing.
            float s = scale, ox = offsetX, oy = offsetY, boost = strokeBoost;
            offsetX = ox + x * s; offsetY = oy + y * s; scale = s * size / 100; strokeBoost = 100 / size * .45f;
            Stroke(ink, 1.8f); draw();
            scale = s; offsetX = ox; offsetY = oy; strokeBoost = boost;
        }
        private void Check(float x, float y, float size)
        { Line(x, y, x + 16 * size, y + 16 * size); Line(x + 16 * size, y + 16 * size, x + 48 * size, y - 18 * size); }
        private void Stroke(Color color, float width)
        { painter.strokeColor = color; painter.lineWidth = scale * width * strokeBoost; }
        private Vector2 P(float x, float y) => new Vector2(offsetX + x * scale, offsetY + y * scale);
        private void DrawnLine(float x, float y, float x2, float y2, float amount)
        { if (amount > 0) Line(x, y, Mathf.Lerp(x, x2, amount), Mathf.Lerp(y, y2, amount)); }
        private void Line(float x, float y, float x2, float y2)
        { painter.BeginPath(); painter.MoveTo(P(x, y)); painter.LineTo(P(x2, y2)); painter.Stroke(); }
        private void Circle(float x, float y, float r, bool filled = false)
        {
            painter.BeginPath(); painter.Arc(P(x, y), r * scale, Angle.Degrees(0), Angle.Degrees(360));
            if (filled) painter.Fill(); else painter.Stroke();
        }
        private void Box(float x, float y, float w, float h, bool filled = false)
        {
            painter.BeginPath(); painter.MoveTo(P(x, y)); painter.LineTo(P(x + w, y));
            painter.LineTo(P(x + w, y + h)); painter.LineTo(P(x, y + h)); painter.ClosePath();
            if (filled) painter.Fill(); else painter.Stroke();
        }
    }
}
