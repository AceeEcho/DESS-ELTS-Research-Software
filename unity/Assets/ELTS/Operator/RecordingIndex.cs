#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// <summary>Discovers changed recording inputs, derives their reviews, and checks
    /// published file hashes. ParticipantCollection owns SQL storage and migrations.</summary>
    internal static class RecordingIndex
    {
        /// <summary>Metadata is an indexing shortcut. Integrity verification reads the actual bytes.</summary>
        private static string SourceStamp(string directory, out DateTime modifiedUtc)
        {
            var stamp = new StringBuilder(TaskDataReview.Version).Append('|').Append(TaskDataReview.AnalysisScreenVersion);
            ParticipantCollection.CheckFile(directory);
            modifiedUtc=DateTime.MinValue;
            // Directory enumeration supplies file attributes, size and time in a
            // single pass. Include supplementary products of every file type.
            foreach (var file in new DirectoryInfo(directory).EnumerateFiles().OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (file.Name == ".session-summary.pending.json" || file.Name.EndsWith(".pending", StringComparison.Ordinal)) continue;
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Data import cannot follow linked files.");
                stamp.Append('|').Append(file.Name).Append('@').Append(file.Length).Append('@').Append(file.LastWriteTimeUtc.Ticks);
                if(file.LastWriteTimeUtc>modifiedUtc)modifiedUtc=file.LastWriteTimeUtc;
            }
            if(modifiedUtc==DateTime.MinValue)modifiedUtc=Directory.GetLastWriteTimeUtc(directory);
            return stamp.ToString();
        }

        public static (int Indexed, int Unchanged, string Errors) Refresh(ParticipantCollection collection,bool rebuild = false)
        {
            collection.Initialize();
            var errors = new List<string>();
            var (cached,excluded) = collection.ReadIndexSources();
            int indexed = 0, unchanged = 0;
            foreach (string directory in Directory.EnumerateDirectories(collection.Root))
            {
                if (excluded.Contains(directory) || !File.Exists(Path.Combine(directory, "events.ndjson"))) continue;
                try
                {
                    string stamp = SourceStamp(directory, out var modifiedUtc);
                    if (!rebuild && cached.TryGetValue(directory, out var previous) && previous == stamp)
                    { unchanged++; continue; }
                    IndexRecording(collection,directory, stamp,modifiedUtc);
                    indexed++;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is ArgumentException || e is FormatException || e is InvalidCastException || e is OverflowException)
                { errors.Add(Path.GetFileName(directory) + ": " + e.Message); }
            }
            return (indexed, unchanged, String.Join("\n", errors));
        }

        public static void IndexRecording(ParticipantCollection collection,string directory)
        {
            collection.Initialize();
            if (collection.IsRemoved(directory)) return;
            string stamp=SourceStamp(directory,out var modifiedUtc);
            IndexRecording(collection,directory,stamp,modifiedUtc);
        }

        private static void IndexRecording(ParticipantCollection collection,string directory, string stamp,DateTime modifiedUtc)
        {
            // Stamp before the read. If a writer appends while indexing, the next
            // refresh will see a change and read the new complete lines.
            string code = Path.GetFileName(directory), visit = "";
            string identityPath = Path.Combine(directory, "participant.json");
            if (File.Exists(identityPath))
            {
                var identity = JObject.Parse(File.ReadAllText(identityPath));
                code = (string?)identity["participantId"] ?? code;
                visit = (string?)identity["visitId"] ?? "";
            }
            collection.Import(directory, code, visit, TaskDataReview.Read(directory).ToString(Formatting.None), stamp, modifiedUtc);
        }
        /// <summary>Read every file named by the writer's closure manifest without modifying it.</summary>
        public static string VerifyRecording(ParticipantCollection collection,string id)
        {
            string directory=collection.SessionDirectory(id);
            string at=DateTime.UtcNow.ToString("O",CultureInfo.InvariantCulture);
            string stamp="";
            try
            {
                stamp=SourceStamp(directory,out _);
                int files=VerifyManifest(directory);
                if(SourceStamp(directory,out _)!=stamp)throw new IOException("Recording files changed during verification. Retry when the folder is stable.");
                collection.SetVerification(id,"Passed",at,stamp);
                return $"Verified {files} file checksums.";
            }
            catch(Exception error) when(error is IOException || error is UnauthorizedAccessException || error is JsonException || error is FormatException)
            {
                collection.SetVerification(id,"Failed",at,stamp);
                throw;
            }
        }
        private static int VerifyManifest(string directory)
        {
            ParticipantCollection.CheckFile(directory);
            string summaryPath=Path.Combine(directory,"session-summary.json");
            ParticipantCollection.CheckFile(summaryPath);
            var summary=JObject.Parse(File.ReadAllText(summaryPath));
            if(summary["schemaVersion"]?.Type!=JTokenType.String || (string?)summary["schemaVersion"]!="elts.session-summary.v2" || summary["complete"]?.Type!=JTokenType.Boolean ||
               (bool?)summary["complete"]!=true || (summary["error"]!=null && summary["error"]!.Type!=JTokenType.Null))
                throw new IOException("The recording has no successful closure summary.");
            var primary=summary["checksumsSha256"] as JObject;
            if(primary==null || primary.Count!=ParticipantCollection.Streams.Length || ParticipantCollection.Streams.Any(name=>primary[name]==null))
                throw new IOException("The summary must identify all three raw stream hashes.");
            var extra=summary["supplementaryChecksumsSha256"] as JObject;
            if(extra==null)throw new IOException("The summary has no supplementary manifest.");
            foreach(var entry in primary.Properties().Concat(extra.Properties()))
            {
                if(!System.Text.RegularExpressions.Regex.IsMatch(entry.Name,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z"))
                    throw new IOException("Invalid file name in closure manifest.");
                if(entry.Value.Type!=JTokenType.String)throw new IOException("Missing checksum for "+entry.Name);
                string path=Path.Combine(directory,entry.Name);ParticipantCollection.CheckFile(path);
                using(var sha=SHA256.Create())
                using(var input=File.OpenRead(path))
                {
                    string actual=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
                    if(!String.Equals(actual,(string?)entry.Value,StringComparison.Ordinal))throw new IOException("Checksum mismatch: "+entry.Name);
                }
            }
            return primary.Count+extra.Count;
        }
    }
}
