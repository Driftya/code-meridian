using CodeMeridian.Core.KeywordGraph;

namespace CodeMeridian.Application.Services;

public interface IKeywordExtractionService
{
    KeywordExtractionResult Extract(KeywordSourceNode input);
}

public sealed record KeywordExtractionResult(string Checksum, IReadOnlyList<ExtractedKeyword> Keywords);
