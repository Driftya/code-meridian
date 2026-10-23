using CodeMeridian.Application.Services;

namespace CodeMeridian.McpServer.Api;

internal static class DocumentSearchApiEndpoints
{
    public static IEndpointRouteBuilder MapDocumentSearchApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/knowledge/documents/search", async (string query, string? projectContext,
            string? rootPath, string? targetPath, ICodebaseQueryService service, CancellationToken ct) =>
        {
            try { return Results.Text(await service.SearchDocumentationAsync(query, projectContext, ct, rootPath, targetPath), "text/markdown"); }
            catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
        }).RequireAuthorization();
        return app;
    }
}
