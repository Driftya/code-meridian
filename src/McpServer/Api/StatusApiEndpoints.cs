using CodeMeridian.Application.Services;
using CodeMeridian.Sdk.Versioning;

namespace CodeMeridian.McpServer.Api;

public static class StatusApiEndpoints
{
    public static IEndpointRouteBuilder MapStatusApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/status").WithTags("Status");

        group.MapGet("/doctor", GetDoctorStatus);
        group.MapGet("/version", GetVersion);

        return app;
    }

    private static async Task<IResult> GetDoctorStatus(
        string? projectContext,
        ICodebaseStatusService statusService,
        CancellationToken ct)
    {
        var status = await statusService.GetDoctorStatusAsync(projectContext, ct);
        return Results.Ok(status);
    }

    private static IResult GetVersion() =>
        Results.Ok(CodeMeridianVersionReader.ReadFrom(typeof(StatusApiEndpoints).Assembly, "CodeMeridian.McpServer"));
}
