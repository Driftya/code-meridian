using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class RelationshipHealthReportCommandTests
{
    [Fact]
    public async Task RunAsync_PrintsLatestBoundedScopeEvidenceWithPercentagesAndFileRoles()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new RelationshipHealthReportCommand((_, _) => new HttpClient(new StubHandler(Response()))
            {
                BaseAddress = new Uri("http://localhost/")
            });

            var exitCode = await sut.RunAsync("CodeMeridian", "http://localhost", null, "text");

            exitCode.Should().Be(0);
            var text = output.ToString();
            text.Should().Contain("TypeScript | J:/repo/web | incremental | full | 15 | 4 | 8 | 2 | 1 | 1 | 0 | 12 | 1");
            text.Should().Contain("unresolved_local:missing=2 (20.0%)");
            text.Should().Contain("external_or_unindexed:type=2 (40.0%)");
            text.Should().Contain("indeterminate:Test=1");
            text.Should().Contain("catalog performance: files=12, load=45ms, heap=123456 bytes");
            text.Should().NotContain("older_reason");
            text.Should().NotContain("secret-value");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_WithJsonFormatReturnsNormalizedRowsOnly()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new RelationshipHealthReportCommand((_, _) => new HttpClient(new StubHandler(Response()))
            {
                BaseAddress = new Uri("http://localhost/")
            });

            var exitCode = await sut.RunAsync("CodeMeridian", "http://localhost", null, "json");

            exitCode.Should().Be(0);
            using var document = JsonDocument.Parse(output.ToString());
            document.RootElement.GetArrayLength().Should().Be(1);
            document.RootElement[0].GetProperty("language").GetString().Should().Be("TypeScript");
            output.ToString().Should().NotContain("secret-value");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    private static HttpResponseMessage Response() =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                data = new
                {
                    nodes = new[]
                    {
                        Node("2026-08-05T09:00:00Z", "older_reason", attempted: 7),
                        Node("2026-08-05T10:00:00Z", "unresolved_local:missing", attempted: 10),
                        ScopeCatalog()
                    }
                }
            })
        };

    private static object Node(string completedAt, string reason, int attempted)
    {
        var callOutcomes = JsonSerializer.Serialize(new
        {
            attempted,
            resolvedLocal = 4,
            externalOrUnindexed = 3,
            unresolvedLocal = 2,
            indeterminate = 1,
            duplicateEdges = 1,
            syntheticEdges = 0,
            reasons = new Dictionary<string, int> { [reason] = 2 },
            failureCountsByFileRole = new Dictionary<string, int>
            {
                ["unresolved_local:Source"] = 2,
                ["indeterminate:Test"] = 1
            }
        });
        var referenceOutcomes = JsonSerializer.Serialize(new
        {
            attempted = 5,
            resolvedLocal = 0,
            externalOrUnindexed = 5,
            unresolvedLocal = 0,
            indeterminate = 0,
            duplicateEdges = 0,
            syntheticEdges = 0,
            reasons = new Dictionary<string, int> { ["external_or_unindexed:type"] = 2 },
            failureCountsByFileRole = new Dictionary<string, int>()
        });
        var properties = new Dictionary<string, string>
        {
            ["externalKind"] = "IndexRun",
            ["language"] = "TypeScript",
            ["resolutionScope"] = "J:/repo/web",
            ["mode"] = "incremental",
            ["usedFullResolutionCatalog"] = "true",
            ["completedAt"] = completedAt,
            ["scannedFileCount"] = "12",
            ["ingestedFileCount"] = "1",
            ["callRelationshipOutcomes"] = callOutcomes,
            ["referenceRelationshipOutcomes"] = referenceOutcomes,
            ["resolutionCatalogFileCount"] = "12",
            ["resolutionCatalogLoadDurationMs"] = "45",
            ["resolutionCatalogHeapUsedBytes"] = "123456",
            ["apiKey"] = "secret-value"
        };

        return new
        {
            id = $"run-{completedAt}",
            properties = properties.Select(item => new { key = item.Key, value = item.Value }).ToArray()
        };
    }

    private static object ScopeCatalog()
    {
        var properties = new Dictionary<string, string>
        {
            ["externalKind"] = "IndexRun",
            ["indexRunKind"] = "IndexScopeCatalog",
            ["language"] = "TypeScript",
            ["resolutionScopes"] = "[]"
        };
        return new
        {
            id = "scope-catalog",
            properties = properties.Select(item => new { key = item.Key, value = item.Value }).ToArray()
        };
    }

    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
