#nullable enable
using System;
using Elts.Geometry;

namespace Elts.Rendering
{
    internal static class RenderNumbers
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static void RequireFinite(Vector3d value, string name)
        {
            if (!Finite(value.X) || !Finite(value.Y) || !Finite(value.Z)) throw new ArgumentException("Finite vector required.", name);
        }
    }
    /// <summary>Off-axis frustum in metres. Screen U is right, V is up, Normal points into the virtual room.</summary>
    public readonly struct OffAxisProjection
    {
        public readonly Vector3d Eye, Right, Up, Forward;
        public readonly double Left, RightExtent, Bottom, Top, Near, Far;
        public OffAxisProjection(ScreenPlane screen, Vector3d eye, double nearMarginMeters, double farMeters)
        {
            RenderNumbers.RequireFinite(eye, nameof(eye));
            if (!(screen.Width > 0) || !(screen.Height > 0)) throw new ArgumentException("A constructed screen plane is required.");
            double distance = Vector3d.Dot(screen.Origin - eye, screen.Normal);
            if (!RenderNumbers.Finite(nearMarginMeters) || nearMarginMeters < 0 || distance <= nearMarginMeters + 0.001)
                throw new ArgumentException("Eye must be in front of the screen, beyond the configured near margin.");
            Near = distance - nearMarginMeters;
            if (!RenderNumbers.Finite(farMeters) || farMeters <= distance) throw new ArgumentException("Far plane must be behind the screen.");
            Far = farMeters; Eye = eye; Right = screen.U; Up = screen.V; Forward = screen.Normal;
            Vector3d offset = screen.Origin - eye;
            Left = Vector3d.Dot(offset, Right) * Near / distance;
            Bottom = Vector3d.Dot(offset, Up) * Near / distance;
            RightExtent = Left + screen.Width * Near / distance;
            Top = Bottom + screen.Height * Near / distance;
        }
        /// <summary>Mathematical projection independent of Unity's GPU API conventions; useful for corner invariants.</summary>
        public (double X, double Y) ProjectNdc(Vector3d world)
        {
            var relative = world - Eye;
            double depth = Vector3d.Dot(relative, Forward);
            if (!(depth > 0)) throw new ArgumentException("Point is behind the eye.");
            return (2 * (Near * Vector3d.Dot(relative, Right) / depth - Left) / (RightExtent - Left) - 1,
                    2 * (Near * Vector3d.Dot(relative, Up) / depth - Bottom) / (Top - Bottom) - 1);
        }
    }

    /// <summary>Render-only positional extrapolation. Inputs are immutable and can remain raw logging inputs.</summary>
    public static class RenderHeadPrediction
    {
        public const double MaximumPredictionSeconds = 0.05;
        public static RigidPose? Predict(RigidPose? raw, Vector3d velocityMetersPerSecond, double predictionSeconds)
        {
            RenderNumbers.RequireFinite(velocityMetersPerSecond, nameof(velocityMetersPerSecond));
            if (!RenderNumbers.Finite(predictionSeconds) || predictionSeconds < 0 || predictionSeconds > MaximumPredictionSeconds)
                throw new ArgumentOutOfRangeException(nameof(predictionSeconds));
            return raw.HasValue ? new RigidPose(raw.Value.Position + velocityMetersPerSecond * predictionSeconds, raw.Value.Orientation) : (RigidPose?)null;
        }
    }

    /// <summary>Play/pause/scrub uses absolute recorded time. Frames are held, never interpolated or repaired.</summary>
    public sealed class ReplayTransport
    {
        public double DurationSeconds { get; }
        public double PositionSeconds { get; private set; }
        public bool IsPlaying { get; private set; }
        public ReplayTransport(double durationSeconds)
        {
            if (!RenderNumbers.Finite(durationSeconds) || durationSeconds < 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            DurationSeconds = durationSeconds;
        }
        public void Play() { if (PositionSeconds >= DurationSeconds) PositionSeconds = 0; IsPlaying = DurationSeconds > 0; }
        public void Pause() { IsPlaying = false; }
        public void Scrub(double seconds)
        {
            if (!RenderNumbers.Finite(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            PositionSeconds = Math.Max(0, Math.Min(DurationSeconds, seconds));
        }
        public void Advance(double deltaSeconds)
        {
            if (!RenderNumbers.Finite(deltaSeconds) || deltaSeconds < 0) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (!IsPlaying) return;
            Scrub(PositionSeconds + deltaSeconds);
            if (PositionSeconds >= DurationSeconds) IsPlaying = false;
        }
    }
}
