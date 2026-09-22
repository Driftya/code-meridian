using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private static GraphPathStep MapPathStep(CodeNode node, string? relationshipType,
        double? confidence, IDictionary<string, object>? relationship)
    {
        string? ReadString(string key) => relationship is not null && relationship.TryGetValue(key, out var value)
            ? value?.ToString() : null;
        int? ReadInt(string key) => int.TryParse(ReadString(key), out var value) ? value : null;
        var kind = Enum.TryParse<EdgeEvidenceKind>(ReadString("evidenceKind"), true, out var parsed)
            && Enum.IsDefined(parsed) ? parsed : EdgeEvidenceKind.Unknown;

        return new GraphPathStep(node, relationshipType, confidence)
        {
            EvidenceKind = kind,
            EvidenceReason = ReadString("evidenceReason"),
            Resolver = ReadString("resolver"),
            SourceFilePath = ReadString("sourceFilePath"),
            SourceLine = ReadInt("sourceLine"),
            SourceColumn = ReadInt("sourceColumn"),
            SourceEndLine = ReadInt("sourceEndLine"),
            SourceEndColumn = ReadInt("sourceEndColumn")
        };
    }
}
