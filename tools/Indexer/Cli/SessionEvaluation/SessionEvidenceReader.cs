using System.Text.Json;

namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class SessionEvidenceReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public IReadOnlyList<SessionEvidenceEvent> Read(FileInfo sessionFile)
    {
        if (!sessionFile.Exists)
            throw new FileNotFoundException($"Session evidence file not found: {sessionFile.FullName}", sessionFile.FullName);

        var events = new List<SessionEvidenceEvent>();
        var lineNumber = 0;

        foreach (var rawLine in File.ReadLines(sessionFile.FullName))
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            try
            {
                var item = JsonSerializer.Deserialize<SessionEvidenceEvent>(line, JsonOptions);
                if (item is not null)
                    events.Add(item);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSONL session evidence at {sessionFile.FullName}:{lineNumber}: {ex.Message}", ex);
            }
        }

        return events;
    }
}
