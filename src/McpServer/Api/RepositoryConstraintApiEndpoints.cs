using CodeMeridian.Application.RepositoryConstraints;

namespace CodeMeridian.McpServer.Api;

internal static class RepositoryConstraintApiEndpoints
{
    public static IEndpointRouteBuilder MapRepositoryConstraintApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/repository-constraints", async (string projectContext, string? targetPath, int? findingLimit,
            RepositoryConstraintQueryService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.FindAsync(projectContext, targetPath, findingLimit ?? 100, ct)); }
            catch (ArgumentException exception) { return Results.BadRequest(exception.Message); }
        }).RequireAuthorization();
        return app;
    }
}
