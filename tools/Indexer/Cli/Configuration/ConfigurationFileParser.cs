using System.Text.Json;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace CodeMeridian.Indexer.Cli.Configuration;

internal static class ConfigurationFileParser
{
    public static bool TryParse(
        FileInfo file,
        string rootPath,
        out IReadOnlyList<ConfigurationEntryRecord> entries,
        out string? error)
        => TryParseContent(file, rootPath, File.ReadAllText(file.FullName), out entries, out error);

    public static bool TryParseContent(FileInfo file, string rootPath, string content,
        out IReadOnlyList<ConfigurationEntryRecord> entries, out string? error)
    {
        try
        {
            entries = ParseContent(file, rootPath, content);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            entries = [];
            error = ex.Message;
            return false;
        }
        catch (YamlException ex)
        {
            entries = [];
            error = ex.Message;
            return false;
        }
        catch (FormatException ex)
        {
            entries = [];
            error = ex.Message;
            return false;
        }
    }

    public static IReadOnlyList<ConfigurationEntryRecord> Parse(FileInfo file, string rootPath)
        => ParseContent(file, rootPath, File.ReadAllText(file.FullName));

    private static IReadOnlyList<ConfigurationEntryRecord> ParseContent(FileInfo file, string rootPath, string content)
    {
        if (content.Length > 4 * 1024 * 1024) throw new FormatException("Configuration exceeds its parser budget.");
        var relativePath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        return file.Extension.ToLowerInvariant() switch
        {
            ".json" => ParseJson(content, relativePath),
            ".yml" or ".yaml" => ParseYaml(content, relativePath),
            _ when file.Name.Equals(".env", StringComparison.OrdinalIgnoreCase)
                || file.Name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase) => ParseEnv(content, relativePath),
            _ => throw new FormatException("Configuration format is not audited for definition extraction.")
        };
    }

    private static IReadOnlyList<ConfigurationEntryRecord> ParseJson(string content, string relativePath)
    {
        using var document = JsonDocument.Parse(content, new JsonDocumentOptions { AllowDuplicateProperties = false });
        var results = new List<ConfigurationEntryRecord>();
        WalkJson(document.RootElement, [], relativePath, "json", results);
        return results;
    }

    private static void WalkJson(
        JsonElement element,
        IReadOnlyList<string> path,
        string relativePath,
        string format,
        List<ConfigurationEntryRecord> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    WalkJson(property.Value, [.. path, property.Name], relativePath, format, results);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WalkJson(item, [.. path, index.ToString()], relativePath, format, results);
                    index++;
                }
                break;
            default:
                AddEntry(results, relativePath, format, "json-path", string.Join(':', path), element.ToString(), DescribeJsonValue(element));
                break;
        }
    }

    private static IReadOnlyList<ConfigurationEntryRecord> ParseEnv(string content, string relativePath)
    {
        var results = new List<ConfigurationEntryRecord>();
        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith("export ", StringComparison.Ordinal)) trimmed = trimmed[7..].TrimStart();
            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                throw new FormatException("Environment configuration contains an unsupported statement.");

            var key = trimmed[..separator].Trim();
            if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_')
                || key.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
                throw new FormatException("Environment configuration contains an unsupported key.");
            var value = trimmed[(separator + 1)..].Trim();
            AddEntry(results, relativePath, "env", ".env", key, value, "string");
        }

        return results;
    }

    private static IReadOnlyList<ConfigurationEntryRecord> ParseYaml(string content, string relativePath)
    {
        using var reader = new StringReader(content);
        var stream = new YamlStream();
        stream.Load(reader);

        var results = new List<ConfigurationEntryRecord>();
        foreach (var document in stream.Documents)
            WalkYaml(document.RootNode, [], relativePath, results);

        return results;
    }

    private static void WalkYaml(
        YamlNode node,
        IReadOnlyList<string> path,
        string relativePath,
        List<ConfigurationEntryRecord> results)
    {
        if (path.Count > 64) throw new FormatException("YAML nesting or aliases exceed the audited depth.");
        if (IsEnvironmentSection(path) && node is YamlMappingNode mapping)
        {
            foreach (var child in mapping.Children)
            {
                var key = ReadYamlKey(child.Key);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var value = (child.Value as YamlScalarNode)?.Value;
                AddEntry(results, relativePath, "yaml", "yaml-environment", key, value, "string");
            }

            return;
        }

        if (IsEnvironmentSection(path) && node is YamlSequenceNode sequence)
        {
            foreach (var item in sequence.Children.OfType<YamlScalarNode>())
            {
                var value = item.Value ?? string.Empty;
                var separator = value.IndexOf('=');
                if (separator <= 0)
                    continue;

                AddEntry(results, relativePath, "yaml", "yaml-environment", value[..separator], value[(separator + 1)..], "string");
            }

            return;
        }

        switch (node)
        {
            case YamlMappingNode childMapping:
                foreach (var child in childMapping.Children)
                {
                    var key = ReadYamlKey(child.Key);
                    if (string.IsNullOrWhiteSpace(key))
                        continue;

                    WalkYaml(child.Value, [.. path, key], relativePath, results);
                }
                break;
            case YamlSequenceNode childSequence:
                for (var index = 0; index < childSequence.Children.Count; index++)
                    WalkYaml(childSequence.Children[index], [.. path, index.ToString()], relativePath, results);
                break;
            case YamlScalarNode scalar:
                AddEntry(results, relativePath, "yaml", "yaml-path", string.Join(':', path), scalar.Value, "string");
                break;
        }
    }

    private static bool IsEnvironmentSection(IReadOnlyList<string> path) =>
        path.Count > 0 && path[^1].Equals("environment", StringComparison.OrdinalIgnoreCase);

    private static string? ReadYamlKey(YamlNode keyNode) =>
        keyNode switch
        {
            YamlScalarNode scalar => scalar.Value,
            _ => null
        };

    private static void AddEntry(
        List<ConfigurationEntryRecord> results,
        string relativePath,
        string format,
        string sourceKind,
        string rawKey,
        string? rawValue,
        string valueType)
    {
        if (results.Count >= 10000) throw new FormatException("Configuration entries exceed the audited budget.");
        if (string.IsNullOrWhiteSpace(rawKey))
            return;

        var canonicalKey = ConfigurationKeyNormalizer.Normalize(rawKey);
        var isSecretLike = ConfigurationValueMasker.IsSecretLike(canonicalKey);
        results.Add(new ConfigurationEntryRecord(
            relativePath,
            format,
            sourceKind,
            rawKey,
            canonicalKey,
            valueType,
            ConfigurationValueMasker.CreatePreview(canonicalKey, rawValue),
            isSecretLike));
    }

    private static string DescribeJsonValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => element.ValueKind.ToString().ToLowerInvariant()
        };
}
