#nullable enable
using System;
using System.Diagnostics;
using UnityEngine.Profiling;

namespace Elts.Operator
{
    /// <summary>Opt-in development-player measurements. Millisecond histograms
    /// keep the cost and memory bounded regardless of recording duration.</summary>
    internal sealed class StationPerformance
    {
        private sealed class Frames
        {
            private readonly int[] milliseconds = new int[2001];
            private int count;
            private double maximum;

            public void Add(double duration)
            {
                milliseconds[Math.Min(milliseconds.Length - 1, (int)Math.Ceiling(duration))]++;
                count++;
                maximum = Math.Max(maximum, duration);
            }

            private int? Percentile(double fraction)
            {
                if (count == 0) return null;
                int wanted = (int)Math.Ceiling(fraction * count);
                for (int i = 0, seen = 0; i < milliseconds.Length; i++)
                    if ((seen += milliseconds[i]) >= wanted) return i;
                return null;
            }

            public object Summary() => new {count, medianMs=Percentile(.5), p95Ms=Percentile(.95), maximumMs=maximum};
        }

        private readonly Frames preview = new Frames(), withoutPreview = new Frames();
        private long previous = Stopwatch.GetTimestamp(), lastSummary, peakAllocatedBytes;
        private object? summary;

        public void Frame(bool viewing)
        {
            long now = Stopwatch.GetTimestamp();
            (viewing ? preview : withoutPreview).Add(1000.0 * (now - previous) / Stopwatch.Frequency);
            previous = now;
        }

        public object? Snapshot()
        {
            // Refresh diagnostics once a second even when the dashboard polls faster.
            if (summary == null || previous - lastSummary >= Stopwatch.Frequency)
            {
                lastSummary = previous;
                peakAllocatedBytes = Math.Max(peakAllocatedBytes, Profiler.GetTotalAllocatedMemoryLong());
                summary = new {preview=preview.Summary(), withoutPreview=withoutPreview.Summary(), peakUnityAllocatedBytes=peakAllocatedBytes};
            }
            return summary;
        }
    }
}
