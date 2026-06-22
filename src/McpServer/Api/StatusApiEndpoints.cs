using CodeMeridian.Application.Services;
using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Versioning;
using ApplicationPrContextReportRequest = CodeMeridian.Application.Services.PrContextReportRequest;
using ApplicationPrContextNodeSummary = CodeMeridian.Application.Services.PrContextNodeSummary;

namespace CodeMeridian.McpServer.Api;

public static class StatusApiEndpoints
{
    public static IEndpointRouteBuilder MapStatusApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/status").WithTags("Status");

        group.MapGet("/doctor", GetDoctorStatus);
        group.MapGet("/report", GetArchitectureReport);
        group.MapGet("/trace-endpoint", TraceEndpoint);
        group.MapPost("/report/pr-context", BuildPrContextReport);
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

    private static async Task<IResult> GetArchitectureReport(
        string? projectContext,
        ICodebaseQueryService queryService,
        CancellationToken ct)
    {
        var report = await queryService.GetArchitectureWeatherReportAsync(projectContext, ct);
        return Results.Text(report, "text/markdown");
    }

    private static async Task<IResult> TraceEndpoint(
        string route,
        string? projectContext,
        string? detailLevel,
        ICodebaseQueryService queryService,
        CancellationToken ct)
    {
        var parsedDetailLevel = Enum.TryParse<ContextDetailLevel>(detailLevel, ignoreCase: true, out var value)
            ? value
            : ContextDetailLevel.Compact;
        var report = await queryService.TraceEndpointAsync(route, projectContext, parsedDetailLevel, ct);
        return Results.Text(report, "text/markdown");
    }

    private static async Task<IResult> BuildPrContextReport(
        CodeMeridian.Sdk.PrContextReportRequest request,
        IPrContextReportService reportService,
        CancellationToken ct)
    {
        var report = await reportService.BuildAsync(
            new ApplicationPrContextReportRequest(
                request.ProjectContext,
                request.ChangedFiles,
                request.BaseRef,
                request.HeadRef,
                request.IncludeDocs,
                request.ImpactDepth,
                request.Limit),
            ct);

        return Results.Ok(new PrContextReportResponse(
            report.ProjectContext,
            report.BaseRef,
            report.HeadRef,
            report.ChangedFiles,
            report.ChangedNodes.Select(MapNode).ToArray(),
            report.ImpactedNodes.Select(item => new PrContextImpactSummaryResponse(
                MapNode(item.Node),
                item.Distance,
                item.ChangedNodeMatches)).ToArray(),
            report.MissingTestNodes.Select(MapNode).ToArray(),
            report.HotspotWarnings.Select(item => new PrContextHotspotWarningResponse(
                MapNode(item.Node),
                item.Reason,
                item.FanIn,
                item.ChangeCount)).ToArray(),
            report.RelatedDocuments.Select(item => new PrContextRelatedDocumentResponse(
                item.Id,
                item.Source,
                item.Confidence,
                item.Score,
                item.MatchedKeywords)).ToArray(),
            report.ReviewFocus));
    }

    private static PrContextNodeSummaryResponse MapNode(ApplicationPrContextNodeSummary node) =>
        new(
            node.Id,
            node.Name,
            node.Type,
            node.FilePath,
            node.ProjectContext,
            node.LineNumber,
            node.LineCount);

    private static IResult GetVersion() =>
        Results.Ok(CodeMeridianVersionReader.ReadFrom(typeof(StatusApiEndpoints).Assembly, "CodeMeridian.McpServer"));
}
