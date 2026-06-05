using CodeMeridian.Application.Services;

namespace CodeMeridian.McpServer.Api;

public static class StatusApiEndpoints
{
    public static IEndpointRouteBuilder MapStatusApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/status").WithTags("Status");

        group.MapGet("/doctor", GetDoctorStatus);

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
}
