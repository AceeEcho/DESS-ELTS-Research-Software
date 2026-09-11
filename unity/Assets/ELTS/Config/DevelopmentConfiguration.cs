using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Elts.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Elts.Config
{
    /// <summary>
    /// Immutable startup snapshot. Load on a setup/worker path, never in a
    /// frame/sampling callback. No other runtime module reads arbitrary JSON.
    /// This v1 implementation is deliberately incapable of study readiness.
    /// </summary>
    public sealed class DevelopmentConfiguration
    {
        private const int MaximumJsonBytes = 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly string[] SchemaNames = {
            "effective.schema.json", "runtime.schema.json", "rig.schema.json",
            "scenario.schema.json", "session.schema.json", "local.schema.json"
        };
        public string EffectiveSha256 { get; }
        public IReadOnlyDictionary<string, string> SourceRawSha256 { get; }
        public RuntimeConfiguration Runtime { get; }
        public RigConfiguration Rig { get; }
        public ScenarioConfiguration Scenario { get; }
        public SessionConfiguration Session { get; }
        public MachineConfiguration Machine { get; }
        public string Mode => "synthetic";
        public bool StudyReady => false;

        private DevelopmentConfiguration(JObject root, string hash, IDictionary<string, string> sourceHashes)
        {
            if ((string)root["mode"] != "synthetic" || root["studyReady"]?.Type != JTokenType.Boolean || (bool)root["studyReady"])
                JsonContract.Fail("study mode is unavailable; physical calibration, equipment and approvals remain pending");
            EffectiveSha256 = hash;
            SourceRawSha256 = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(sourceHashes));
            Runtime = new RuntimeConfiguration((JObject)root["runtime"]);
            Rig = new RigConfiguration((JObject)root["rig"]);
            Scenario = new ScenarioConfiguration((JObject)root["scenario"]);
            Session = new SessionConfiguration((JObject)root["session"]);
            Machine = new MachineConfiguration((JObject)root["machine"]);
        }

        public void RequireStudyReadiness()
        {
            throw new InvalidOperationException("Study mode is unavailable in this synthetic development build.");
        }

        public static DevelopmentConfiguration LoadDirectory(string directory)
        {
            string root = Path.GetFullPath(directory);
            JObject manifest = ReadJson(ReadFile(root, "manifest.json"));
            RequireFields(manifest, "schemaVersion", "mode", "effectiveConfig", "effectiveConfigSha256", "sourceRawSha256", "schema", "schemaFilesSha256");
            if ((int?)manifest["schemaVersion"] != 1 || (string)manifest["mode"] != "synthetic"
                || (string)manifest["effectiveConfig"] != "effective-config.json"
                || (string)manifest["schema"] != "schemas/effective.schema.json")
                JsonContract.Fail("unsupported manifest or study mode");
            var schemaHashes = manifest["schemaFilesSha256"] as JObject;
            if (schemaHashes == null) JsonContract.Fail("schema hash table is required");
            RequireFields(schemaHashes, SchemaNames);
            var schemas = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (string name in SchemaNames)
            {
                byte[] bytes = ReadFile(root, "schemas/" + name);
                RequireHash(bytes, (string)schemaHashes[name], name);
                schemas.Add(name, ReadJson(bytes));
            }
            byte[] effectiveBytes = ReadFile(root, "effective-config.json");
            string hash = (string)manifest["effectiveConfigSha256"];
            RequireHash(effectiveBytes, hash, "effective-config.json");
            JObject effective = ReadJson(effectiveBytes);
            new JsonContract(schemas).Validate(effective, "effective.schema.json");
            JObject rawHashes = manifest["sourceRawSha256"] as JObject;
            if (rawHashes == null) JsonContract.Fail("source hash table is required");
            RequireFields(rawHashes, "runtime", "rig", "scenario", "session", "local", "staging");
            var sources = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in rawHashes.Properties())
            {
                string value = (string)property.Value;
                if (!IsHash(value)) JsonContract.Fail("invalid source hash: " + property.Name);
                sources.Add(property.Name, value);
            }
            return new DevelopmentConfiguration(effective, hash, sources);
        }

        private static byte[] ReadFile(string root, string relative)
        {
            string current = root;
            if (!Directory.Exists(root)) JsonContract.Fail("missing configuration directory");
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) JsonContract.Fail("configuration directory cannot be a link");
            foreach (string segment in relative.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." || segment.IndexOfAny(new[] { ':', '\\' }) >= 0)
                    JsonContract.Fail("invalid configuration path");
                current = Path.Combine(current, segment);
                if (!File.Exists(current) && !Directory.Exists(current)) JsonContract.Fail("missing staged file: " + relative);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) JsonContract.Fail("staged files cannot be links");
            }
            var info = new FileInfo(current);
            if (info.Length > MaximumJsonBytes) JsonContract.Fail("staged JSON exceeds size limit");
            return File.ReadAllBytes(current);
        }

        private static JObject ReadJson(byte[] bytes)
        {
            using (var text = new StringReader(StrictUtf8.GetString(bytes)))
            using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 })
            {
                var value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) JsonContract.Fail("trailing JSON content");
                // Newtonsoft accepts NaN/Infinity extensions; this contract does not.
                foreach (JToken token in value.DescendantsAndSelf())
                    if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) JsonContract.Number(token);
                return value;
            }
        }

        private static void RequireFields(JObject value, params string[] fields)
        {
            if (value == null || !new HashSet<string>(value.Properties().Select(p => p.Name), StringComparer.Ordinal).SetEquals(fields))
                JsonContract.Fail("missing or unknown object fields");
        }
        private static bool IsHash(string value) => value != null && Regex.IsMatch(value, "^[0-9a-f]{64}$");
        private static void RequireHash(byte[] bytes, string expected, string name)
        {
            using (var algorithm = SHA256.Create())
            {
                string actual = BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (!IsHash(expected) || !string.Equals(actual, expected, StringComparison.Ordinal))
                    JsonContract.Fail("checksum mismatch: " + name);
            }
        }
        internal static double Number(JObject value, string key) => JsonContract.Number(value[key]);
        internal static Vector3d Vector(JToken token) => new Vector3d(JsonContract.Number(token[0]), JsonContract.Number(token[1]), JsonContract.Number(token[2]));
    }

    public sealed class RuntimeConfiguration
    {
        public double SampleRateHz { get; }
        public double RenderHeadPredictionSeconds { get; }
        public double TriggerLockoutSeconds { get; }
        public int SampleQueueCapacity { get; }
        public int EventQueueCapacity { get; }
        public double LogFlushSeconds { get; }
        public double DiagnosticIntervalSeconds { get; }
        internal RuntimeConfiguration(JObject value)
        {
            // Enforce scientific boundaries even if someone edits/re-hashes schemas.
            if ((string)value["trackingSource"] != "synthetic" || DevelopmentConfiguration.Number(value, "loggedPredictionSeconds") != 0)
                JsonContract.Fail("synthetic source and zero logged prediction are required");
            SampleRateHz = DevelopmentConfiguration.Number(value, "sampleRateHz");
            RenderHeadPredictionSeconds = DevelopmentConfiguration.Number(value, "renderHeadPredictionSeconds");
            TriggerLockoutSeconds = DevelopmentConfiguration.Number(value, "triggerLockoutSeconds");
            SampleQueueCapacity = (int)value["sampleQueueCapacity"];
            EventQueueCapacity = (int)value["eventQueueCapacity"];
            LogFlushSeconds = DevelopmentConfiguration.Number(value, "logFlushSeconds");
            DiagnosticIntervalSeconds = DevelopmentConfiguration.Number(value, "diagnosticIntervalSeconds");
        }
    }

    public sealed class RigConfiguration
    {
        public string RigId { get; }
        public string GeometryStatus => "synthetic_unmeasured";
        public ScreenPlane Display { get; }
        public Vector3d HeadEyeOffsetM { get; }
        public Vector3d WeaponMuzzleOffsetM { get; }
        public Vector3d WeaponBoreLocalDirection { get; }
        public Quaterniond WeaponZero { get; }
        public string HeadSerial { get; }
        public string WeaponSerial { get; }
        public string CalibrationId { get; }
        internal RigConfiguration(JObject value)
        {
            RigId = (string)value["rigId"];
            if ((string)value["geometryStatus"] != GeometryStatus || (string)value["coordinateFrame"] != "unity_left_handed_m"
                || (string)value["calibration"]["status"] != "synthetic_only" || (string)value["calibration"]["rigId"] != RigId)
                JsonContract.Fail("unmeasured synthetic geometry and matching calibration rig ID are required");
            var display = value["display"];
            Vector3d origin = DevelopmentConfiguration.Vector(display["lowerLeftM"]);
            Vector3d horizontal = DevelopmentConfiguration.Vector(display["lowerRightM"]) - origin;
            Vector3d vertical = DevelopmentConfiguration.Vector(display["upperLeftM"]) - origin;
            Display = new ScreenPlane(origin, horizontal, vertical, horizontal.Length, vertical.Length);
            HeadEyeOffsetM = DevelopmentConfiguration.Vector(value["headEyeOffsetM"]);
            WeaponMuzzleOffsetM = DevelopmentConfiguration.Vector(value["weaponMuzzleOffsetM"]);
            WeaponBoreLocalDirection = DevelopmentConfiguration.Vector(value["weaponBoreLocalDirection"]);
            if (Math.Abs(WeaponBoreLocalDirection.Length - 1) > 1e-9) JsonContract.Fail("bore direction must be unit length");
            JToken q = value["weaponZeroQuaternionXyzw"];
            double x = JsonContract.Number(q[0]), y = JsonContract.Number(q[1]), z = JsonContract.Number(q[2]), w = JsonContract.Number(q[3]);
            if (Math.Abs(Math.Sqrt(x * x + y * y + z * z + w * w) - 1) > 1e-9) JsonContract.Fail("weapon zero must be a unit quaternion");
            WeaponZero = new Quaterniond(x, y, z, w);
            HeadSerial = (string)value["trackerSerials"]["head"];
            WeaponSerial = (string)value["trackerSerials"]["weapon"];
            if (HeadSerial == WeaponSerial) JsonContract.Fail("head and weapon serials must differ");
            CalibrationId = (string)value["calibration"]["calibrationId"];
        }
    }

    public sealed class ScenarioConfiguration
    {
        public string ScenarioId { get; }
        public int Seed { get; }
        public double TargetDistanceM { get; }
        public double TargetRadiusM { get; }
        public double TargetSpeedMps { get; }
        public double TargetLifetimeSeconds { get; }
        public double SpawnIntervalSeconds { get; }
        public double SpawnWidthM { get; }
        public double SpawnHeightM { get; }
        public double FtHoldSeconds { get; }
        public string MtMotion { get; }
        internal ScenarioConfiguration(JObject value)
        {
            if ((bool?)value["provisionalDevelopmentValues"] != true) JsonContract.Fail("scenario values must be explicitly provisional");
            ScenarioId = (string)value["scenarioId"]; Seed = (int)value["seed"];
            TargetDistanceM = DevelopmentConfiguration.Number(value, "targetDistanceM");
            TargetRadiusM = DevelopmentConfiguration.Number(value, "targetRadiusM");
            TargetSpeedMps = DevelopmentConfiguration.Number(value, "targetSpeedMps");
            TargetLifetimeSeconds = DevelopmentConfiguration.Number(value, "targetLifetimeSeconds");
            SpawnIntervalSeconds = DevelopmentConfiguration.Number(value, "spawnIntervalSeconds");
            SpawnWidthM = DevelopmentConfiguration.Number(value, "spawnWidthM");
            SpawnHeightM = DevelopmentConfiguration.Number(value, "spawnHeightM");
            FtHoldSeconds = DevelopmentConfiguration.Number(value, "ftHoldSeconds");
            MtMotion = (string)value["mtMotion"];
        }
    }

    public sealed class SessionConfiguration
    {
        public string SessionId { get; }
        public double BlockDurationSeconds { get; }
        public double PracticeDurationSeconds { get; }
        public double BreakDurationSeconds { get; }
        public IReadOnlyList<string> ConditionOrder { get; }
        public int BlockRepeats { get; }
        internal SessionConfiguration(JObject value)
        {
            if ((bool?)value["syntheticOnly"] != true || DevelopmentConfiguration.Number(value, "blockDurationSeconds") != 300)
                JsonContract.Fail("synthetic-only session with fixed 300-second blocks is required");
            SessionId = (string)value["sessionId"];
            BlockDurationSeconds = DevelopmentConfiguration.Number(value, "blockDurationSeconds");
            PracticeDurationSeconds = DevelopmentConfiguration.Number(value, "practiceDurationSeconds");
            BreakDurationSeconds = DevelopmentConfiguration.Number(value, "breakDurationSeconds");
            string[] order = value["conditionOrder"].Values<string>().ToArray();
            if (order.Length != 4 || !new HashSet<string>(order).SetEquals(new[] { "WE_MT", "NE_MT", "WE_FT", "NE_FT" }))
                JsonContract.Fail("condition order must contain each development condition once");
            ConditionOrder = Array.AsReadOnly(order);
            BlockRepeats = (int)value["blockRepeats"];
        }
    }

    public sealed class MachineConfiguration
    {
        public int ParticipantDisplayIndex { get; }
        public int OperatorDisplayIndex { get; }
        public string DataRoot { get; }
        public string EltsEndpoint { get; }
        /// <summary>
        /// Keep recordings in the full project clone across Editor runs and rebuilt players.
        /// A standalone copied player uses its own installation directory instead.
        /// </summary>
        public string ResolveDataRoot(string installationDirectory)
        {
            var installation = new DirectoryInfo(Path.GetFullPath(installationDirectory));
            for (var candidate = installation; candidate != null; candidate = candidate.Parent)
            {
                // Repository markers work for ordinary clones and Git worktrees alike.
                if (File.Exists(Path.Combine(candidate.FullName, "config", "toolchain.json")) &&
                    File.Exists(Path.Combine(candidate.FullName, "unity", "ProjectSettings", "ProjectVersion.txt")))
                    return Path.GetFullPath(Path.Combine(candidate.FullName, DataRoot));
            }
            return Path.GetFullPath(Path.Combine(installation.FullName, DataRoot));
        }
        internal MachineConfiguration(JObject value)
        {
            ParticipantDisplayIndex = (int)value["participantDisplayIndex"];
            OperatorDisplayIndex = (int)value["operatorDisplayIndex"];
            if (ParticipantDisplayIndex == OperatorDisplayIndex) JsonContract.Fail("display indices must differ");
            DataRoot = (string)value["dataRoot"];
            if (string.IsNullOrWhiteSpace(DataRoot) || Path.IsPathRooted(DataRoot) || DataRoot.IndexOfAny(new[] { ':', '\\' }) >= 0
                || DataRoot.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.TrimEnd(' ', '.') != p
                    || Regex.IsMatch(p, "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\\.|$)", RegexOptions.IgnoreCase)))
                JsonContract.Fail("data root must be a portable relative directory without traversal or reserved names");
            EltsEndpoint = (string)value["eltsEndpoint"];
            if (EltsEndpoint != "mock://elts" && EltsEndpoint != "null://elts") JsonContract.Fail("physical output is not enabled in development");
        }
    }
}
