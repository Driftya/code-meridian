using System.Text;

namespace CodeMeridian.Tooling.Documents;

public static class DocumentTextSplitter
{
    public static IReadOnlyList<string> SplitIntoChunks(string text, int maxChars = 4_000)
    {
        if (text.Length <= maxChars)
            return [text];

        var chunks = new List<string>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                for (var offset = 0; offset < paragraph.Length; offset += maxChars)
                {
                    var slice = paragraph[offset..Math.Min(offset + maxChars, paragraph.Length)].Trim();
                    if (slice.Length > 0)
                        chunks.Add(slice);
                }

                continue;
            }

            var separatorLength = current.Length > 0 ? 2 : 0;
            if (current.Length + separatorLength + paragraph.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append("\n\n");

            current.Append(paragraph);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks.Count > 0 ? chunks : [normalized[..Math.Min(maxChars, normalized.Length)]];
    }
}
