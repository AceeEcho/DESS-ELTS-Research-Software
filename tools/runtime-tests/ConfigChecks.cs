using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Elts.Config;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class ConfigChecks
{
    private static int passed;
    private static void Assert(bool value, string message)
    {
        if (!value) throw new Exception("FAIL: " + message);
        passed++;
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        catch (JsonException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, message);
    }
    private static string Hash(byte[] bytes)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source)) Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }
    private static void MutateEffective(string directory, Action<JObject> mutate)
    {
        string file = Path.Combine(directory, "effective-config.json");
        JObject value = JObject.Parse(File.ReadAllText(file));
        mutate(value);
        byte[] bytes = new UTF8Encoding(false).GetBytes(value.ToString(Formatting.None) + "\n");
        File.WriteAllBytes(file, bytes);
        string manifestFile = Path.Combine(directory, "manifest.json");
        JObject manifest = JObject.Parse(File.ReadAllText(manifestFile));
        manifest["effectiveConfigSha256"] = Hash(bytes);
        File.WriteAllText(manifestFile, manifest.ToString(Formatting.None), new UTF8Encoding(false));
    }
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the staged config directory.");
        string source = Path.GetFullPath(args[0]);
        DevelopmentConfiguration config = DevelopmentConfiguration.LoadDirectory(source);
        Assert(config.Mode == "synthetic" && !config.StudyReady, "synthetic mode is explicit");
        Assert(config.Rig.RigId == "synthetic-rig-001", "rig identity loaded without a fallback");
        Assert(Math.Abs(config.Rig.Display.Width - 1.2) < 1e-12, "known display geometry");
        Assert(config.Runtime.SampleRateHz == 250, "configured sample frequency");
        Assert(config.Session.BlockDurationSeconds == 300 && config.Session.ConditionOrder.Count == 4, "fixed-duration development session");
        Assert(config.SourceRawSha256.Count == 6, "raw source provenance retained");
        Reject(config.RequireStudyReadiness, "synthetic configuration cannot enable study mode");

        string temporary = Path.Combine(Path.GetTempPath(), "ELTS configuration checks " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string boundary = Path.Combine(temporary, "basis accepted"); Copy(source, boundary);
            MutateEffective(boundary, v => v["rig"]["display"]["upperLeftM"][0] = -0.6 + 3.375e-11);
            Assert(DevelopmentConfiguration.LoadDirectory(boundary).Mode == "synthetic", "basis below tolerance accepted");
            MutateEffective(boundary, v => v["rig"]["display"]["upperLeftM"][0] = -0.6 + 3.375e-10);
            Reject(() => DevelopmentConfiguration.LoadDirectory(boundary), "basis above tolerance rejected");
            Action<JObject>[] invalid = {
                v => v["unexpected"] = true,
                v => ((JObject)v["runtime"]).Remove("sampleRateHz"),
                v => v["runtime"]["sampleRateHz"] = 0,
                v => v["runtime"]["sampleRateHz"] = "250",
                v => v["runtime"]["loggedPredictionSeconds"] = 0.02,
                v => v["mode"] = "study",
                v => v["studyReady"] = true,
                v => v["rig"]["coordinateFrame"] = "openvr_native",
                v => v["rig"]["display"]["upperLeftM"] = v["rig"]["display"]["lowerLeftM"].DeepClone(),
                v => v["rig"]["calibration"]["rigId"] = "other-rig",
                v => v["rig"]["trackerSerials"]["weapon"] = v["rig"]["trackerSerials"]["head"],
                v => v["rig"]["weaponZeroQuaternionXyzw"] = new JArray(0, 0, 0, 0),
                v => v["session"]["conditionOrder"] = new JArray("WE_MT", "WE_MT", "WE_FT", "NE_FT"),
                v => v["session"]["blockDurationSeconds"] = 1,
                v => v["machine"]["dataRoot"] = "../escape",
                v => v["machine"]["dataRoot"] = "C:/escape",
                v => v["machine"]["dataRoot"] = "data/NUL.txt",
                v => v["machine"]["eltsEndpoint"] = "tcp://hardware:1234",
                v => v["machine"]["participantDisplayIndex"] = v["machine"]["operatorDisplayIndex"]
            };
            for (int i = 0; i < invalid.Length; i++)
            {
                string directory = Path.Combine(temporary, "invalid " + i);
                Copy(source, directory);
                MutateEffective(directory, invalid[i]);
                Reject(() => DevelopmentConfiguration.LoadDirectory(directory), "invalid configuration " + i + " rejected after valid rehash");
            }
            string corrupt = Path.Combine(temporary, "corrupt"); Copy(source, corrupt);
            File.AppendAllText(Path.Combine(corrupt, "effective-config.json"), " ");
            Reject(() => DevelopmentConfiguration.LoadDirectory(corrupt), "effective byte corruption rejected");
            string missing = Path.Combine(temporary, "missing"); Copy(source, missing);
            File.Delete(Path.Combine(missing, "schemas/rig.schema.json"));
            Reject(() => DevelopmentConfiguration.LoadDirectory(missing), "missing schema rejected");
            string schema = Path.Combine(temporary, "schema"); Copy(source, schema);
            File.AppendAllText(Path.Combine(schema, "schemas/rig.schema.json"), " ");
            Reject(() => DevelopmentConfiguration.LoadDirectory(schema), "schema byte corruption rejected");
            string duplicate = Path.Combine(temporary, "duplicate"); Copy(source, duplicate);
            string manifestFile = Path.Combine(duplicate, "manifest.json");
            string manifest = File.ReadAllText(manifestFile);
            File.WriteAllText(manifestFile, "{\"mode\":\"synthetic\"," + manifest.Substring(1));
            Reject(() => DevelopmentConfiguration.LoadDirectory(duplicate), "duplicate manifest key rejected");
        }
        finally
        {
            // The unique temporary root was created above; it never names source or user data.
            Directory.Delete(temporary, true);
        }
        Console.WriteLine("PASS: " + passed + " configuration checks");
    }
}
