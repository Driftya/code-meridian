using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CodeMeridian.McpServer.Tools;

internal static class StructuredToolResult
{
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(McpJsonUtilities.DefaultOptions)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static CallToolResult Create<T>(string text, T value)
        where T : notnull =>
        new()
        {
            Content = [new TextContentBlock { Text = text }],
            StructuredContent = JsonSerializer.SerializeToElement(value, StructuredJsonOptions)
        };

    public static CallToolResult TextOnly(string text) =>
        new()
        {
            Content = [new TextContentBlock { Text = text }]
        };
}
