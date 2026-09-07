#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Elts.Geometry;
using Elts.Tracking;

namespace Elts.Logging
{
/// <summary>Owns an exclusively reserved directory. The reservation is retained as recovery evidence.</summary>
public sealed class LoggingRunLease
{
    internal LoggingRunLease(string runId, string directoryPath, string reservationPath) { RunId = runId; DirectoryPath = directoryPath; ReservationPath = reservationPath; }
    public string RunId { get; }
    public string DirectoryPath { get; }
    public string ReservationPath { get; }
}

/// <summary>Portable path validation and best-effort exclusive run reservation. No reparse point is followed below dataRoot.</summary>
public static class LoggingRunDirectory
{
    private const int MaxUniqueSuffix = 9999;

    public static LoggingRunLease ReserveUnique(string dataRoot, string requestedRunId)
    {
        if (String.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A data root is required.", nameof(dataRoot));
        string root = Path.GetFullPath(dataRoot);
        RejectReparsePoints(root, true);
        Directory.CreateDirectory(root);
        RejectReparsePoints(root, false);
        string baseId = ValidateRunId(requestedRunId);

        for (int suffix = 0; suffix <= MaxUniqueSuffix; suffix++)
        {
            // The suffix is part of the schema-bound 80-character ID, so reserve six characters for it.
            string suffixedBase = baseId.Length > 74 ? baseId.Substring(0, 74) : baseId;
            string runId = suffix == 0 ? baseId : suffixedBase + "-r" + suffix.ToString("D4", CultureInfo.InvariantCulture);
            string directory = CombineChild(root, runId);
            // An existing directory is never adopted, even if it appears empty.
            if (Directory.Exists(directory) || File.Exists(directory)) continue;
            string reservation = CombineChild(root, "." + runId + ".reservation");
            try
            {
                using (new FileStream(reservation, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            }
            catch (IOException) { continue; }

            try
            {
                // The create-new reservation serializes cooperative writers. .NET has no portable
                // atomic mkdir API, so the existing-directory check is repeated after creation.
                Directory.CreateDirectory(directory);
                RejectReparsePoints(directory, false);
                return new LoggingRunLease(runId, directory, reservation);
            }
            catch
            {
                TryDeleteReservation(reservation);
                throw;
            }
        }
        throw new IOException("Could not reserve a unique logging run directory.");
    }

    public static string ValidateRunId(string runId)
    {
        if (String.IsNullOrWhiteSpace(runId) || runId.Length > 80) throw new ArgumentException("Run ID must contain 1 to 80 characters.", nameof(runId));
        if (runId == "." || runId == ".." || Path.IsPathRooted(runId)) throw new ArgumentException("Run ID must be a relative file-name token.", nameof(runId));
        for (int i = 0; i < runId.Length; i++)
        {
            char c = runId[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_')) throw new ArgumentException("Run ID may contain only ASCII letters, digits, hyphen, and underscore.", nameof(runId));
        }
        return runId;
    }

    internal static string CombineChild(string root, string child)
    {
        string path = Path.GetFullPath(Path.Combine(root, child));
        string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)) throw new IOException("Output path escapes the configured data root.");
        return path;
    }

    internal static void RejectReparsePoints(string path, bool allowMissingFinalDirectory)
    {
        string full = Path.GetFullPath(path);
        string? current = Path.GetPathRoot(full);
        if (String.IsNullOrEmpty(current)) throw new IOException("Output path has no root.");
        string remainder = full.Substring(current.Length);
        foreach (string piece in remainder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, piece);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                if (allowMissingFinalDirectory && String.Equals(current, full, StringComparison.OrdinalIgnoreCase)) return;
                continue;
            }
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Logging paths may not contain symbolic links or reparse points: " + current);
        }
    }

    private static void TryDeleteReservation(string path) { try { File.Delete(path); } catch { } }
}

/// <summary>Failure/stall injection seam. Implementations are called only from the dedicated writer thread.</summary>
public interface ILogSink : IDisposable
{
    void Write(string fileName, string utf8Json);
    void Flush(bool durable);
    IReadOnlyDictionary<string, string> FileChecksums { get; }
}
public interface ILogSinkFactory { ILogSink Open(LoggingRunLease lease); }
public sealed class FileLogSinkFactory : ILogSinkFactory { public ILogSink Open(LoggingRunLease lease) => new FileLogSink(lease); }

internal sealed class FileLogSink : ILogSink
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    private readonly Dictionary<string, StreamWriter> writers = new Dictionary<string, StreamWriter>(StringComparer.Ordinal);
    private readonly Dictionary<string, IncrementalHash> hashes = new Dictionary<string, IncrementalHash>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> completedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
    private bool disposed;

