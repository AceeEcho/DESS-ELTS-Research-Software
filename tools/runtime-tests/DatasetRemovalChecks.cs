using System;
using System.IO;
using System.Linq;
using Elts.Operator;

internal static class DatasetRemovalChecks
{
    internal static void Run(string temporary, Action<bool,string> check)
    {
        string root = Path.Combine(temporary, "wipe-data"), profiles = Path.Combine(temporary, "wipe-profiles");
        string run = Path.Combine(root, "synthetic-1"), exports = Path.Combine(root, "exports");
        Directory.CreateDirectory(run); Directory.CreateDirectory(exports); Directory.CreateDirectory(profiles);
        string marker = Path.Combine(root, ".synthetic-1.reservation");
        string room = Path.Combine(temporary, "station-room.json");
        string[] removed = {
            marker, Path.Combine(run, "events.ndjson"), Path.Combine(run, "participant.json"),
            Path.Combine(root, "collection.sqlite"), Path.Combine(root, "collection.sqlite-wal"),
            Path.Combine(exports, "recovery-test-aabbcc.zip"), Path.Combine(exports, "elts-results-20261118-091001-aabbcc.xlsx"),
            Path.Combine(exports, "collection-" + new string('a',32) + ".sqlite"),
            Path.Combine(profiles, "participant-0.json"), Path.Combine(profiles, "Old participant.tmp"),
            Path.Combine(profiles, ".draft-" + new string('b',32) + ".json")
        };
        string[] kept = {room, Path.Combine(root, "keep.txt"), Path.Combine(exports, "unrelated.xlsx"),
            Path.Combine(profiles, "README.md")};
        foreach (string path in removed.Concat(kept)) File.WriteAllText(path, "temporary fixture");
        using (var station = new ParticipantDataset(root))
        {
            foreach (var invalid in new[] {"", "confirm", "CONFIRM "})
            {
                bool rejected = false;
                try { station.Remove(profiles, invalid); } catch (ArgumentException) { rejected = true; }
                check(rejected && removed.All(File.Exists), "Exact confirmation is required before any deletion");
            }
            using (var other = new ParticipantDataset(root))
            {
                bool rejected = false;
                try { station.Remove(profiles, "CONFIRM"); } catch (IOException) { rejected = true; }
                check(rejected && removed.All(File.Exists), "An open station blocks permanent removal");
            }
            using (new FileStream(removed[1], FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool rejected = false;
                try { station.Remove(profiles, "CONFIRM"); } catch (IOException) { rejected = true; }
                check(rejected && removed.All(File.Exists), "A locked recording is detected before data removal");
            }
            string linked = Path.Combine(run, "outside");
            try { Directory.CreateSymbolicLink(linked, temporary); }
            catch (UnauthorizedAccessException) { /* Some Windows accounts cannot create test links. */ }
            catch (IOException) when (!Directory.Exists(linked)) { }
            if (Directory.Exists(linked))
            {
                try
                {
                    bool rejected = false;
                    try { station.Remove(profiles, "CONFIRM"); } catch (IOException) { rejected = true; }
                    check(rejected && removed.All(File.Exists) && kept.All(File.Exists), "A linked tree blocks removal before deleting any file");
                }
                finally { Directory.Delete(linked, false); }
            }
            station.Remove(profiles, "CONFIRM");
            check(!Directory.Exists(run) && removed.All(p => !File.Exists(p)), "Wipe removes original, derived, draft and reservation data");
            check(kept.All(File.Exists), "Station configuration and unrelated files survive");
            string generation = File.ReadAllText(Path.Combine(profiles, ParticipantDataset.GenerationFile));
            check(Guid.TryParseExact(generation, "N", out _), "Wipe invalidates existing calibration clients");
            station.Remove(profiles, "CONFIRM");
            check(kept.All(File.Exists), "An empty collection can be reset again");
        }
    }
}
