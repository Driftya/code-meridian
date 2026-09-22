using System.Text.Json.Serialization;

namespace CodeMeridian.Core.CodeGraph;

[JsonConverter(typeof(JsonStringEnumConverter<EdgeEvidenceKind>))]
public enum EdgeEvidenceKind
{
    [JsonStringEnumMemberName("unknown")]
    Unknown,
    [JsonStringEnumMemberName("extracted")]
    Extracted,
    [JsonStringEnumMemberName("inferred")]
    Inferred,
    [JsonStringEnumMemberName("ambiguous")]
    Ambiguous
}
