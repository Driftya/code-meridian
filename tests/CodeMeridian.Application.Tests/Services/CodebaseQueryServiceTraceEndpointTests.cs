using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceTraceEndpointTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task TraceEndpointAsync_WhenNoPaths_ReturnsReindexHint()
    {
        var (sut, graph) = Build();
        graph.FindEndpointTracesAsync("POST /api/orders", "Shop.Api", 10, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.TraceEndpointAsync("POST /api/orders", "Shop.Api");

        result.Should().Contain("No database or event trace found");
        result.Should().Contain(".meridian/database-tracing.json");
    }

    [Fact]
    public async Task TraceEndpointAsync_WithDatabaseAndEventPaths_GroupsResults()
    {
        var (sut, graph) = Build();
        var endpoint = Node("endpoint", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop.Api");
        var handler = Node("handler", "CreateOrder", CodeNodeType.Method, "src/Api/OrdersEndpoint.cs", 22, "Shop.Api");
        var operation = new CodeNode
        {
            Id = "db-op",
            Name = "EFCore Writes Orders",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Api",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = "EFCore"
            }
        };
        var table = Node("table", "Orders", CodeNodeType.DatabaseTable, project: "Shop.Api");
        var topic = Node("topic", "order-created", CodeNodeType.MessageTopic, project: "Shop.Api");

        graph.FindEndpointTracesAsync("POST /api/orders", "Shop.Api", 10, Arg.Any<CancellationToken>())
            .Returns([
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(handler, "Writes", null),
                    new GraphPathStep(operation, "Writes", null),
                    new GraphPathStep(table, null, null)
                ]),
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(handler, "PublishesTo", null),
                    new GraphPathStep(topic, null, null)
                ])
            ]);

        var result = await sut.TraceEndpointAsync("POST /api/orders", "Shop.Api");

        result.Should().Contain("## Endpoint Trace - `POST /api/orders` - Shop.Api");
        result.Should().Contain("### Database paths (1)");
        result.Should().Contain("### Event paths (1)");
        result.Should().Contain("EFCore Writes Orders");
        result.Should().Contain("**DatabaseTable** `Orders`");
        result.Should().Contain("**MessageTopic** `order-created`");
        result.Should().Contain("Graph-only trace");
    }

    [Fact]
    public async Task TraceEndpointAsync_Summary_IgnoresSingleNodePathsAndCountsTerminalGroups()
    {
        var (sut, graph) = Build();
        var endpoint = Node("endpoint", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop.Api");
        var handler = Node("handler", "CreateOrder", CodeNodeType.Method, "src/Api/OrdersEndpoint.cs", 22, "Shop.Api");
        var table = Node("table", "Orders", CodeNodeType.DatabaseTable, project: "Shop.Api");
        var topic = Node("topic", "order-created", CodeNodeType.MessageTopic, project: "Shop.Api");

        graph.FindEndpointTracesAsync("POST /api/orders", "Shop.Api", 10, Arg.Any<CancellationToken>())
            .Returns([
                new EndpointTracePath([
                    new GraphPathStep(endpoint, null, null)
                ]),
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(handler, "Writes", null),
                    new GraphPathStep(table, null, null)
                ]),
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(topic, null, null)
                ])
            ]);

        var result = await sut.TraceEndpointAsync("POST /api/orders", "Shop.Api", ContextDetailLevel.Summary);

        result.Should().Be("Endpoint trace summary for `POST /api/orders`: 1 database path(s), 1 event path(s), 2 total graph path(s).");
    }


}

