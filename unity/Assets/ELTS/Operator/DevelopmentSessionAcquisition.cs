#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Elts.Logging;
using Elts.Tracking;

namespace Elts.Operator
{
    /// <summary>
    /// Development-only acquisition adapter. It owns a synthetic source on one
    /// background thread and publishes only immutable paired observations.
    /// The callback must be a bounded, non-blocking enqueue (false means the
    /// bounded sink rejected the pair); this class never builds an unbounded
    /// queue or performs file I/O.
    /// </summary>
    public sealed class DevelopmentSessionAcquisition
    {
        private const int StopJoinMilliseconds = 2000;
        private readonly Func<TrackingSamplePair, bool> logger;
        private readonly object gate = new object();
        // Lifecycle cancellation must stay reachable when capture/logging stalls.
        private readonly object lifecycleGate = new object();
        private readonly ITrackingSource source;
        private readonly double cadenceSeconds;
        private Thread? worker;
        private CancellationTokenSource? cancellation;
        private TrackingSamplePair? latest;
        private string? failure;
        private long acquiredCount;
        private long droppedCount;
        private long startedTimestamp;
        private long lastTimestamp;

        public DevelopmentSessionAcquisition(SyntheticTrackingSettings settings, Func<TrackingSamplePair, bool> logger)
            : this(new SyntheticTrackingSource(settings ?? throw new ArgumentNullException(nameof(settings))), settings.SampleRateHz, logger)
        { }

        /// <summary>One acquisition/logging path for interchangeable paired pose sources.</summary>
        public DevelopmentSessionAcquisition(ITrackingSource source, double sampleRateHz, Func<TrackingSamplePair, bool> logger)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            if (double.IsNaN(sampleRateHz) || double.IsInfinity(sampleRateHz) || sampleRateHz <= 0)
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            cadenceSeconds = 1.0 / sampleRateHz;
        }

        public bool IsRunning => worker is { IsAlive: true };
        public string? Failure { get { lock (gate) return failure; } }
        public TrackingSamplePair? Snapshot { get { lock (gate) return latest; } }
        public long AcquiredCount => Interlocked.Read(ref acquiredCount);
        public long DroppedCount => Interlocked.Read(ref droppedCount);
        public long SkippedCount => (source as SyntheticTrackingSource)?.SkippedAcquisitionCount ?? 0;

        /// <summary>
        /// Capture and enqueue the exact pair used by a desktop trigger. The same
        /// lock covers background polls and enqueue order, so a newer trigger pair
        /// cannot overtake an older background sample. A rejected raw sample must
        /// not produce a scored desktop shot.
        /// </summary>
        public bool TryCaptureForTrigger(out TrackingSamplePair pair)
        {
            lock (gate)
            {
                pair = default;
                if (!IsRunning || cancellation == null || cancellation.IsCancellationRequested || failure != null) return false;
                return CaptureAndLog(out pair);
            }
        }

        private bool CaptureAndLog(out TrackingSamplePair pair)
        {
            pair = default;
            if (!source.TryGetNext(out TrackingSample head)) return false;
            if (!source.TryGetNext(out TrackingSample weapon))
                throw new InvalidOperationException("A paired source must emit the weapon observation immediately after its head observation.");
            pair = new TrackingSamplePair(head, weapon);
            latest = pair;
            Interlocked.Increment(ref acquiredCount);
            bool admitted = logger(pair);
            if (!admitted) Interlocked.Increment(ref droppedCount);
            Interlocked.Exchange(ref lastTimestamp, Stopwatch.GetTimestamp());
            return admitted;
        }
        public double ActualElapsedRateHz
        {
            get
            {
                long start = Interlocked.Read(ref startedTimestamp);
                long end = Interlocked.Read(ref lastTimestamp);
                long count = AcquiredCount;
                if (start <= 0 || end <= start || count <= 0) return 0;
                return count / ((end - start) / (double)Stopwatch.Frequency);
            }
        }

        public void Start()
        {
            lock (lifecycleGate)
            {
                if (IsRunning) throw new InvalidOperationException("Synthetic acquisition is already running.");
                if (worker != null) throw new InvalidOperationException("Synthetic acquisition cannot be restarted.");
                cancellation = new CancellationTokenSource();
                worker = new Thread(() => Produce(cancellation.Token)) { IsBackground = true, Name = "ELTS synthetic acquisition" };
                worker.Start();
            }
        }

        public async Task StopAsync()
        {
            Thread? thread;
            CancellationTokenSource? stopSignal;
            lock (lifecycleGate)
            {
                // Snapshot lifecycle handles quickly. Cancellation must not wait
                // behind a deliberately blocked logger callback holding gate.
                stopSignal = cancellation;
                thread = worker;
            }
            stopSignal?.Cancel();
            if (thread == null) return;
            await Task.Run(() => thread.Join(StopJoinMilliseconds)).ConfigureAwait(false);
            if (thread.IsAlive) throw new TimeoutException("Synthetic acquisition did not stop within the bounded join interval.");
            else source.Dispose();
        }

        private void Produce(CancellationToken token)
        {
            long start = Stopwatch.GetTimestamp();
            Interlocked.Exchange(ref startedTimestamp, start);
            long next = start;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    lock (gate) CaptureAndLog(out _);
                    next = WaitUntil(next, token);
                }
            }
            catch (Exception exception)
            {
                SetFailure(exception.GetBaseException().Message);
            }
        }

        private long WaitUntil(long previous, CancellationToken token)
        {
            long interval = Math.Max(1L, (long)Math.Round(Stopwatch.Frequency * cadenceSeconds));
            long target = previous > long.MaxValue - interval ? long.MaxValue : previous + interval;
            while (!token.IsCancellationRequested)
            {
                long remaining = target - Stopwatch.GetTimestamp();
                if (remaining <= 0) return Stopwatch.GetTimestamp();
                double milliseconds = remaining * 1000.0 / Stopwatch.Frequency;
                // Windows timer waits can oversleep a 4 ms period. Sleep only
                // while comfortably ahead, then bounded-spin for the tail.
                if (milliseconds > 5) token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(Math.Min(milliseconds - 2, 20)));
                else Thread.SpinWait(64);
            }
            return target;
        }

        private void SetFailure(string message)
        {
            lock (gate) if (failure == null) failure = message;
        }
    }
}
