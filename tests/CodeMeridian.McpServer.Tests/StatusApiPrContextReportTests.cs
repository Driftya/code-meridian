using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Api;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SdkPrContextReportRequest = CodeMeridian.Sdk.PrContextReportRequest;

namespace CodeMeridian.McpServer.Tests;

public sealed class StatusApiPrContextReportTests
{
    [Fact]
    public async Task BuildPrContextReport_ReturnsJsonPayload()
    {
        var reportService = Substitute.For<IPrContextReportService>();
        reportService.BuildAsync(Arg.Any<Application.Services.PrContextReportRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Application.Services.PrContextReport(
                "CodeMeridian",
                "origin/main",
                "HEAD",
                ["src/App.cs"],
                [new Application.Services.PrContextNodeSummary("node-1", "App", "Class", "src/App.cs", "CodeMeridian", 1, 20)],
                [],
                [],
                [],
                [new Application.Services.PrContextRelatedDocument("doc-1", "docs/features/app.md", "High", 8.2d, ["app"])],
                ["Review App changes."])
            {
                Applicability = new("ok", 7, null, ".", ["Enabled report sections have complete coverage."])
                { Project = "CodeMeridian", Coverage = "complete", Limitations = ["Published inventory scope only."], NextActions = [] }
            });

        var method = typeof(StatusApiEndpoints).GetMethod("BuildPrContextReport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull();

        var result = await (Task<IResult>)method!.Invoke(
            null,
            [new SdkPrContextReportRequest(["src/App.cs"], "CodeMeridian", "origin/main", "HEAD"), reportService, CancellationToken.None])!;

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddRouting()
            .BuildServiceProvider();

        await result.ExecuteAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Position = 0;
        var payload = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        payload.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
        payload.GetProperty("relatedDocuments")[0].GetProperty("source").GetString().Should().Be("docs/features/app.md");
        var applicability = payload.GetProperty("applicability");
        applicability.GetProperty("status").GetString().Should().Be("ok");
        applicability.GetProperty("profileGeneration").GetInt64().Should().Be(7);
        applicability.GetProperty("coverage").GetString().Should().Be("complete");
        applicability.GetProperty("contractVersion").GetString().Should().Be("1.0");
        applicability.GetProperty("limitations")[0].GetString().Should().Be("Published inventory scope only.");
    }
}
