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
    {
        try
        {
            entries = Parse(file, rootPath);
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
    }

    public static IReadOnlyList<ConfigurationEntryRecord> Parse(FileInfo file, string rootPath)
    {
        var relativePath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        return file.Extension.ToLowerInvariant() switch
        {
            ".json" => ParseJson(file, relativePath),
            ".yml" or ".yaml" => ParseYaml(file, relativePath),
            _ when file.Name.Equals(".env", StringComparison.OrdinalIgnoreCase) => ParseEnv(file, relativePath),
            _ => []
        };
    }

    private static IReadOnlyList<ConfigurationEntryRecord> ParseJson(FileInfo file, string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file.FullName));
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

    private static IReadOnlyList<ConfigurationEntryRecord> ParseEnv(FileInfo file, string relativePath)
    {
        var results = new List<ConfigurationEntryRecord>();
        foreach (var line in File.ReadLines(file.FullName))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();
            AddEntry(results, relativePath, "env", ".env", key, value, "string");
        }

        return results;
    }

    private static IReadOnlyList<ConfigurationEntryRecord> ParseYaml(FileInfo file, string relativePath)
    {
        using var reader = file.OpenText();
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
