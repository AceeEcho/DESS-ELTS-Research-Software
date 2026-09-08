#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Elts.Clock;
using Elts.Logging;
using Elts.Scenario;
using Elts.Session;

static class SessionInteropFixture
{
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2 || args[0] != "--output") throw new ArgumentException("Usage: SessionInteropFixture --output DIRECTORY");
            WriteFixture(args[1]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    static void WriteFixture(string output)
    {
        string root = Path.GetFullPath(output);
        Directory.CreateDirectory(root);
        string repo = FindRepositoryRoot();
        var clock = new ManualSharedClock(DateTimeOffset.UnixEpoch);
        string config = Path.Combine(repo, "config", "development", "scenario.json");
        string source = HashText(HashFile(Path.Combine(repo, "unity", "Assets", "ELTS", "Session", "SessionRuntime.cs"))
            + HashFile(Path.Combine(repo, "unity", "Assets", "ELTS", "Session", "SessionRecordingAdapter.cs"))
            + HashFile(Path.Combine(repo, "unity", "Assets", "ELTS", "Logging", "SessionLogWriter.cs"))
            + HashFile(Path.Combine(repo, "tools", "runtime-tests", "SessionInteropFixture.cs")));
        string fixture = HashText("session-interop-v1|manual-clock|four-300s-blocks|abort-rerun");
        var provenance = new SessionProvenance("session-interop-v1", GitRevision(repo), HashFile(config), source, fixture, true, clock.UtcStartupAnchor);

        string completed = RunCompleted(root, clock, provenance);
        string aborted = RunAbortedAndRerun(root, clock, provenance);
        var report = new { schema = "elts.session-interop-report.v1", recordedUtc = DateTimeOffset.UtcNow, clockAnchorUtc = clock.UtcStartupAnchor,
            sourceRevision = provenance.SourceRevision, configurationHash = provenance.ConfigurationHash, scenarioHash = provenance.ScenarioHash,
            fixtureHash = provenance.FixtureHash, completedRunDirectory = completed, abortedRerunRunDirectory = aborted,
            completedEvents = CountLines(Path.Combine(completed, "events.ndjson")), abortedRerunEvents = CountLines(Path.Combine(aborted, "events.ndjson")) };
        File.WriteAllText(Path.Combine(root, "fixture-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS: session interop fixture; completed=" + completed + "; aborted-rerun=" + aborted);
    }

    static string RunCompleted(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("session-four-block-300s").GetAwaiter().GetResult(), "reserve completed run");
        var engine = NewEngine(clock, adapter);
        Require(engine.StartSetup(Ready()).SetupAccepted, "setup completed run");
        engine.Advance(); engine.Advance(); engine.Tick();
        for (int i = 0; i < 4; i++)
        {
            Require(engine.State == SessionState.BlockReady, "block ready " + i);
            engine.StartBlock(); clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick();
            Require(engine.State == SessionState.BlockEnded, "normal 300-second completion " + i);
            engine.Advance(); engine.Advance();
        }
        Require(engine.State == SessionState.SessionComplete && adapter.CloseAsync().GetAwaiter().GetResult(), "completed run publishes");
        return adapter.RunDirectory!;
    }

    static string RunAbortedAndRerun(string root, ManualSharedClock clock, SessionProvenance provenance)
    {
        using var adapter = new SessionRecordingAdapter(root, provenance);
        Require(adapter.ReserveAndStartAsync("session-abort-rerun").GetAwaiter().GetResult(), "reserve abort rerun run");
        var engine = NewEngine(clock, adapter);
        Require(engine.StartSetup(Ready()).SetupAccepted, "setup abort rerun run");
        engine.Advance(); engine.Advance(); engine.Tick(); engine.StartBlock();
        string first = engine.CurrentBlockId!;
        clock.Advance(TimeSpan.FromSeconds(12)); engine.Abort("synthetic-fixture-abort");
        Require(engine.State == SessionState.Aborted && engine.CanRerun, "aborted block permits rerun");
        engine.RerunCurrentBlock(); string rerun = engine.CurrentBlockId!;
        Require(first != rerun, "rerun has a unique block identity");
        engine.StartBlock(); clock.Advance(TimeSpan.FromSeconds(300)); engine.Tick();
        Require(engine.State == SessionState.BlockEnded && adapter.CloseAsync().GetAwaiter().GetResult(), "rerun output publishes");
        return adapter.RunDirectory!;
    }

    static SessionEngine NewEngine(ManualSharedClock clock, SessionRecordingAdapter adapter)
    {
        return new SessionEngine(clock, new SessionEventSequence(), new SessionPlan("fixture-p1", new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }),
            new SessionTiming(0, 0, true), adapter, new FixtureLink());
    }

    static SessionReadinessInput Ready() => new SessionReadinessInput { TwoDisplays = true, SteamVrRunning = true, TrackersBoundAndValidForTwoSeconds = true,
        ConfigurationHashesComputed = true, DiskFreeBytes = 2L * 1024 * 1024 * 1024, LogWriterAlive = true, WeLinkReachable = true };
    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException("Fixture failed: " + message); }
    static int CountLines(string path) { return File.Exists(path) ? File.ReadAllLines(path).Length : 0; }
    static string HashFile(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant(); }
    static string HashText(string text) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text))).ToLowerInvariant(); }
    static string FindRepositoryRoot() { var dir = new DirectoryInfo(AppContext.BaseDirectory); while (dir != null) { if (Directory.Exists(Path.Combine(dir.FullName, ".git"))) return dir.FullName; dir = dir.Parent; } return Directory.GetCurrentDirectory(); }
    static string GitRevision(string root) { using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false }); if (process == null) throw new InvalidOperationException("Cannot read git revision."); string text = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0 || String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Cannot read git revision."); return text; }
    sealed class FixtureLink : ISessionLink { public bool TrySetEnabled(bool enabled) => true; }
}