    public FileLogSink(LoggingRunLease lease)
    {
        Create(lease, "samples.ndjson"); Create(lease, "events.ndjson"); Create(lease, "targets.ndjson");
    }
    public IReadOnlyDictionary<string, string> FileChecksums => completedHashes;

    public void Write(string fileName, string utf8Json)
    {
        if (disposed) throw new ObjectDisposedException(nameof(FileLogSink));
        if (!writers.TryGetValue(fileName, out StreamWriter? writer)) throw new ArgumentException("Unknown log product.", nameof(fileName));
        byte[] bytes = Utf8.GetBytes(utf8Json + "\n");
        writer.Write(utf8Json); writer.Write('\n');
        hashes[fileName].AppendData(bytes);
    }
    public void Flush(bool durable)
    {
        foreach (StreamWriter writer in writers.Values) writer.Flush();
        if (durable) foreach (StreamWriter writer in writers.Values) ((FileStream)writer.BaseStream).Flush(true);
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var pair in writers) { pair.Value.Dispose(); completedHashes[pair.Key] = ToHex(hashes[pair.Key].GetHashAndReset()); hashes[pair.Key].Dispose(); }
        writers.Clear(); hashes.Clear();
    }
    private void Create(LoggingRunLease lease, string fileName)
    {
        string path = LoggingRunDirectory.CombineChild(lease.DirectoryPath, fileName);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream, Utf8) { NewLine = "\n" };
        writers.Add(fileName, writer); hashes.Add(fileName, IncrementalHash.CreateHash(HashAlgorithmName.SHA256));
    }
    private static string ToHex(byte[] value) { var builder = new StringBuilder(value.Length * 2); foreach (byte b in value) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture)); return builder.ToString(); }
}

/// <summary>
/// Writer-thread seam for terminal publication tests. A pending file is never
/// a completed run; only a successful Publish call exposes the final name.
/// </summary>
public interface ISessionSummaryPublisher
{
    void WritePending(LoggingRunLease lease, string content);
    void Publish(LoggingRunLease lease);
}

internal sealed class FileSessionSummaryPublisher : ISessionSummaryPublisher
{
    private const string PendingFileName = ".session-summary.pending.json";
    private const string FinalFileName = "session-summary.json";
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    public void WritePending(LoggingRunLease lease, string content)
    {
        string path = LoggingRunDirectory.CombineChild(lease.DirectoryPath, PendingFileName);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream, Utf8))
        {
            writer.NewLine = "\n";
            writer.Write(content);
            writer.Flush();
            // A durable-flush failure leaves only this ignored pending file.
            stream.Flush(true);
        }
    }

    public void Publish(LoggingRunLease lease)
    {
        string pending = LoggingRunDirectory.CombineChild(lease.DirectoryPath, PendingFileName);
        string final = LoggingRunDirectory.CombineChild(lease.DirectoryPath, FinalFileName);
        if (File.Exists(final)) throw new IOException("A final session summary already exists.");
        // Same-directory File.Move is the no-overwrite terminal publication step.
        File.Move(pending, final);
    }
}

/// <summary>Single-writer session recorder. Samples can drop under pressure; target and event failures make the session unhealthy.</summary>
public sealed class SessionLogWriter : IDisposable
{
    // A critical-event flood cannot starve streaming samples indefinitely.
    private const int MaximumConsecutiveCriticalItems = 32;
    private readonly LoggingRunLease lease;
    private readonly SessionProvenance provenance;
    private readonly LoggingConfiguration configuration;
    private readonly ILogSinkFactory sinkFactory;
    private readonly ISessionSummaryPublisher summaryPublisher;
    private readonly BlockingCollection<WriterItem> samples;
    private readonly BlockingCollection<WriterItem> critical;
    private readonly object faultLock = new object();
    // Serializes the irrevocable final move against a caller timing out Close().
    private readonly object terminalPublicationLock = new object();
    private Thread? thread;
    private Exception? fault;
    private int started;
    private int closeRequested;
    private int exited;
    private bool terminalPublicationCancelled;
    private bool terminalPublicationWon;
    private long droppedSamples;
    private long writtenSamples;
    private long writtenEvents;
    private long writtenTargets;

