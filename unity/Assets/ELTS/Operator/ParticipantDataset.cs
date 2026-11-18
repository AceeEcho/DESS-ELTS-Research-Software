#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Elts.Operator
{
    /// <summary>Coordinates stations sharing a collection and permanently removes its
    /// owned data. Only the configured roots supplied by the station are accepted.</summary>
    internal sealed class ParticipantDataset : IDisposable
    {
        internal const string GenerationFile = ".dataset-generation";
        private const string RunId = "[A-Za-z0-9][A-Za-z0-9_-]{0,79}";
        private static readonly Regex Reservation = new Regex(@"^\.(" + RunId + @")\.reservation$");
        private static readonly Regex Run = new Regex("^" + RunId + "$");
        private static readonly Regex Database = new Regex(@"^collection\.sqlite(?:-wal|-shm|-journal)?$");
        private static readonly Regex Export = new Regex(@"^(?:(?:raw-|recovery-)?" + RunId + @"-[a-f0-9]{6,32}\.zip|elts-results-\d{8}-\d{6}-[a-f0-9]{6}\.xlsx|(?:participant-[a-f0-9]{32}|collection)-[a-f0-9]{32}\.(?:xlsx|sqlite))(?:\.pending)?$");
        // The original calibration editor saved arbitrary 1–64 character profile
        // names here. Those filenames can themselves contain participant identity.
        private static readonly Regex Profile = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 _-]{0,63}\.(?:json|tmp)$");
        private static readonly Regex Draft = new Regex(@"^\.draft-[0-9a-f]{32}\.(?:json|tmp)$");
        private readonly string root, lockPath, mutexName;
        private FileStream stationLease;

        public ParticipantDataset(string dataRoot)
        {
            root = SafeDirectory(dataRoot);
            Directory.CreateDirectory(root);
            lockPath = Path.Combine(root, ".participant-store.lock");
            CheckExisting(lockPath);
            using (var sha = SHA256.Create())
                mutexName = "ELTS-dataset-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-", "");
            stationLease = OpenStationLease();
        }

        private FileStream OpenStationLease() => new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        public void Dispose() => stationLease.Dispose();

        public void Remove(string profileDirectory, string confirmation)
        {
            if (confirmation != "CONFIRM") throw new ArgumentException("Type CONFIRM exactly to remove participant data.");
            SafeDirectory(root);
            CheckExisting(lockPath);
            string profiles = SafeDirectory(profileDirectory);
            // Serialize upgrades so two idle stations cannot both yield their shared
            // leases at once. A separate player with the collection open blocks removal.
            using (var mutex = new Mutex(false, mutexName))
            {
                bool entered;
                try { entered = mutex.WaitOne(0); } catch (AbandonedMutexException) { entered = true; }
                if (!entered) throw new IOException("Another station is removing participant data.");
                try
                {
                    stationLease.Dispose();
                    try
                    {
                        FileStream exclusive;
                        try { exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
                        catch (IOException error) { throw new IOException("Close other ELTS players using this data folder, then try again.", error); }
                        using (exclusive) RemoveFiles(profiles);
                    }
                    finally { stationLease = OpenStationLease(); }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private void RemoveFiles(string profiles)
        {
            Directory.CreateDirectory(profiles);
            string marker = Path.Combine(profiles, GenerationFile);
            CheckExisting(marker);
            // The calibration server holds this file open while saving a profile.
            // Its browser presents this generation on every save, so an older tab
            // cannot put the previous participant back after a reset.
            using (var generation = new FileStream(marker, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                var files = new List<string>();
                var directories = new List<string>();
                var databases = new List<string>();
                var reservations = new List<string>();
                var missingReservations = new List<string>();
                foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
                {
                    if (entry is DirectoryInfo directory)
                    {
                        if (directory.Name == "exports")
                        {
                            RejectLink(directory.FullName);
                            foreach (var item in directory.EnumerateFiles())
                                if (Export.IsMatch(item.Name)) files.Add(item.FullName);
                        }
                        else if (Run.IsMatch(directory.Name) &&
                            (File.Exists(Path.Combine(root, "." + directory.Name + ".reservation")) ||
                             File.Exists(Path.Combine(directory.FullName, "events.ndjson")) ||
                             File.Exists(Path.Combine(directory.FullName, "participant.json")) ||
                             File.Exists(Path.Combine(directory.FullName, "session-summary.json"))))
                        {
                            CollectRecording(directory, files, directories);
                            string reservation = Path.Combine(root, "." + directory.Name + ".reservation");
                            if (!File.Exists(reservation)) missingReservations.Add(reservation);
                        }
                    }
                    else if (Reservation.IsMatch(entry.Name)) reservations.Add(entry.FullName);
                    else if (Database.IsMatch(entry.Name)) databases.Add(entry.FullName);
                }
                foreach (var profile in new DirectoryInfo(profiles).EnumerateFiles())
                    if (Profile.IsMatch(profile.Name) || Draft.IsMatch(profile.Name)) files.Add(profile.FullName);
                // Keep the index until the originals are gone. An interrupted run
                // can be retried, including the case of a partially removed folder.
                foreach (string path in files.Concat(reservations).Concat(databases))
                {
                    RejectLink(path);
                    using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                }
                foreach (string reservation in missingReservations)
                {
                    // Imported recordings may predate reservations. Keep an empty
                    // ownership marker until the folder is gone so retry can recognize it.
                    using (new FileStream(reservation, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    reservations.Add(reservation);
                }
                byte[] epoch = Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N"));
                generation.SetLength(0);
                generation.Write(epoch, 0, epoch.Length);
                generation.Flush(true);
                foreach (string path in files)
                {
                    RejectLink(path);
                    File.Delete(path);
                }
                foreach (string directory in directories)
                {
                    RejectLink(directory);
                    Directory.Delete(directory, false);
                }
                foreach (string path in reservations.Concat(databases))
                {
                    RejectLink(path);
                    File.Delete(path);
                }
            }
        }

        private static void CollectRecording(DirectoryInfo directory, List<string> files, List<string> directories)
        {
            RejectLink(directory.FullName);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                RejectLink(entry.FullName);
                if (entry is DirectoryInfo nested) CollectRecording(nested, files, directories);
                else files.Add(entry.FullName);
            }
            directories.Add(directory.FullName);
        }

        private static string SafeDirectory(string path)
        {
            string full = Path.GetFullPath(path);
            if (new DirectoryInfo(full).Parent == null) throw new IOException("A drive root cannot be a participant storage directory.");
            for (DirectoryInfo? part = new DirectoryInfo(full); part != null; part = part.Parent) CheckExisting(part.FullName);
            return full;
        }

        private static void CheckExisting(string path)
        {
            // GetAttributes can also identify links whose target no longer exists.
            try { RejectLink(path); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Participant data removal cannot follow a linked path: " + path);
        }
    }
}
