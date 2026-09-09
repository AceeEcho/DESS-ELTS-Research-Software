#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// UI preference and operator checklist state. It never stores rig calibration or acceptance evidence.
    public sealed class DashboardWorkspace
    {
        public const int SchemaVersion = 1;
        public static readonly string[] BuiltinModules = { "preparation", "calibration", "run", "notes", "recordings", "devices", "checkpoints" };
        public static readonly string[] BuiltinLiveModules = { "participant", "operator", "health" };
        public static readonly string[] ConditionCodes = { "WE_MT", "NE_MT", "WE_FT", "NE_FT" };

        public int Version { get; set; } = SchemaVersion;
        public List<string> ModuleOrder { get; set; } = new List<string>(BuiltinModules);
        public List<string> LiveOrder { get; set; } = new List<string>(BuiltinLiveModules);
        public string SelectedModule { get; set; } = "preparation";
        public bool ReducedMotion { get; set; }
        // Widths are UI pixels, retained as preferences rather than physical dimensions.
        public float RailWidth { get; set; } = 240f;
        public float LiveWidth { get; set; } = 380f;
        public List<DashboardCheckpoint> Checkpoints { get; set; } = new List<DashboardCheckpoint>();
        public List<DashboardPreset> Presets { get; set; } = new List<DashboardPreset> { DashboardPreset.Standard() };
        // Active condition order is an operator preference only; the run engine owns its execution plan.
        public List<string> ActiveConditionOrder { get; set; } = new List<string>(ConditionCodes);

        public DashboardWorkspace() { }

        public static DashboardWorkspace Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Workspace path is required.");
            try
            {
                if (!File.Exists(path)) throw new InvalidDataException("Workspace file was not found: " + path);
                if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Dashboard workspace exceeds the 2 MiB size limit; the file was left unchanged.");
                // Replace constructor defaults with the persisted collections; otherwise Json.NET would append
                // the default standard preset to the serialized preset list on every load.
                var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace, MissingMemberHandling = MissingMemberHandling.Error };
                var json = JObject.Parse(File.ReadAllText(path), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                var value = json.ToObject<DashboardWorkspace>(JsonSerializer.Create(settings));
                if (value == null) throw new InvalidDataException("Workspace JSON was empty.");
                value.NormalizeAndValidate();
                return value;
            }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            { throw new InvalidDataException("Dashboard workspace could not be loaded; the file was left unchanged. " + ex.Message, ex); }
        }

        public void Save(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidDataException("Workspace path is required.");
            NormalizeAndValidate();
            string full = Path.GetFullPath(path), directory = Path.GetDirectoryName(full) ?? ".";
            Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(this, Formatting.Indented) + Environment.NewLine);
                if (File.Exists(full)) File.Replace(temporary, full, null);
                else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void NormalizeAndValidate()
        {
            if (Version != SchemaVersion) throw new InvalidDataException("Unsupported dashboard workspace schema version " + Version + "; expected " + SchemaVersion + ".");
            ModuleOrder = NormalizeModules(ModuleOrder, BuiltinModules, "ModuleOrder");
            LiveOrder = NormalizeModules(LiveOrder, BuiltinLiveModules, "LiveOrder");
            if (ActiveConditionOrder == null || ActiveConditionOrder.Count != 4 || ActiveConditionOrder.Distinct(StringComparer.Ordinal).Count() != 4 || !ActiveConditionOrder.All(x => ConditionCodes.Contains(x, StringComparer.Ordinal))) throw new InvalidDataException("Active condition order must be an exact permutation of WE_MT, NE_MT, WE_FT, NE_FT.");
            if (string.IsNullOrWhiteSpace(SelectedModule) || SelectedModule.Length > 100 || !BuiltinModules.Contains(SelectedModule)) SelectedModule = "preparation";
            if (float.IsNaN(RailWidth) || float.IsInfinity(RailWidth) || RailWidth < 120 || RailWidth > 1000) throw new InvalidDataException("RailWidth must be finite and between 120 and 1000 UI pixels.");
            if (float.IsNaN(LiveWidth) || float.IsInfinity(LiveWidth) || LiveWidth < 180 || LiveWidth > 1200) throw new InvalidDataException("LiveWidth must be finite and between 180 and 1200 UI pixels.");
            if (Checkpoints == null || Checkpoints.Count > 200) throw new InvalidDataException("Checkpoints must contain between 0 and 200 items.");
            var checkpointIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Checkpoints) { if (item == null) throw new InvalidDataException("Checkpoint entries cannot be null."); item.Validate(); if (!checkpointIds.Add(item.Id)) throw new InvalidDataException("Duplicate checkpoint id: " + item.Id); }
            if (Presets == null || Presets.Count > 30 || Presets.Count == 0) throw new InvalidDataException("Presets must contain between 1 and 30 items.");
            var presetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Presets) { if (item == null) throw new InvalidDataException("Preset entries cannot be null."); item.Validate(); if (!presetIds.Add(item.Id)) throw new InvalidDataException("Duplicate preset id: " + item.Id); }
        }

        private static List<string> NormalizeModules(List<string>? value, string[] builtins, string name)
        {
            var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
            if (value != null) foreach (var module in value) if (!string.IsNullOrWhiteSpace(module) && module.Length <= 100 && seen.Add(module) && builtins.Contains(module)) result.Add(module);
            foreach (var module in builtins) if (seen.Add(module)) result.Add(module);
            return result;
        }

        public DashboardCheckpoint AddCheckpoint(string title, string instructions)
        {
            if (Checkpoints == null) Checkpoints = new List<DashboardCheckpoint>();
            if (Checkpoints.Count >= 200) throw new InvalidOperationException("The maximum of 200 checkpoints has been reached.");
            string id; int n = Checkpoints.Count + 1; do { id = "checkpoint-" + n++; } while (Checkpoints.Any(x => x.Id == id));
            var item = new DashboardCheckpoint { Id = id, Title = title ?? "", Instructions = instructions ?? "" }; item.Validate(); Checkpoints.Add(item); return item;
        }
        public bool RemoveCheckpoint(string id) { var item = Checkpoints?.FirstOrDefault(x => x.Id == id); return item != null && Checkpoints!.Remove(item); }
        public void RestoreCheckpoint(DashboardCheckpoint item, int index) { if (item == null) throw new ArgumentNullException(nameof(item)); item.Validate(); if (Checkpoints == null) Checkpoints = new List<DashboardCheckpoint>(); if (Checkpoints.Any(x => x.Id == item.Id)) throw new InvalidOperationException("Checkpoint id already exists: " + item.Id); if (Checkpoints.Count >= 200) throw new InvalidOperationException("The maximum of 200 checkpoints has been reached."); Checkpoints.Insert(Math.Max(0, Math.Min(index, Checkpoints.Count)), item); }
        public DashboardPreset AddPreset(string name, string[] order) { if (Presets == null) Presets = new List<DashboardPreset>(); if (Presets.Count >= 30) throw new InvalidOperationException("The maximum of 30 presets has been reached."); var p = new DashboardPreset { Id = "preset-" + Guid.NewGuid().ToString("N"), Name = name ?? "", ConditionOrder = order }; p.Validate(); Presets.Add(p); return p; }
        public void UpdatePreset(string id, string name, string[] order) { var p = Presets?.FirstOrDefault(x => x.Id == id) ?? throw new KeyNotFoundException("Preset id was not found: " + id); var replacement = new DashboardPreset { Id = p.Id, Name = name ?? "", ConditionOrder = order }; replacement.Validate(); p.Name = replacement.Name; p.ConditionOrder = replacement.ConditionOrder; }
        public bool RemovePreset(string id) { if (Presets == null) return false; var p = Presets.FirstOrDefault(x => x.Id == id); if (p == null) return false; if (Presets.Count <= 1) throw new InvalidOperationException("At least one preset must remain."); return Presets.Remove(p); }
    }

    public sealed class DashboardCheckpoint
    {
        public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Instructions { get; set; } = ""; public bool Completed { get; set; }
        public DashboardCheckpoint() { }
        internal void Validate() { if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Checkpoint id is invalid."); if (Title == null || Title.Length > 300) throw new InvalidDataException("Checkpoint title must be at most 300 characters."); if (Instructions == null || Instructions.Length > 4000) throw new InvalidDataException("Checkpoint instructions must be at most 4000 characters."); }
    }
    public sealed class DashboardPreset
    {
        public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string[] ConditionOrder { get; set; } = Array.Empty<string>();
        public DashboardPreset() { }
        internal static DashboardPreset Standard() { return new DashboardPreset { Id = "standard", Name = "Standard", ConditionOrder = (string[])DashboardWorkspace.ConditionCodes.Clone() }; }
        internal void Validate() { if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 || Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new InvalidDataException("Preset id is invalid."); if (Name == null || Name.Length > 200) throw new InvalidDataException("Preset name must be at most 200 characters."); if (ConditionOrder == null || ConditionOrder.Length != 4 || ConditionOrder.Distinct(StringComparer.Ordinal).Count() != 4 || !ConditionOrder.All(x => DashboardWorkspace.ConditionCodes.Contains(x, StringComparer.Ordinal))) throw new InvalidDataException("Preset condition order must be an exact permutation of WE_MT, NE_MT, WE_FT, NE_FT."); }
    }
}
