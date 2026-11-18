using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Elts.Config;
using Elts.Geometry;
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
        if (args.Length == 2 && args[0] == "--profile-inventory")
        {
            ProfileInventoryBenchmark.Run(Path.GetFullPath(args[1]));
            return;
        }
        if (args.Length != 1) throw new ArgumentException("Pass the staged config directory.");
        string source = Path.GetFullPath(args[0]);
        DevelopmentConfiguration config = DevelopmentConfiguration.LoadDirectory(source);
        string pathFixture = Path.Combine(Path.GetTempPath(), "elts-data-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            string clone = Path.Combine(pathFixture, "project with spaces");
            Directory.CreateDirectory(Path.Combine(clone, "config"));
            Directory.CreateDirectory(Path.Combine(clone, "unity", "ProjectSettings"));
            File.WriteAllText(Path.Combine(clone, "config", "toolchain.json"), "{}");
            File.WriteAllText(Path.Combine(clone, "unity", "ProjectSettings", "ProjectVersion.txt"), "fixture");
            // Cover both old machine settings and the current default, even
            // when the machine running setup uses only one of them. Normalize
            // expected separators just as the resolver does on Windows.
            foreach (string dataRoot in new[] { config.Machine.DataRoot, "data/synthetic", "collection data" }.Distinct())
            {
                var machine = new MachineConfiguration(new JObject {
                    ["participantDisplayIndex"] = 0, ["operatorDisplayIndex"] = 1,
                    ["dataRoot"] = dataRoot, ["eltsEndpoint"] = "mock://elts"
                });
                string expected = Path.GetFullPath(Path.Combine(clone, dataRoot));
                Assert(machine.ResolveDataRoot(Path.Combine(clone, "unity")) == expected, "Editor saves at project root: " + dataRoot);
                Assert(machine.ResolveDataRoot(Path.Combine(clone, "build", "first player")) == expected, "Built player saves at project root: " + dataRoot);
                Assert(machine.ResolveDataRoot(Path.Combine(clone, "build", "rebuilt player")) == expected, "Rebuild preserves collection location: " + dataRoot);
                string portable = Path.Combine(pathFixture, "portable player");
                Assert(machine.ResolveDataRoot(portable) == Path.GetFullPath(Path.Combine(portable, dataRoot)), "Portable player retains a writable installation-relative location: " + dataRoot);
            }
        }
        finally { if (Directory.Exists(pathFixture)) Directory.Delete(pathFixture, true); }
        Assert(config.Mode == "synthetic" && !config.StudyReady, "synthetic mode is explicit");
        Assert(config.Rig.RigId == "synthetic-rig-001", "rig identity loaded without a fallback");
        // Samsung display from config/rig/templates/synthetic-rig.json (metres).
        Assert(Math.Abs(config.Rig.Display.Width - 1.42875) < 1e-12, "known display width");
        Assert(Math.Abs(config.Rig.Display.Height - 0.803275) < 1e-12, "known display height");
        Assert(Math.Abs(config.Rig.ScreenBottomHeightAboveFloorM - 1.050925) < 1e-12, "Samsung screen-bottom height is configured");
        Assert(Math.Abs(config.Rig.VirtualFloorY - (config.Rig.Display.Origin.Y - 1.050925)) < 1e-12, "virtual floor derives from the display instead of a separate scene offset");
        Assert(config.Runtime.SampleRateHz == 250, "configured sample frequency");
        Assert(config.Session.BlockDurationSeconds == 300 && config.Session.ConditionOrder.Count == 4, "fixed-duration development session");
        Assert(config.SourceRawSha256.Count == 6, "raw source provenance retained");
        Reject(config.RequireStudyReadiness, "synthetic configuration cannot enable study mode");

        string temporary = Path.Combine(Path.GetTempPath(), "ELTS configuration checks " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string boundary = Path.Combine(temporary, "basis accepted"); Copy(source, boundary);
            // Offsets straddle the normalized 1e-10 perpendicularity tolerance for this
            // display height; they shift the configured corner rather than a fixed size.
            double left = config.Rig.Display.Origin.X;
            MutateEffective(boundary, v => v["rig"]["display"]["upperLeftM"][0] = left + 3.375e-11);
            Assert(DevelopmentConfiguration.LoadDirectory(boundary).Mode == "synthetic", "basis below tolerance accepted");
            MutateEffective(boundary, v => v["rig"]["display"]["upperLeftM"][0] = left + 3.375e-10);
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
                v => v["rig"]["screenBottomHeightAboveFloorM"] = 0,
                v => v["rig"]["screenBottomHeightAboveFloorM"] = 3.1,
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
        ParticipantCalibrationChecks(source);
        Console.WriteLine("PASS: " + passed + " configuration checks");
    }

    private static bool Near(Vector3d actual, double x, double y, double z) =>
        Math.Abs(actual.X - x) < 1e-9 && Math.Abs(actual.Y - y) < 1e-9 && Math.Abs(actual.Z - z) < 1e-9;

    /// <summary>Synthetic participant files only; nothing here is a measured calibration.</summary>
    private static void ParticipantCalibrationChecks(string staged)
    {
        string root = Path.GetFullPath(staged);
        while (!File.Exists(Path.Combine(root, "Calibration Environment", "defaults.json")))
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("Run from inside the repository.");
        JObject profile = JObject.Parse(File.ReadAllText(Path.Combine(root, "Calibration Environment", "defaults.json")));

        var corpus = JObject.Parse(File.ReadAllText(Path.Combine(root, "Calibration Environment", "tests", "participant-ids.json")));
        foreach (var item in corpus["inputs"]!)
        {
            string text = (string)item["text"]!;
            bool parsed = ParticipantCalibration.TryParseId(text, out int id);
            Assert(item["id"]!.Type == JTokenType.Null ? !parsed : parsed && id == (int)item["id"]!,
                "shared participant ID example: " + text);
        }
        foreach (var item in corpus["records"]!)
        {
            int fileId = (int)item["fileId"]!;
            var record = (JObject)item["fields"]!.DeepClone();
            record["format"] = ParticipantCalibration.Format;
            record["savedUtc"] = "";
            record["calibration"] = profile.DeepClone();
            byte[] json = Encoding.UTF8.GetBytes(record.ToString());
            if ((bool)item["valid"]!)
                Assert(ParticipantCalibration.Parse(json, fileId).ParticipantId == fileId, "shared participant record accepted");
            else
                Reject(() => ParticipantCalibration.Parse(json, fileId), "shared renamed participant record rejected");
        }

        // Same inputs and expected values as tools/config/test_stage.py.
        profile["screen"]!["width"] = 2; profile["screen"]!["height"] = 1; profile["screen"]!["x"] = 2;
        profile["screen"]!["z"] = -3; profile["screen"]!["bottom"] = 1; profile["screen"]!["rotation"] = new JArray(0, 90, 0);
        profile["head"]!["eye"] = new JArray(1, 2, 3);
        profile["head"]!["mount"] = JObject.FromObject(new { position = new[] { 1, 1, 1 }, rotation = new[] { 0, 0, 90 } });
        profile["weapon"]!["muzzle"] = new JArray(0, 1, -2); profile["weapon"]!["bore"] = new JArray(0, 90, 0);
        profile["weapon"]!["mount"] = JObject.FromObject(new { position = new[] { 0, 1, 0 }, rotation = new[] { 0, 90, 0 } });
        byte[] Saved(int id, string name = "Alias", string format = ParticipantCalibration.Format, int? recordedId = null) =>
            new UTF8Encoding(false).GetBytes(new JObject { ["format"] = format, ["participantId"] = recordedId ?? id, ["name"] = name,
                ["savedUtc"] = "2026-01-01T00:00:00+00:00", ["calibration"] = profile }.ToString());

        var calibration = ParticipantCalibration.Parse(Saved(12), 12);
        Assert(calibration.ParticipantId == 12, "legacy participant profile loads by ID");
        Assert(Near(calibration.HeadEyeOffsetM, 1, 0, -2), "eye offset matches Python conversion");
        Assert(Near(calibration.WeaponMuzzleOffsetM, 2, 0, 0), "muzzle offset matches Python conversion");
        Assert(Near(calibration.WeaponBoreLocalDirection, 0, 0, 1), "bore direction matches Python conversion");
        Assert(Near(calibration.LowerLeftM, 2, 1, 2) && Near(calibration.LowerRightM, 2, 1, 4) && Near(calibration.UpperLeftM, 2, 2, 2),
            "screen corners match Python conversion");
        Reject(() => ParticipantCalibration.Parse(Saved(12, recordedId: 13), 12), "file renamed to another participant rejected");
        Reject(() => ParticipantCalibration.Parse(Saved(12, format: "other"), 12), "unknown file format rejected");
        Assert(ParticipantCalibration.Parse(Saved(12, name: "legacy field"), 12).ParticipantId == 12, "legacy name is ignored");
        Reject(() => ParticipantCalibration.Parse(new byte[] { 0x7b }, 12), "truncated file rejected");

        // A different saved screen is reported, but the staged screen is kept.
        var config = DevelopmentConfiguration.LoadDirectory(staged);
        Assert(!calibration.ScreenMatches(config.Rig), "different saved screen detected");
        var withParticipant = config.WithParticipantCalibration(calibration);
        Assert(withParticipant.Rig.Display.Origin.Equals(config.Rig.Display.Origin) && withParticipant.EffectiveSha256 == config.EffectiveSha256,
            "staged screen and bundle identity are kept");
        Assert(Near(withParticipant.Rig.HeadEyeOffsetM, 1, 0, -2), "participant eye offset replaces staged offset");

        // A rig whose screen matches receives the participant's offsets and nothing else.
        JObject rigJson = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(staged, "effective-config.json")))["rig"]!;
        rigJson["display"]!["lowerLeftM"] = new JArray(2, 1, 2); rigJson["display"]!["lowerRightM"] = new JArray(2, 1, 4);
        rigJson["display"]!["upperLeftM"] = new JArray(2, 2, 2); rigJson["screenBottomHeightAboveFloorM"] = 1;
        var matching = new RigConfiguration(rigJson);
        Assert(calibration.ScreenMatches(matching), "matching saved screen detected");
        var applied = calibration.ApplyTo(matching);
        Assert(Near(applied.HeadEyeOffsetM, 1, 0, -2) && Near(applied.WeaponBoreLocalDirection, 0, 0, 1), "offsets applied to matching rig");
        Assert(applied.HeadSerial == matching.HeadSerial && applied.Display.Origin.Equals(matching.Display.Origin), "screen and trackers unchanged");
        Assert(applied.CalibrationId == "synthetic-participant-12-" + calibration.Sha256, "calibration ID names participant file hash");
        Assert(applied.WeaponZero.W == 1, "saved bore already includes zero correction");

        string directory = Path.Combine(Path.GetTempPath(), "elts-participant-calibrations-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "participant-12.json"), Saved(12));
            File.WriteAllBytes(Path.Combine(directory, "participant-3.json"), Saved(3, name: ""));
            File.WriteAllBytes(Path.Combine(directory, "participant-4.json"), Saved(5));
            File.WriteAllText(Path.Combine(directory, "Older named profile.json"), profile.ToString());
            var inventory = new ParticipantCalibration.Inventory();
            var listed = inventory.Scan(directory, matching);
            Assert(listed.Count == 3 && listed[0] == (3,true,true,Hash(Saved(3, name: ""))) && listed[1] == (4,false,false,"") && listed[2] == (12,true,true,Hash(Saved(12))), "inventory sorted with unreadable profiles marked");
            File.WriteAllBytes(Path.Combine(directory, "participant-4.json"), Saved(4, name: "changed"));
            Assert(inventory.Scan(directory, matching)[1] == (4,true,true,Hash(Saved(4, name: "changed"))), "repaired file appears in existing inventory");
            bool missing = false;
            try { ParticipantCalibration.Load(directory, 9); }
            catch (FileNotFoundException exception) { missing = exception.Message.StartsWith("No saved calibration for participant 9", StringComparison.Ordinal); }
            Assert(missing, "missing participant calibration explains what to do");
        }
        finally { Directory.Delete(directory, true); }

        string clone = Path.Combine(Path.GetTempPath(), "elts-profiles-path-" + Guid.NewGuid().ToString("N"), "project with spaces");
        try
        {
            Directory.CreateDirectory(Path.Combine(clone, "config")); Directory.CreateDirectory(Path.Combine(clone, "unity", "ProjectSettings"));
            File.WriteAllText(Path.Combine(clone, "config", "toolchain.json"), "{}");
            File.WriteAllText(Path.Combine(clone, "unity", "ProjectSettings", "ProjectVersion.txt"), "");
            Assert(MachineConfiguration.ResolveCalibrationProfiles(Path.Combine(clone, "build", "start-elts-x")) ==
                Path.Combine(clone, "Calibration Environment", "profiles"), "built player finds calibration profiles at project root");
        }
        finally { Directory.Delete(Path.GetDirectoryName(clone)!, true); }
    }
}
