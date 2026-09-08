#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Elts.Logging;
using Elts.Tracking;

namespace Elts.Session
{
    /// <summary>
    /// Bridges the pure session engine to one immutable-provenance run directory.
    /// Reservation and close occur off the caller thread; event and target records
    /// use the bounded critical queue, while samples may be dropped. A closed adapter
    /// can reserve a new uniquely named run for a later rerun.
    /// </summary>
    public sealed class SessionRecordingAdapter : ISessionEventSink, ISessionRecordingLifecycle, IDisposable
    {
        private readonly string dataRoot;
        private readonly SessionProvenance provenance;
        private readonly LoggingConfiguration options;
        private readonly ILogSinkFactory? sinkFactory;
        private readonly SemaphoreSlim lifecycleGate = new SemaphoreSlim(1, 1);
        private readonly object sync = new object();
        private SessionLogWriter? writer;
        private string? runDirectory;
        private long droppedSampleCount;
        private bool disposed;
        private bool? lastCloseSucceeded;

        public SessionRecordingAdapter(string dataRoot, SessionProvenance provenance, LoggingConfiguration? options = null)
        {
            if (String.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A logging data root is required.", nameof(dataRoot));
            this.dataRoot = dataRoot;
            this.provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            this.options = options ?? new LoggingConfiguration();
            sinkFactory = null;
        }

        internal SessionRecordingAdapter(string dataRoot, SessionProvenance provenance, LoggingConfiguration? options, ILogSinkFactory sinkFactory)
        {
            if (String.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A logging data root is required.", nameof(dataRoot));
            this.dataRoot = dataRoot;
            this.provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
            this.options = options ?? new LoggingConfiguration();
            this.sinkFactory = sinkFactory ?? throw new ArgumentNullException(nameof(sinkFactory));
        }

        /// <summary>Directory of the current or most recently closed unique run.</summary>
        public string? RunDirectory { get { lock (sync) return runDirectory; } }
        public bool IsOpen { get { lock (sync) return writer != null; } }
        public bool WriterHealthy { get { lock (sync) return writer != null && writer.IsWriterAlive && writer.Health.IsHealthy; } }
        public long DroppedSampleCount { get { lock (sync) return writer?.DroppedSampleCount ?? droppedSampleCount; } }

        public async Task<bool> ReserveAndStartAsync(string runId)
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                lock (sync) { if (writer != null) return false; }

                try
                {
                    var lease = await Task.Run(() => LoggingRunDirectory.ReserveUnique(dataRoot, runId)).ConfigureAwait(false);
                    var next = new SessionLogWriter(lease, provenance, options, sinkFactory);
                    next.Start();
                    lock (sync)
                    {
                        writer = next;
                        runDirectory = lease.DirectoryPath;
                        droppedSampleCount = 0;
                        lastCloseSucceeded = null;
                    }
                    return next.Health.IsHealthy;
                }
                catch
                {
                    return false;
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public async Task<bool> CloseAsync()
        {
            await lifecycleGate.WaitAsync().ConfigureAwait(false);
            try
            {
                SessionLogWriter? closing;
                lock (sync)
                {
                    closing = writer;
                    writer = null;
                }
                if (closing == null) return lastCloseSucceeded ?? false;

                try
                {
                    var result = await Task.Run(() => closing.Close()).ConfigureAwait(false);
                    lock (sync) { droppedSampleCount = closing.DroppedSampleCount; }
                    bool succeeded = result.Completed && result.CompleteOutput && closing.Health.IsHealthy;
                    lock (sync) { lastCloseSucceeded = succeeded; }
                    return succeeded;
                }
                catch
                {
                    lock (sync) { droppedSampleCount = closing.DroppedSampleCount; lastCloseSucceeded = false; }
                    return false;
                }
                finally
                {
                    closing.Dispose();
                }
            }
            finally
            {
                lifecycleGate.Release();
            }
        }

        public bool TryRecord(SessionEvent item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            lock (sync)
            {
                return writer != null && writer.TryLogEvent(item) && writer.Health.IsHealthy;
            }
        }

        /// <summary>Records a paired tracking observation; false means it was not accepted or was dropped.</summary>
        public bool TryLogSample(TrackingSamplePair sample)
        {
            lock (sync)
            {
                bool accepted = writer != null && writer.TryLogSample(sample);
                if (writer != null) droppedSampleCount = writer.DroppedSampleCount;
                return accepted;
            }
        }

        /// <summary>Records a target lifecycle snapshot through the writer's critical queue.</summary>
        public bool TryLogTarget(TargetSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            lock (sync)
            {
                return writer != null && writer.TryLogTarget(snapshot) && writer.Health.IsHealthy;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseAsync().GetAwaiter().GetResult();

        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(SessionRecordingAdapter));
        }
    }
}
