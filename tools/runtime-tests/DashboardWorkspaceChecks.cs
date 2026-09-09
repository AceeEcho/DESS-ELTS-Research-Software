#nullable enable
using System;
using System.IO;
using System.Linq;
using Elts.Operator;
using Newtonsoft.Json;

static class DashboardWorkspaceChecks
{
    static int passed;
    static void True(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); passed++; }
    static void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { passed++; return; } catch (ArgumentException) { passed++; return; } catch (InvalidOperationException) { passed++; return; } throw new Exception("FAIL: " + name); }
    static void Main()
    {
        var root = Path.Combine(Path.GetTempPath(), "ELTS dashboard checks " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var workspace = new DashboardWorkspace { ReducedMotion = true, RailWidth = 260, LiveWidth = 420 };
            var checkpoint = workspace.AddCheckpoint("Unicode ✅ checkpoint", "Long instructions: " + new string('x', 500) + " 日本語");
            checkpoint.Completed = true;
            var preset = workspace.AddPreset("Alternate", new[] { "NE_FT", "WE_FT", "NE_MT", "WE_MT" });
            workspace.ActiveConditionOrder = new System.Collections.Generic.List<string> { "WE_FT", "NE_FT", "WE_MT", "NE_MT" };
            string file = Path.Combine(root, "workspace.json"); workspace.Save(file);
            var loaded = DashboardWorkspace.Load(file);
            True(loaded.ReducedMotion && loaded.RailWidth == 260 && loaded.Checkpoints[0].Title.Contains("✅") && loaded.Presets.Any(x => x.Id == preset.Id) && string.Join(",", loaded.ActiveConditionOrder) == "WE_FT,NE_FT,WE_MT,NE_MT", "roundtrip preserves preferences, active condition order, and unicode checklist");

            loaded.ModuleOrder = new System.Collections.Generic.List<string> { "run", "run", "unknown", "preparation" }; loaded.LiveOrder = new System.Collections.Generic.List<string> { "health" }; loaded.NormalizeAndValidate();
            True(string.Join(",", loaded.ModuleOrder) == "run,preparation,calibration,notes,recordings,devices,checkpoints" && string.Join(",", loaded.LiveOrder) == "health,participant,operator", "module order removes duplicates and appends missing builtins");
            True(loaded.RemoveCheckpoint(checkpoint.Id), "checkpoint removal supports undo workflow"); loaded.RestoreCheckpoint(checkpoint, 0); True(loaded.Checkpoints[0].Id == checkpoint.Id, "checkpoint restore honors insertion index");

            File.WriteAllText(file, "{ definitely not json"); string corruptBytes = File.Exists(file) ? File.ReadAllText(file) : ""; Reject(() => DashboardWorkspace.Load(file), "corrupt JSON rejected"); True(File.ReadAllText(file) == corruptBytes, "corrupt load leaves file unchanged");
            Reject(() => JsonConvert.DeserializeObject<DashboardWorkspace>("{\"Version\":99}" )!.NormalizeAndValidate(), "unsupported schema rejected");
            string[] bad = { "{\"Version\":1,\"RailWidth\":0}", "{\"Version\":1,\"LiveWidth\":99999}", "{\"Version\":1,\"ActiveConditionOrder\":[\"WE_MT\",\"WE_MT\",\"WE_FT\",\"NE_FT\"]}", "{\"Version\":1,\"Presets\":[]}", "{\"Version\":1,\"Presets\":[{\"Id\":\"x\",\"Name\":\"x\",\"ConditionOrder\":[\"WE_MT\",\"WE_MT\",\"WE_FT\",\"NE_FT\"]}]}", "{\"Version\":1,\"Checkpoints\":[null]}" };
            foreach (var payload in bad) { File.WriteAllText(file, payload); Reject(() => DashboardWorkspace.Load(file), "unsafe malformed payload rejected"); }
            File.WriteAllText(file, "{\"Version\":1,\"Version\":1}"); Reject(() => DashboardWorkspace.Load(file), "duplicate JSON property rejected");
            File.WriteAllText(file, "{\"Version\":1}");
            using (var stream = new FileStream(file, FileMode.Append)) { stream.SetLength(2 * 1024 * 1024 + 1); }
            Reject(() => DashboardWorkspace.Load(file), "oversized workspace rejected");
            Reject(() => workspace.AddPreset("bad", new[] { "WE_MT", "WE_MT", "WE_FT", "NE_FT" }), "invalid condition permutation rejected");
            while (workspace.Presets.Count < 30) workspace.AddPreset("p", DashboardWorkspace.ConditionCodes.ToArray());
            Reject(() => workspace.AddPreset("too many", DashboardWorkspace.ConditionCodes.ToArray()), "preset limit enforced");
            True(workspace.RemovePreset("missing") == false, "removing unknown preset is harmless");
            while (workspace.Presets.Count > 1) workspace.RemovePreset(workspace.Presets[0].Id);
            Reject(() => workspace.RemovePreset(workspace.Presets[0].Id), "last preset cannot be removed");
            Console.WriteLine("PASS: " + passed + " dashboard workspace checks");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
