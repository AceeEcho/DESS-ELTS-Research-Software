#nullable enable
using System;
using System.IO;
using System.Text;
using System.Threading;
using Elts.Logging;

internal static class LoggingPublicationChecks
{
    private static readonly string Hash = new string('c', 64);
    private static int passed;
    private static void True(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "elts-logging-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            NormalPublication(root);
            FlushFailureAfterPendingBytes(root);
            TimeoutCancelsBeforePublication(root);
            MoveFailureDoesNotPublish(root);
            PublicationWinsBeforeWriterExit(root);
            Console.WriteLine("PASS: " + passed + " logging publication checks");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    private static void NormalPublication(string root)
    {
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "normal");
        var writer = new SessionLogWriter(lease, Provenance());
        writer.Start();
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(2));
        True(close.Completed && close.CompleteOutput, "normal close joins and publishes");
        True(File.Exists(Final(lease)) && !File.Exists(Pending(lease)), "normal close exposes final summary only");
    }

    private static void FlushFailureAfterPendingBytes(string root)
    {
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "flush-failure");
        var publisher = new ControlledPublisher(PublicationMode.ThrowAfterPendingBytes);
        var writer = new SessionLogWriter(lease, Provenance(), sinkFactory: null, summaryPublisher: publisher);
        writer.Start();
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(2));
        True(close.Completed && !close.CompleteOutput && !writer.Health.IsHealthy, "reported summary flush failure makes output incomplete");
        True(File.Exists(Pending(lease)) && !File.Exists(Final(lease)), "flush failure never exposes complete final summary");
    }

    private static void TimeoutCancelsBeforePublication(string root)
    {
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "timeout-before-publish");
        var publisher = new ControlledPublisher(PublicationMode.BlockAfterPending);
        var writer = new SessionLogWriter(lease, Provenance(), sinkFactory: null, summaryPublisher: publisher);
        writer.Start();
        LogCloseResult? close = null;
        var closer = new Thread(() => close = writer.Close(TimeSpan.FromMilliseconds(20)));
        closer.Start();
        True(publisher.Entered.Wait(TimeSpan.FromSeconds(2)), "publisher reaches pending write seam");
        True(closer.Join(TimeSpan.FromSeconds(2)), "timed close returns while pending write is stalled");
        True(close != null && !close.Completed && !close.CompleteOutput && !writer.Health.IsHealthy, "close timeout cancels unpublished terminal record");
        publisher.Release.Set();
        writer.Close(TimeSpan.FromSeconds(2));
        True(!File.Exists(Final(lease)), "cancelled pending summary is never moved to final name");
    }

    private static void MoveFailureDoesNotPublish(string root)
    {
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "move-failure");
        var publisher = new ControlledPublisher(PublicationMode.ThrowOnMove);
        var writer = new SessionLogWriter(lease, Provenance(), sinkFactory: null, summaryPublisher: publisher);
        writer.Start();
        LogCloseResult close = writer.Close(TimeSpan.FromSeconds(2));
        True(close.Completed && !close.CompleteOutput && !writer.Health.IsHealthy, "terminal move failure makes output incomplete");
        True(File.Exists(Pending(lease)) && !File.Exists(Final(lease)), "move failure keeps final name absent");
    }

    private static void PublicationWinsBeforeWriterExit(string root)
    {
        LoggingRunLease lease = LoggingRunDirectory.ReserveUnique(root, "published-before-exit");
        var publisher = new ControlledPublisher(PublicationMode.BlockAfterMove);
        var writer = new SessionLogWriter(lease, Provenance(), sinkFactory: null, summaryPublisher: publisher);
        ThreadPool.QueueUserWorkItem(_ => { publisher.Moved.Wait(); Thread.Sleep(80); publisher.Release.Set(); });
        writer.Start();
        LogCloseResult close = writer.Close(TimeSpan.FromMilliseconds(20));
        True(!close.Completed && close.CompleteOutput && writer.Health.IsHealthy, "publication state remains distinct from a timed-out thread join");
        True(File.Exists(Final(lease)), "published output retains final summary after post-move exit delay");
    }

    private static SessionProvenance Provenance() => new SessionProvenance("publication-test", "publication-test", Hash, Hash, Hash, true, DateTimeOffset.UtcNow);
    private static string Pending(LoggingRunLease lease) => Path.Combine(lease.DirectoryPath, ".session-summary.pending.json");
    private static string Final(LoggingRunLease lease) => Path.Combine(lease.DirectoryPath, "session-summary.json");

    private enum PublicationMode { Normal, ThrowAfterPendingBytes, BlockAfterPending, ThrowOnMove, BlockAfterMove }
    private sealed class ControlledPublisher : ISessionSummaryPublisher
    {
        private readonly PublicationMode mode;
        public readonly ManualResetEventSlim Entered = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Moved = new ManualResetEventSlim(false);
        public readonly ManualResetEventSlim Release = new ManualResetEventSlim(false);
        public ControlledPublisher(PublicationMode mode) { this.mode = mode; }

        public void WritePending(LoggingRunLease lease, string content)
        {
            using (var stream = new FileStream(Pending(lease), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false, true)))
            {
                writer.Write(content); writer.Flush(); stream.Flush(true);
            }
            Entered.Set();
            if (mode == PublicationMode.ThrowAfterPendingBytes) throw new IOException("injected error after summary bytes");
            if (mode == PublicationMode.BlockAfterPending) Release.Wait();
        }

        public void Publish(LoggingRunLease lease)
        {
            if (mode == PublicationMode.ThrowOnMove) throw new IOException("injected terminal move failure");
            File.Move(Pending(lease), Final(lease));
            Moved.Set();
            if (mode == PublicationMode.BlockAfterMove) Release.Wait();
        }
    }
}
