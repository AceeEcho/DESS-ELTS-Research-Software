using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Elts.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>Optional ConfigChecks mode: --profile-inventory PATH_TO_CHECKOUT.</summary>
internal static class ProfileInventoryBenchmark
{
    private const int Samples = 7;

    public static void Run(string checkout)
    {
        var defaults = JObject.Parse(File.ReadAllText(Path.Combine(checkout, "Calibration Environment", "defaults.json")));
        var rig = new RigConfiguration(JObject.Parse(File.ReadAllText(Path.Combine(checkout, "config/rig/templates/synthetic-rig.json"))));
        using var temporary = new TemporaryDirectory();
        foreach (int count in new[] { 1, 55, 300 })
        {
            string directory = Directory.CreateDirectory(Path.Combine(temporary.Path, count.ToString())).FullName;
            void Save(int id, int version)
            {
                var saved = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(version);
                var record = new JObject { ["format"] = ParticipantCalibration.Format, ["participantId"] = id,
                    ["savedUtc"] = saved.ToString("O"), ["calibration"] = defaults };
                string path = Path.Combine(directory, ParticipantCalibration.FileNameFor(id));
                // Exercise a normal same-length atomic replacement using a distinct timestamp.
                string pending = path + ".tmp";
                File.WriteAllText(pending, record.ToString(Formatting.Indented));
                File.SetLastWriteTimeUtc(pending, saved);
                File.Move(pending, path, overwrite: true);
            }
            for (int id = 0; id < count; id++) Save(id, 0);
            var cold = new double[Samples];
            var unchanged = new double[Samples];
            var changed = new double[Samples];
            for (int round = 0; round <= Samples; round++)
            {
                // The first round warms file-system caches and compiled methods. Each
                // cold sample still starts with a completely new application inventory.
                var inventory = new ParticipantCalibration.Inventory();
                int changedId = count / 2;
                var timer = Stopwatch.StartNew();
                var first = inventory.Scan(directory, rig);
                double coldMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                var same = inventory.Scan(directory, rig);
                double unchangedMs = timer.Elapsed.TotalMilliseconds;
                Save(changedId, round + 1);
                timer.Restart();
                var replaced = inventory.Scan(directory, rig);
                double changedMs = timer.Elapsed.TotalMilliseconds;
                if (first.Count != count || first.Any(p => !p.Available) || !first.SequenceEqual(same)
                    || replaced.Count != count || !replaced[changedId].Available
                    || replaced[changedId].Revision == same[changedId].Revision
                    || !same.Where((_, i) => i != changedId).SequenceEqual(replaced.Where((_, i) => i != changedId)))
                    throw new InvalidOperationException("The measured inventory did not reflect exactly one changed profile.");
                if (round > 0)
                {
                    cold[round - 1] = coldMs;
                    unchanged[round - 1] = unchangedMs;
                    changed[round - 1] = changedMs;
                }
            }
            Print(count, "cold", cold);
            Print(count, "unchanged", unchanged);
            Print(count, "one changed", changed);
        }
    }

    private static void Print(int count, string condition, double[] times)
    {
        string Ms(double time) => time.ToString("F3", CultureInfo.InvariantCulture);
        Console.WriteLine($"{count} profiles, {condition}: median {Ms(times.OrderBy(t => t).ElementAt(Samples / 2))} ms; samples [{String.Join(", ", times.Select(Ms))}]");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("elts-profile-inventory-").FullName;
        public void Dispose() => Directory.Delete(Path, true);
    }
}
