using CodeMeridian.Core.Knowledge;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Knowledge;

internal static class Neo4jVectorRepositoryHelpers
{
    internal static KnowledgeDocument MapToDocument(INode node)
    {
        var props = node.Properties;

        DateTimeOffset? ReadTimestamp(string key)
        {
            if (!props.TryGetValue(key, out var raw) || raw is null) return null;
            var ms = raw.As<long?>();
            return ms.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ms.Value) : null;
        }

        return new KnowledgeDocument
        {
            Id = props["id"].As<string>(),
            Content = props["content"].As<string>(),
            Source = props.TryGetValue("source", out var src) ? src?.As<string>() : null,
            ProjectContext = props.TryGetValue("projectContext", out var pc) ? pc?.As<string>() : null,
            CreatedAt = ReadTimestamp("createdAt"),
            UpdatedAt = ReadTimestamp("updatedAt"),
            Metadata = ReadMetadata(props)
        };
    }

    internal static Dictionary<string, string> ReadMetadata(IReadOnlyDictionary<string, object?> props)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (props.TryGetValue("relatedNodeIds", out var relatedNodeIds) && relatedNodeIds is not null)
            metadata["relatedNodeIds"] = relatedNodeIds.As<string>();

        if (props.TryGetValue("relatedDocumentIds", out var relatedDocumentIds) && relatedDocumentIds is not null)
            metadata["relatedDocumentIds"] = relatedDocumentIds.As<string>();

        if (props.TryGetValue("metadataKind", out var metadataKind) && metadataKind is not null)
            metadata["kind"] = metadataKind.As<string>();

        return metadata;
    }

    internal static List<string> ExtractMentionIds(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "relatedNodeIds", "relatedNodes", "mentions" })
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            return raw
                .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    internal static List<string> ExtractRelatedDocumentIds(IReadOnlyDictionary<string, string> metadata)
    {
        foreach (var key in new[] { "relatedDocumentIds", "references", "relatedDocuments" })
        {
            if (!metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
                continue;

            return raw
                .Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return [];
    }

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    internal static string EscapeLuceneQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return string.Empty;

        var sb = new System.Text.StringBuilder(query.Length * 2);
        foreach (var ch in query.Trim())
        {
            if ("+-!(){}[]^\"~*?:\\/".Contains(ch))
                sb.Append('\\');

            sb.Append(ch);
        }

        return sb.ToString();
    }
}
