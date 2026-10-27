using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.McpServer.Api;

internal static class PowerShellApiEndpoints
{
    public static void MapPowerShellEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/powershell", async (HttpRequest request, IPowerShellGraphRepository repository, CancellationToken ct) =>
        {
            if (!request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            PowerShellGraphSnapshot? snapshot;
            try
            {
                snapshot = await request.ReadFromJsonAsync<PowerShellGraphSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    AllowDuplicateProperties = false, Converters = { new JsonStringEnumConverter() }
                }, ct);
            }
            catch (JsonException) { return Results.BadRequest("Invalid PowerShell graph JSON payload."); }
            if (snapshot is null) return Results.BadRequest("A PowerShell graph snapshot is required.");
            if (snapshot.Validate() is { } error) return Results.BadRequest(error);
            try { await repository.PublishPowerShellGraphAsync(snapshot, ct); }
            catch (ProjectProfileConflictException ex) { return Results.Conflict(ex.Message); }
            return Results.Ok(new { generation = snapshot.Generation });
        });
    }
}
