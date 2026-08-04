namespace CodeMeridian.McpServer;

internal static class McpArgumentValidation
{
    internal const int MaximumProjectContextLength = 200;

    public static void ValidateProjectContext(string? projectContext)
    {
        if (projectContext is null)
            return;
        if (projectContext.Length > MaximumProjectContextLength)
        {
            throw new ArgumentException(
                $"Project context must not exceed {MaximumProjectContextLength} characters.",
                nameof(projectContext));
        }
        if (projectContext.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Project context must not contain control characters.",
                nameof(projectContext));
        }
    }
}
