using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace Elts.Config
{
    /// <summary>
    /// Local, fail-closed JSON Schema subset used by the staged configuration.
    /// Schemas are the same hashed files used by Python staging. Unsupported
    /// keywords are errors; this is deliberately not a general schema engine.
    /// </summary>
    internal sealed class JsonContract
    {
        private const int MaximumDepth = 64;
        private static readonly HashSet<string> Keywords = new HashSet<string>
        {
            "$schema", "$id", "title", "description", "$comment", "default", "examples",
            "$defs", "$ref", "type", "properties", "required", "additionalProperties",
            "items", "minItems", "maxItems", "uniqueItems", "enum", "const", "pattern",
            "minLength", "maxLength", "minimum", "maximum", "exclusiveMinimum"
        };
        private readonly IReadOnlyDictionary<string, JObject> schemas;

        public JsonContract(IReadOnlyDictionary<string, JObject> schemas)
        {
            this.schemas = schemas;
        }

        public void Validate(JToken value, string schemaName)
        {
            if (!schemas.ContainsKey(schemaName)) Fail("Unknown local schema: " + schemaName);
            Validate(value, schemas[schemaName], schemaName, "$", 0);
        }

        private void Validate(JToken value, JToken rule, string document, string path, int depth)
        {
            if (depth > MaximumDepth) Fail(path + ": schema nesting exceeds limit");
            if (rule.Type == JTokenType.Boolean)
            {
                if (!(bool)rule) Fail(path + ": field is not allowed");
                return;
            }
            if (!(rule is JObject schema)) { Fail(path + ": invalid schema object"); return; }
            foreach (var property in schema.Properties())
                if (!Keywords.Contains(property.Name)) Fail("Unsupported schema keyword: " + property.Name);

            if (schema["$ref"] != null)
            {
                string reference = (string)schema["$ref"];
                string[] parts = reference.Split(new[] { '#' }, 2);
                string filename = parts[0].Length == 0 ? document : parts[0];
                if (filename.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || !schemas.ContainsKey(filename))
                    Fail("Schema references must name a known sibling file");
                JToken target = schemas[filename];
                if (parts.Length == 2 && parts[1].Length > 0)
                {
                    if (!parts[1].StartsWith("/", StringComparison.Ordinal)) Fail("Invalid schema pointer");
                    foreach (string token in parts[1].Substring(1).Split('/'))
                    {
                        string key = token.Replace("~1", "/").Replace("~0", "~");
                        target = (target as JObject)?[key];
                        if (target == null) Fail("Unresolved schema pointer: " + reference);
                    }
                }
                Validate(value, target, filename, path, depth + 1);
            }

            if (schema["type"] != null && !MatchesType(value, (string)schema["type"]))
                Fail(path + ": expected " + (string)schema["type"]);
            if (schema["const"] != null && !JToken.DeepEquals(value, schema["const"]))
                Fail(path + ": wrong constant value");
            if (schema["enum"] is JArray choices && !choices.Any(x => JToken.DeepEquals(x, value)))
                Fail(path + ": value is outside allowed choices");

            if (value is JObject obj)
            {
                if (schema["required"] is JArray required)
                    foreach (string name in required.Values<string>())
                        if (obj.Property(name, StringComparison.Ordinal) == null) Fail(path + ": missing " + name);
                var properties = schema["properties"] as JObject;
                foreach (var property in obj.Properties())
                {
                    JToken propertyRule = properties?[property.Name] ?? schema["additionalProperties"] ?? new JValue(true);
                    Validate(property.Value, propertyRule, document, path + "." + property.Name, depth + 1);
                }
            }
            if (value is JArray array)
            {
                if (array.Count < IntOr(schema["minItems"], 0) || array.Count > IntOr(schema["maxItems"], int.MaxValue))
                    Fail(path + ": invalid array length");
                if ((bool?)schema["uniqueItems"] == true)
                    for (int a = 0; a < array.Count; a++)
                        for (int b = a + 1; b < array.Count; b++)
                            if (JToken.DeepEquals(array[a], array[b])) Fail(path + ": duplicate array value");
                if (schema["items"] != null)
                    for (int i = 0; i < array.Count; i++)
                        Validate(array[i], schema["items"], document, path + "[" + i + "]", depth + 1);
            }
            if (value.Type == JTokenType.String)
            {
                string text = (string)value;
                if (text.Length < IntOr(schema["minLength"], 0) || text.Length > IntOr(schema["maxLength"], int.MaxValue))
                    Fail(path + ": invalid string length");
                if (schema["pattern"] != null && !Regex.IsMatch(text, (string)schema["pattern"], RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100))) Fail(path + ": invalid string format");
            }
            if (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
            {
                double number = Number(value);
                if (schema["minimum"] != null && number < Number(schema["minimum"])) Fail(path + ": below minimum");
                if (schema["maximum"] != null && number > Number(schema["maximum"])) Fail(path + ": above maximum");
                if (schema["exclusiveMinimum"] != null && number <= Number(schema["exclusiveMinimum"]))
                    Fail(path + ": must exceed minimum");
            }
        }

        private static int IntOr(JToken token, int fallback) => token == null ? fallback : (int)token;
        private static bool MatchesType(JToken value, string type)
        {
            switch (type)
            {
                case "object": return value.Type == JTokenType.Object;
                case "array": return value.Type == JTokenType.Array;
                case "string": return value.Type == JTokenType.String;
                case "integer": return value.Type == JTokenType.Integer;
                case "number": return value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
                case "boolean": return value.Type == JTokenType.Boolean;
                case "null": return value.Type == JTokenType.Null;
                default: Fail("Unsupported schema type: " + type); return false;
            }
        }

        internal static double Number(JToken value)
        {
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                Fail("Expected a JSON number");
            if (!double.TryParse(value.ToString(Newtonsoft.Json.Formatting.None), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double number) || double.IsNaN(number) || double.IsInfinity(number))
                Fail("JSON numbers must be finite and representable");
            return number;
        }

        internal static void Fail(string message) => throw new ArgumentException("Configuration: " + message);
    }
}