    public SessionLogWriter(LoggingRunLease lease, SessionProvenance provenance, LoggingConfiguration? configuration = null, ILogSinkFactory? sinkFactory = null, ISessionSummaryPublisher? summaryPublisher = null)
    {
        this.lease = lease ?? throw new ArgumentNullException(nameof(lease));
        this.provenance = provenance ?? throw new ArgumentNullException(nameof(provenance));
        this.configuration = configuration ?? new LoggingConfiguration();
        this.sinkFactory = sinkFactory ?? new FileLogSinkFactory();
        this.summaryPublisher = summaryPublisher ?? new FileSessionSummaryPublisher();
        samples = new BlockingCollection<WriterItem>(new ConcurrentQueue<WriterItem>(), this.configuration.SampleQueueCapacity);
        critical = new BlockingCollection<WriterItem>(new ConcurrentQueue<WriterItem>(), this.configuration.CriticalQueueCapacity);
    }

    public LoggingRunLease Lease => lease;
    public long DroppedSampleCount => Interlocked.Read(ref droppedSamples);
    public LoggingHealth Health { get { lock (faultLock) return new LoggingHealth(fault == null, fault?.Message); } }
    public bool IsWriterAlive => Volatile.Read(ref started) != 0 && Volatile.Read(ref exited) == 0;

