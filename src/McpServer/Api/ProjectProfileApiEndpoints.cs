using System.Text.Json;
using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.McpServer.Api;

internal static class ProjectProfileApiEndpoints
{
    public static IEndpointRouteBuilder MapProjectProfileApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/project-profiles").RequireAuthorization();
        group.MapPost("/begin", async (ProjectProfileBeginRequest request, IProjectProfileRepository repository, CancellationToken ct) =>
        {
            if (!ValidProject(request.ProjectContext)) return Results.BadRequest("A valid project context is required.");
            return Results.Ok(new { generation = await repository.BeginAsync(request.ProjectContext, ct) });
        });
        group.MapPost("/", async (HttpRequest request, IProjectProfileRepository repository, CancellationToken ct) =>
        {
            if (!request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            ProjectProfileSnapshot? snapshot;
            try { snapshot = await request.ReadFromJsonAsync<ProjectProfileSnapshot>(cancellationToken: ct); }
            catch (JsonException) { return Results.BadRequest("Invalid project profile JSON payload."); }
            if (snapshot is null) return Results.BadRequest("A project profile snapshot is required.");
            if (snapshot.Validate() is { } error) return Results.BadRequest(error);
            try { await repository.PublishAsync(snapshot, ct); }
            catch (ProjectProfileConflictException ex) { return Results.Conflict(ex.Message); }
            return Results.Ok(new { generation = snapshot.Generation });
        });
        group.MapGet("/", async (string projectContext, string? targetPath, ProjectProfileService service, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.GetAsync(projectContext, targetPath, ct)); }
            catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
        });
        return app;
    }

    private static bool ValidProject(string? project) =>
        !string.IsNullOrWhiteSpace(project) && project.Length <= 200 && project == project.Trim() && !project.Any(char.IsControl);
}
