using CodeMeridian.Core.CodeGraph;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMeridian.McpServer.Api;

internal static class SqlApiEndpoints
{
    private static readonly JsonSerializerOptions WorkerJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static void MapSqlEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/sql", async (HttpRequest request, ISqlGraphRepository repository, CancellationToken ct) =>
        {
            if (!request.HasJsonContentType()) return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
            SqlGraphSnapshot? snapshot;
            try { snapshot = await request.ReadFromJsonAsync<SqlGraphSnapshot>(WorkerJson, ct); }
            catch (JsonException) { return Results.BadRequest("Invalid SQL graph JSON payload."); }
            if (snapshot is null) return Results.BadRequest("SQL graph snapshot is required.");
            if (snapshot.Validate() is { } error) return Results.BadRequest(error);
            await repository.PublishSqlGraphAsync(snapshot, ct);
            return Results.Ok();
        });
    }
}