    public void Start()
    {
        if (Interlocked.CompareExchange(ref started, 1, 0) != 0) throw new InvalidOperationException("Logging has already started.");
        thread = new Thread(WriterLoop) { IsBackground = true, Name = "ELTS log writer" };
        thread.Start();
    }
    public bool TryLogSample(TrackingSamplePair sample)
    {
        if (!sample.IsValid) throw new ArgumentException("A complete paired tracking observation is required.", nameof(sample));
        if (!Accepting()) return false;
        try
        {
            if (samples.TryAdd(WriterItem.FromSample(sample))) return true;
        }
        catch (InvalidOperationException) { }
        Interlocked.Increment(ref droppedSamples);
        return false;
    }
    public bool TryLogEvent(SessionEvent sessionEvent)
    {
        if (!Accepting()) return false;
        return TryAddCritical(WriterItem.FromEvent(sessionEvent));
    }
    public bool TryLogTarget(TargetSnapshot target)
    {
        if (!Accepting()) return false;
        return TryAddCritical(WriterItem.FromTarget(target));
    }
    public LogCloseResult Close(TimeSpan? timeout = null)
    {
        if (Interlocked.CompareExchange(ref started, 0, 0) == 0) throw new InvalidOperationException("Start logging before closing it.");
        if (Interlocked.Exchange(ref closeRequested, 1) == 0) { samples.CompleteAdding(); critical.CompleteAdding(); }
        TimeSpan wait = timeout ?? configuration.CloseTimeout;
        if (wait <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        bool completed = thread != null && thread.Join(wait);
        bool published;
        if (!completed)
        {
            // This lock covers only terminal arbitration and File.Move. It can exceed the
            // requested close wait only if a filesystem metadata move itself stalls; that
            // tradeoff prevents timeout cancellation from racing a complete publication.
            lock (terminalPublicationLock)
            {
                if (!terminalPublicationWon)
                {
                    terminalPublicationCancelled = true;
                    SetFaultLocked(new TimeoutException("Logging did not close within the configured timeout; output is incomplete."));
                }
                published = terminalPublicationWon;
            }
        }
        else
        {
            lock (terminalPublicationLock) published = terminalPublicationWon;
        }
        Exception? current; lock (faultLock) current = fault;
        return new LogCloseResult(completed, published && current == null, current?.Message);
    }
    public void Dispose() { if (Interlocked.CompareExchange(ref started, 0, 0) != 0 && Volatile.Read(ref closeRequested) == 0) Close(); }

    private bool Accepting() => Volatile.Read(ref started) != 0 && Volatile.Read(ref closeRequested) == 0 && Health.IsHealthy;
    private bool TryAddCritical(WriterItem item)
    {
        try
        {
            if (critical.TryAdd(item)) return true;
        }
        catch (InvalidOperationException) { }
        SetFault(new IOException("Critical logging queue overflow; session must fail closed."));
        return false;
    }
    private void WriterLoop()
    {
        ILogSink? sink = null;
        try
        {
            sink = sinkFactory.Open(lease);
            Stopwatch flushClock = Stopwatch.StartNew();
            int consecutiveCriticalItems = 0;
            while (!critical.IsCompleted || !samples.IsCompleted)
            {
                WriterItem item;
                bool found = false;
                // Both fast paths are nonblocking. Waiting on an empty critical queue here
                // would silently limit the 250 Hz sample stream to the scheduler timeout.
                if (consecutiveCriticalItems < MaximumConsecutiveCriticalItems && critical.TryTake(out item))
                {
                    consecutiveCriticalItems++;
                    found = true;
                }
                else if (samples.TryTake(out item))
                {
                    consecutiveCriticalItems = 0;
                    found = true;
                }
                else if (critical.TryTake(out item))
                {
                    consecutiveCriticalItems = 1;
                    found = true;
                }
                else
                {
                    // This is the only blocking wait and it observes both queues.
                    int source = BlockingCollection<WriterItem>.TryTakeFromAny(new[] { critical, samples }, out item, 10);
                    if (source >= 0)
                    {
                        consecutiveCriticalItems = source == 0 ? consecutiveCriticalItems + 1 : 0;
                        found = true;
                    }
                }
                if (!found) { FlushIfDue(sink, flushClock); continue; }
                WriteItem(sink, item);
                FlushIfDue(sink, flushClock);
            }
            sink.Flush(true);
        }
        catch (Exception error) { SetFault(error); }
        finally
        {
            if (sink != null)
            {
                try
                {
                    sink.Dispose();
                    if (sink is FileLogSink files) FinalizeSummary(files.FileChecksums);
                }
                catch (Exception error) { SetFault(error); }
            }
            Volatile.Write(ref exited, 1);
        }
    }
    private void FlushIfDue(ILogSink sink, Stopwatch stopwatch)
    {
        if (stopwatch.Elapsed < configuration.FlushInterval) return;
        sink.Flush(false); stopwatch.Restart();
    }
    private void WriteItem(ILogSink sink, WriterItem item)
    {
        switch (item.Kind)
        {
            case WriterItemKind.Sample: sink.Write("samples.ndjson", LogJson.Sample(item.Sample)); Interlocked.Increment(ref writtenSamples); break;
            case WriterItemKind.Event: sink.Write("events.ndjson", LogJson.Event(item.Event!)); Interlocked.Increment(ref writtenEvents); break;
            case WriterItemKind.Target: sink.Write("targets.ndjson", LogJson.Target(item.Target!)); Interlocked.Increment(ref writtenTargets); break;
            default: throw new InvalidOperationException("Unknown log item.");
        }
    }
    private void FinalizeSummary(IReadOnlyDictionary<string, string> checksums)
    {
        lock (terminalPublicationLock)
        {
            if (terminalPublicationCancelled || HasFaultLocked()) return;
        }
        // The candidate has no authority while it remains under the pending name.
        summaryPublisher.WritePending(lease, SerializeSummary(checksums));
        lock (terminalPublicationLock)
        {
            if (terminalPublicationCancelled || HasFaultLocked()) return;
            summaryPublisher.Publish(lease);
            terminalPublicationWon = true;
        }
    }
    private string SerializeSummary(IReadOnlyDictionary<string, string> checksums)
    {
        Exception? current; lock (faultLock) current = fault;
        return LogJson.Summary(lease, provenance, current == null, current?.Message, DroppedSampleCount, Interlocked.Read(ref writtenSamples),
            Interlocked.Read(ref writtenEvents), Interlocked.Read(ref writtenTargets), checksums);
    }
    private void SetFault(Exception error)
    {
        lock (terminalPublicationLock)
        {
            // No later producer-side race may revise a successfully published terminal record.
            if (!terminalPublicationWon) SetFaultLocked(error);
        }
    }
    private void SetFaultLocked(Exception error) { lock (faultLock) { if (fault == null) fault = error; } }
    private bool HasFaultLocked() { lock (faultLock) return fault != null; }

    private enum WriterItemKind { Sample, Event, Target }
    private readonly struct WriterItem
    {
        private WriterItem(WriterItemKind kind, TrackingSamplePair sample, SessionEvent? sessionEvent, TargetSnapshot? target) { Kind = kind; Sample = sample; Event = sessionEvent; Target = target; }
        public WriterItemKind Kind { get; }
        public TrackingSamplePair Sample { get; }
        public SessionEvent? Event { get; }
        public TargetSnapshot? Target { get; }
        public static WriterItem FromSample(TrackingSamplePair value) => new WriterItem(WriterItemKind.Sample, value, null, null);
        public static WriterItem FromEvent(SessionEvent value) => new WriterItem(WriterItemKind.Event, default, value ?? throw new ArgumentNullException(nameof(value)), null);
        public static WriterItem FromTarget(TargetSnapshot value) => new WriterItem(WriterItemKind.Target, default, null, value ?? throw new ArgumentNullException(nameof(value)));
    }
}
}