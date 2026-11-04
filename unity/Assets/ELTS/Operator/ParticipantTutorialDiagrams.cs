#nullable enable
using Elts.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    // Lesson layouts are kept separate from refresh/binding. These methods only
    // create visuals: they cannot accept a capture, fire, reload or start a phase.
    // Each screen shows one picture and at most a few one-word labels; the
    // heading, cue badge and single instruction line carry the words.
    public sealed partial class ParticipantTutorialView
    {
        private static readonly Color Confirmed = new Color(.61f, .78f, .69f);

        private void BuildWelcome()
        {
            // Setup is highlighted as the next stage; nothing is marked complete.
            var route = Element(content, "tutorial-route");
            RouteStop(route, "Setup", "setup", 0, true);
            RouteArrow(route, .6f);
            RouteStop(route, "Practice", "target", .8f, false);
            RouteArrow(route, 1.4f);
            RouteStop(route, "Study", "study", 1.6f, false);
        }

        // "You → Operator" scenes share one picture; the bubble's symbol changes.
        private void BuildQuestions() => Conversation("question");
        private void BuildReadiness() => Conversation("ready");
        private void BuildComfort() => Conversation("adjust");
        private void BuildDebrief() => Conversation("question");

        private void BuildEquipment()
        {
            var row = Element(content, "tutorial-equipment");
            EquipmentRole(row, "tracking", "Tracking", 0);
            EquipmentRole(row, "eye", "Eye tracker", .5f);
            EquipmentRole(row, "heart", "Heart rate", 1);
        }

        private void EquipmentRole(VisualElement row, string icon, string title, float delay)
        {
            var column = Element(row, "tutorial-equipment-role");
            Art(column, icon, "tutorial-equipment-art");
            Text(column, title, "tutorial-picture-label");
            Reveal(column, delay);
        }

        private void BuildEquipmentReview() => Art(content, "equipment-check", "tutorial-review-art", true);

        private void BuildCalibration(bool gun)
        {
            // The illustration is a short preparation cue. The configured dot
            // replaces the entire cue, and stays stationary during review/replay.
            var settings = session.TutorialSettings;
            var cue = Extra(new VisualElement(), "tutorial-calibration-cue");
            illustration = cue;
            if (gun && settings.showHoldingDiagram) Art(cue, "holding-pose", "tutorial-calibration-holding");
            dot = Extra(new VisualElement(), "tutorial-dot");
            float x = gun ? settings.gunDotX : settings.headDotX;
            float y = gun ? settings.gunDotY : settings.headDotY;
            int index = session.Tutorial.HeadCalibrationIndex;
            if (!gun)
            {
                // Side-step dots sit symmetrically about the centre dot.
                if (index == 4) x = settings.headDotX - settings.headStepOffsetX;
                if (index == 5) x = settings.headDotX + settings.headStepOffsetX;
                cue.AddToClassList("tutorial-hidden");
                // A direction pictogram above the clear central field. It is
                // drawn in outline only so the reference dot is the one dot.
                string[] poses = { "head-center", "head-left", "head-right", "head-up", "step-left", "step-right", "step-center" };
                Extra(new ParticipantTutorialArt(poses[index], settings), "tutorial-pose");
                detail.text = "Step " + (index + 1) + " of " + poses.Length;
            }
            float size = settings.dotDiameter;
            dot.style.width = size; dot.style.height = size;
            if (ColorUtility.TryParseHtmlString(settings.dotColor, out var dotColor)) dot.style.backgroundColor = dotColor;
            // A static outline ring makes the single reference dot easy to find.
            if (settings.dotRingDiameter > size)
            {
                var ring = Element(dot, "tutorial-dot-ring");
                float ringSize = settings.dotRingDiameter, inset = (size - ringSize) / 2;
                ring.style.width = ringSize; ring.style.height = ringSize; ring.style.left = inset; ring.style.top = inset;
                ring.style.borderTopColor = ring.style.borderBottomColor = ring.style.borderLeftColor = ring.style.borderRightColor = dotColor;
            }
            if (!gun && index is >= 1 and <= 3)
            {
                // Look cues use the full participant window, the same distance
                // from whichever edge they sit beside.
                dot.RemoveFromHierarchy();
                layer.Add(dot);
                float margin = settings.edgeDotMarginPx;
                dot.style.translate = new Translate(Length.Percent(index == 2 ? 50 : -50), Length.Percent(-50), 0);
                if (index == 1) { dot.style.left = margin; dot.style.top = Length.Percent(y * 100); }
                else if (index == 2) { dot.style.right = margin; dot.style.top = Length.Percent(y * 100); }
                else { dot.style.left = Length.Percent(x * 100); dot.style.top = margin; }
            }
            else
            {
                dot.style.left = x * 1920 - size / 2;
                dot.style.top = y * 1080 - size / 2;
            }
        }

        private void BuildHolding()
        {
            if (!session.TutorialSettings.showHoldingDiagram) return;
            var figure = Element(content, "tutorial-contact-figure");
            Art(figure, "holding", "tutorial-contact-art", true);
            Text(figure, "Stock", "tutorial-stock-label");
            Text(figure, "Shoulder", "tutorial-shoulder-label");
        }

        private void BuildCalibrationSummary()
        {
            var row = Element(content, "tutorial-check-row");
            CheckItem(row, "Head tracker", session.Tutorial.HeadAccepted, 0);
            CheckItem(row, "Gun tracker", session.Tutorial.GunAccepted, .4f);
        }

        private void CheckItem(VisualElement row, string title, bool accepted, float delay)
        {
            // Shown only from accepted operator reviews, never from timing.
            var item = Element(row, "tutorial-check-item");
            Art(item, accepted ? "check-circle" : "clock", "tutorial-check-art").Ink = accepted ? Confirmed : Color.gray;
            Text(item, title, "tutorial-picture-label");
            Reveal(item, delay);
        }

        private void BuildControlLesson(TutorialStage stage)
        {
            bool firing = stage == TutorialStage.Fire;
            var lesson = Extra(new VisualElement(), "tutorial-control-lesson");
            Art(lesson, firing ? "trigger" : "reload", "tutorial-control-art", true);
            actionStatus = Text(lesson, "", "tutorial-control-status");
            AmmoHint();
            detail.text = firing ? "Desktop rehearsal · click to fire" : "Desktop rehearsal · press R";
        }

        private void AmmoHint()
        {
            // A callout centred above the real ammunition display. Refresh
            // measures that display, so the caret always points at its middle.
            var hint = new VisualElement { pickingMode = PickingMode.Ignore };
            hint.AddToClassList("tutorial-ammo-hint");
            Text(hint, "Your ammo", "tutorial-ammo-hint-label");
            Element(hint, "tutorial-ammo-hint-caret");
            layer.Add(hint); extras.Add(hint); ammoHint = hint;
        }

        private void BuildControlConfirmation()
        {
            var confirmation = Extra(new VisualElement(), "tutorial-control-lesson tutorial-control-confirmation");
            // The heading and instruction already say what was observed.
            Art(confirmation, "check-circle", "tutorial-confirmation-art").Ink = Confirmed;
        }

        private void BuildPracticeGuide()
        {
            var strip = Extra(new VisualElement(), "tutorial-practice-guide");
            PracticeAction(strip, "target", "Aim");
            Text(strip, "→", "tutorial-inline-arrow");
            PracticeAction(strip, "trigger", "Fire");
            Text(strip, "→", "tutorial-inline-arrow");
            PracticeAction(strip, "reload", "Reload");
        }

        private void PracticeAction(VisualElement strip, string icon, string label)
        {
            var action = Element(strip, "tutorial-practice-action");
            Art(action, icon, "tutorial-practice-icon");
            Text(action, label, "tutorial-practice-label");
        }

        private void BuildPracticeSummary()
        {
            // Firing and reloading were observed; aiming was operator-confirmed.
            var row = Element(content, "tutorial-check-row");
            SummaryControl(row, "target", "Aim", 0);
            SummaryControl(row, "trigger", "Fire", .3f);
            SummaryControl(row, "reload", "Reload", .6f);
        }

        private void SummaryControl(VisualElement row, string icon, string label, float delay)
        {
            var item = Element(row, "tutorial-check-item");
            Art(item, icon, "tutorial-summary-icon");
            var line = Element(item, "tutorial-summary-line");
            Art(line, "complete", "tutorial-receipt-check").Ink = Confirmed;
            Text(line, label, "tutorial-picture-label");
            Reveal(item, delay);
        }

        private void BuildDemonstration()
        {
            Extra(new ParticipantTutorialArt("arrow", session.TutorialSettings), "tutorial-arrow");
            Extra(new Label("ELTS"), "tutorial-arrow-label");
            footer.text = "SIMULATED DEMONSTRATION · NO HARDWARE OUTPUTS";
        }

        private void BuildScenarioSequence(TutorialStage stage)
        {
            var row = Element(content, "tutorial-scenario-sequence");
            bool timeLimits = stage != TutorialStage.Scenarios;
            bool breaks = stage == TutorialStage.BreakOverview;
            for (int i = 0; i < 4; i++)
            {
                var group = Element(row, "tutorial-scenario-group");
                var scenario = Element(group, "tutorial-scenario-card");
                Text(scenario, "SCENARIO", "tutorial-kicker");
                Text(scenario, (i + 1).ToString(), "tutorial-scenario-number");
                var time = Element(scenario, "tutorial-time-limit");
                Art(time, "clock", "tutorial-clock-icon");
                Text(time, "Time limit", "tutorial-caption");
                time.style.visibility = timeLimits ? Visibility.Visible : Visibility.Hidden;
                // Existing cards stay still across all three overview lessons.
                // Only time limits or the break markers are newly revealed.
                if (stage == TutorialStage.TimeLimits) Reveal(time, i * .25f);
                // A static example duration, never a running countdown.
                var rest = Element(group, "tutorial-sequence-rest");
                Text(rest, "→", "tutorial-inline-arrow");
                Text(rest, i == 3 ? "Final\nbreak" : "Break", "tutorial-break-title");
                Text(rest, FormatTime(session.Engine?.BreakDurationSeconds ?? 0), "tutorial-break-duration");
                rest.style.visibility = breaks ? Visibility.Visible : Visibility.Hidden;
                if (breaks) Reveal(rest, i * .45f);
            }
        }

        private void BuildStartCue()
        {
            var row = Element(content, "tutorial-check-row");
            var pull = Element(row, "tutorial-check-item");
            Art(pull, "trigger", "tutorial-start-art", true);
            Text(pull, "Pull the trigger", "tutorial-picture-label");
            RouteArrow(row, .4f);
            var begin = Element(row, "tutorial-check-item");
            Art(begin, "clock", "tutorial-check-art");
            Text(begin, "Scenario starts", "tutorial-picture-label");
            Reveal(begin, .4f);
        }

        private void BuildCheckpoint()
        {
            var column = Hero(session.Tutorial.LastAttemptAbnormal ? "stop" : "check-circle");
            saveStatus = Text(column, "", "tutorial-hero-caption");
        }

        private void BuildRest(bool final)
        {
            // The large timer is bound to the real break clock in Refresh.
            // This quiet strip only explains what comes after the break.
            var strip = Extra(new VisualElement(), "tutorial-rest-route");
            Text(strip, "Rest now", "tutorial-rest-current");
            Text(strip, "→", "tutorial-inline-arrow");
            Text(strip, final ? "Post-study form" : "Next scenario", "tutorial-rest-next");
        }

        private void BuildNextScenario() => Hero("hourglass");
        private void BuildHold(bool stopped) => Hero(stopped ? "stop" : "pause");
        private void BuildForm() => Hero("form");

        private void BuildThanks()
        {
            var column = Hero("check-circle");
            Text(column, "Thank you for your time.", "tutorial-thanks-message");
        }

        private VisualElement Hero(string icon)
        {
            var column = Element(content, "tutorial-hero");
            var art = Art(column, icon, "tutorial-hero-art");
            if (icon == "check-circle") art.Ink = Confirmed;
            return column;
        }

        private void Conversation(string message)
        {
            var scene = Element(content, "tutorial-conversation");
            Art(scene, "tell-" + message, "tutorial-conversation-art");
            var names = Element(scene, "tutorial-conversation-names");
            Text(names, "You", "tutorial-conversation-name");
            Text(names, "Operator", "tutorial-conversation-name");
        }

        private void RouteStop(VisualElement parent, string title, string icon, float delay, bool next)
        {
            var stop = Element(parent, next ? "tutorial-route-stop tutorial-route-next" : "tutorial-route-stop");
            Art(stop, icon, "tutorial-route-icon");
            Text(stop, title, "tutorial-route-title");
            Reveal(stop, delay);
        }
        private void RouteArrow(VisualElement parent, float delay)
        { Reveal(Text(parent, "→", "tutorial-route-arrow"), delay); }
        private ParticipantTutorialArt Art(VisualElement parent, string kind, string className, bool animate = false)
        {
            var art = new ParticipantTutorialArt(kind, session.TutorialSettings);
            art.AddToClassList(className); parent.Add(art);
            if (animate) drawings.Add(art);
            return art;
        }
        private static VisualElement Element(VisualElement parent, string classes)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string className in classes.Split(' ')) element.AddToClassList(className);
            parent.Add(element); return element;
        }
        private static Label Text(VisualElement parent, string value, string classes)
        {
            var label = new Label(value) { pickingMode = PickingMode.Ignore };
            foreach (string className in classes.Split(' ')) label.AddToClassList(className);
            parent.Add(label); return label;
        }
        private void Reveal(VisualElement element, float delay)
        { reveals.Add((element, delay)); }
    }
}
