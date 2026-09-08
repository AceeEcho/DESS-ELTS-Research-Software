#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using Elts.Clock;
using Elts.Logging;
using Elts.Session;

static class SessionRecordingAdapterChecks
{
    static int passed;
    static readonly string Hash = new string('a', 64);

    static void True(bool value, string name)
    {
        if (!value) throw new Exception("FAIL: " + name);
        passed++;
    }

    static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "elts-session-adapter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var provenance = new SessionProvenance("test", "source", Hash, Hash, Hash, true, DateTimeOffset.UtcNow);
            using var adapter = new SessionRecordingAdapter(root, provenance, new LoggingConfiguration(closeTimeout: TimeSpan.FromSeconds(5)));

            True(adapter.ReserveAndStartAsync("dev-session").GetAwaiter().GetResult(), "unique run reservation and writer start succeed");
            string firstDirectory = adapter.RunDirectory!;
            True(adapter.IsOpen && adapter.WriterHealthy, "open adapter exposes healthy writer state");
            True(adapter.TryRecord(new SessionEvent(1, new MonotonicTimestamp(10), "SessionMetadata")), "event sink forwards critical event to writer");
            True(adapter.CloseAsync().GetAwaiter().GetResult(), "healthy writer closes and publishes output");
            True(adapter.CloseAsync().GetAwaiter().GetResult(), "repeated healthy close preserves its successful result");
            True(!adapter.IsOpen && File.Exists(Path.Combine(firstDirectory, "session-summary.json")), "close publishes the immutable run output");

            True(adapter.ReserveAndStartAsync("dev-session").GetAwaiter().GetResult(), "closed adapter can reserve a later rerun");
            string secondDirectory = adapter.RunDirectory!;
            True(secondDirectory != firstDirectory, "later rerun receives a distinct reserved directory");
            True(adapter.CloseAsync().GetAwaiter().GetResult(), "later run closes cleanly");

            using var faulted = new SessionRecordingAdapter(root, provenance, new LoggingConfiguration(closeTimeout: TimeSpan.FromSeconds(5)), new FaultingSinkFactory());
            True(faulted.ReserveAndStartAsync("faulted-session").GetAwaiter().GetResult(), "fault-injection writer starts");
            faulted.TryRecord(new SessionEvent(2, new MonotonicTimestamp(20), "OperatorNote"));
            for (int i = 0; i < 100 && faulted.WriterHealthy; i++) Thread.Sleep(10);
            True(faulted.IsOpen && !faulted.WriterHealthy, "owned writer remains closable after its writer thread fails");
            True(!faulted.CloseAsync().GetAwaiter().GetResult(), "unhealthy writer close reports failure");
            True(!faulted.CloseAsync().GetAwaiter().GetResult(), "repeated failed close preserves its failed result");
            Console.WriteLine("PASS: " + passed + " session recording adapter checks");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class FaultingSinkFactory : ILogSinkFactory
    {
        public ILogSink Open(LoggingRunLease lease) => new FaultingSink();
    }

    private sealed class FaultingSink : ILogSink
    {
        public IReadOnlyDictionary<string, string> FileChecksums => new Dictionary<string, string>();
        public void Write(string fileName, string utf8Json) => throw new IOException("injected writer failure");
        public void Flush(bool durable) { }
        public void Dispose() { }
    }
}
