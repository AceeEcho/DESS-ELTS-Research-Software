#nullable enable
using System;
using System.IO;
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
            True(!adapter.IsOpen && File.Exists(Path.Combine(firstDirectory, "session-summary.json")), "close publishes the immutable run output");

            True(adapter.ReserveAndStartAsync("dev-session").GetAwaiter().GetResult(), "closed adapter can reserve a later rerun");
            string secondDirectory = adapter.RunDirectory!;
            True(secondDirectory != firstDirectory, "later rerun receives a distinct reserved directory");
            True(adapter.CloseAsync().GetAwaiter().GetResult(), "later run closes cleanly");
            Console.WriteLine("PASS: " + passed + " session recording adapter checks");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
