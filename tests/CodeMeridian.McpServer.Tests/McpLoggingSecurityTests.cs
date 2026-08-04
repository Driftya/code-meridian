using System.Collections.Concurrent;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpLoggingSecurityTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpLoggingSecurityTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ToolCallLogs_ExcludeCredentialsAndRawArguments()
    {
        const string sensitiveQuery = "SENTINEL-RAW-QUERY";
        const string sensitiveProject = "SENTINEL-PROJECT";
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.QueryStructureAsync(
                sensitiveQuery,
                sensitiveProject,
                Arg.Any<CancellationToken>())
            .Returns("safe result");
        using var logProvider = new CapturingLoggerProvider();
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(logProvider);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodebaseQueryService>();
                services.AddSingleton(queryService);
            });
        });
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.CallToolAsync(
            "query_codebase",
            new Dictionary<string, object?>
            {
                ["query"] = sensitiveQuery,
                ["projectContext"] = sensitiveProject
            });

        result.Content.OfType<TextContentBlock>().Should().ContainSingle();
        var logs = string.Join(Environment.NewLine, logProvider.Messages);
        logs.Should().NotContain(sensitiveQuery);
        logs.Should().NotContain(sensitiveProject);
        logs.Should().NotContain(GraphQlWebApplicationFactory.ApiKey);
        logs.Should().NotContain("Authorization: Bearer");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception));
    }
}
