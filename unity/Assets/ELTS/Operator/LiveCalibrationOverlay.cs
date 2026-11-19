#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Elts.Operator
{
    /// <summary>
    /// Participant-window layer for live-tracking steps: one dot at a known place
    /// on the glass and short instructions. Dot positions are fractions of the
    /// whole window, so the window must fill the screen (F11) for the dot to sit
    /// where the calibration assumes. Presentation only; it reads no tracker.
    /// </summary>
    internal sealed class LiveCalibrationOverlay
    {
        /// <summary>Text sizes are authored for a 1080-pixel-high window and scale with it.</summary>
        private const float ReferenceHeight = 1080;
        private const float MinimumDotPixels = 10;
        /// <summary>The ring is this many dot diameters across; it helps the eye find a small dot.</summary>
        private const float RingScale = 3.2f;
        private static readonly Color Backdrop = new Color(0.035f, 0.05f, 0.07f, 1f);
        private static readonly Color Ink = new Color(0.96f, 0.95f, 0.93f, 1f);
        private static readonly Color Muted = new Color(0.62f, 0.67f, 0.72f, 1f);
        private static readonly Color Alert = new Color(1f, 0.72f, 0.3f, 1f);

        private readonly VisualElement host, backdrop, ring, dot, markerEdge, markerRing, markerCore;
        private readonly Label heading, instruction, status, keys, banner;
        /// <summary>Aim marker ring diameter for a 1080-pixel-high window (pixels).</summary>
        private const float MarkerReferencePixels = 34;
        private static readonly Color MarkerInk = new Color(1f, 0.27f, 0.2f, 1f);
        private static readonly Color MarkerEdge = new Color(0.03f, 0.03f, 0.03f, 0.85f);

        public LiveCalibrationOverlay(VisualElement host)
        {
            this.host = host;
            backdrop = new VisualElement { name = "liveCalibration", pickingMode = PickingMode.Ignore };
            backdrop.style.position = Position.Absolute;
            backdrop.style.left = 0; backdrop.style.top = 0; backdrop.style.right = 0; backdrop.style.bottom = 0;
            backdrop.style.backgroundColor = Backdrop;
            backdrop.style.display = DisplayStyle.None;
            heading = Text(backdrop, Ink, true, TextAnchor.UpperCenter);
            instruction = Text(backdrop, Ink, false, TextAnchor.UpperCenter);
            status = Text(backdrop, Muted, false, TextAnchor.LowerCenter);
            keys = Text(backdrop, Muted, false, TextAnchor.LowerCenter);
            ring = Circle(backdrop); ring.style.backgroundColor = Color.clear;
            ring.style.borderLeftColor = ring.style.borderRightColor = ring.style.borderTopColor = ring.style.borderBottomColor = Muted;
            dot = Circle(backdrop); dot.style.backgroundColor = Ink;
            banner = Text(host, Alert, true, TextAnchor.UpperLeft);
            banner.style.left = 16; banner.style.top = 12; banner.style.display = DisplayStyle.None;
            // The aim marker: a dark-edged ring with a centre point, drawn from plain
            // elements so its centre is exactly the computed point (a text "+" is not).
            markerEdge = Circle(host); markerEdge.style.backgroundColor = Color.clear; Border(markerEdge, MarkerEdge);
            markerRing = Circle(host); markerRing.style.backgroundColor = Color.clear; Border(markerRing, MarkerInk);
            markerCore = Circle(host); markerCore.style.backgroundColor = MarkerInk;
            SetMarker(false, 0, 0);
            host.Add(backdrop);
        }

        /// <summary>Shows where the sights point, as fractions of the window (0,0 = lower left).</summary>
        public void SetMarker(bool visible, float u, float v)
        {
            float width = host.resolvedStyle.width, height = host.resolvedStyle.height;
            bool show = visible && width > 0 && height > 0;
            markerEdge.style.display = markerRing.style.display = markerCore.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            float diameter = Mathf.Max(22, MarkerReferencePixels * height / ReferenceHeight);
            float line = Mathf.Max(2, diameter * 0.09f), x = u * width, y = (1 - v) * height;
            Size(markerEdge, x, y, diameter + 2 * line, line * 2);
            Size(markerRing, x, y, diameter, line);
            Size(markerCore, x, y, Mathf.Max(4, diameter * 0.16f), 0);
        }

        private static void Border(VisualElement element, Color color)
        {
            element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
        }

        public void Refresh(LiveOverlayContent? content, string bannerText)
        {
            float height = host.resolvedStyle.height, width = host.resolvedStyle.width;
            float scale = Mathf.Max(0.6f, height / ReferenceHeight);
            banner.text = bannerText;
            banner.style.fontSize = 20 * scale;
            banner.style.display = bannerText.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            backdrop.style.display = content == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (content == null || !(width > 0) || !(height > 0)) return;
            // Keep text at the top and bottom edges, clear of every dot.
            Place(heading, content.Heading, 34 * scale, top: 0.02f * height, bottom: null);
            Place(instruction, content.Instruction, 26 * scale, top: 0.02f * height + 46 * scale, bottom: null);
            Place(status, content.Status, 22 * scale, top: null, bottom: 0.02f * height + 30 * scale);
            Place(keys, content.Keys, 18 * scale, top: null, bottom: 0.015f * height);
            ring.style.display = dot.style.display = content.ShowDot ? DisplayStyle.Flex : DisplayStyle.None;
            if (!content.ShowDot) return;
            float diameter = Mathf.Max(MinimumDotPixels, content.DotDiameterFraction * width);
            float x = content.DotU * width, y = (1 - content.DotV) * height;
            Size(dot, x, y, diameter, 0);
            Size(ring, x, y, diameter * RingScale, Mathf.Max(2, diameter * 0.12f));
        }

        private static void Size(VisualElement element, float x, float y, float diameter, float border)
        {
            element.style.width = diameter; element.style.height = diameter;
            element.style.left = x - diameter / 2; element.style.top = y - diameter / 2;
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = diameter / 2;
            element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = border;
        }

        private static void Place(Label label, string text, float fontSize, float? top, float? bottom)
        {
            label.text = text;
            label.style.fontSize = fontSize;
            label.style.display = text.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (top.HasValue) label.style.top = top.Value;
            if (bottom.HasValue) label.style.bottom = bottom.Value;
        }

        private static VisualElement Circle(VisualElement parent)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            parent.Add(element);
            return element;
        }

        private static Label Text(VisualElement parent, Color color, bool bold, TextAnchor anchor)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.left = 0; label.style.right = 0;
            label.style.paddingLeft = 48; label.style.paddingRight = 48;
            label.style.color = color;
            label.style.unityTextAlign = anchor;
            label.style.whiteSpace = WhiteSpace.Normal;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(label);
            return label;
        }
    }
}
