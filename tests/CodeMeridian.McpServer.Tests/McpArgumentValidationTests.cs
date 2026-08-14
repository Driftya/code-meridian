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

public sealed class McpArgumentValidationTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public McpArgumentValidationTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData(201)]
    [InlineData(0)]
    public async Task InvalidProjectContext_IsRejectedBeforeApplicationCall(int length)
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        var projectContext = length == 0 ? "bad\u0001context" : new string('x', length);
        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.CallToolAsync(
            "query_codebase",
            new Dictionary<string, object?>
            {
                ["query"] = "find services",
                ["projectContext"] = projectContext
            });

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("An error occurred invoking 'query_codebase'.");
        await queryService.DidNotReceiveWithAnyArgs().QueryStructureAsync(
            default!,
            default,
            default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task OptionalProjectContext_WorksWithoutMirroredHeader(
        string? pinnedProtocolVersion)
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.QueryStructureAsync(
                "find services",
                null,
                Arg.Any<CancellationToken>())
            .Returns("query result");
        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient, pinnedProtocolVersion);

        var result = await client.CallToolAsync(
            "query_codebase",
            new Dictionary<string, object?> { ["query"] = "find services" });

        result.IsError.Should().NotBeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be("query result");
        await queryService.Received(1).QueryStructureAsync(
            "find services",
            null,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("resolve_exact_symbol")]
    [InlineData("find_implementation_surface")]
    public async Task MissingRequiredArgument_ReturnsToolErrorWithoutUnhandledExceptionLog(
        string toolName)
    {
        using var logProvider = new CapturingLoggerProvider();
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(logProvider);
            }));
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.CallToolAsync(
            toolName,
            new Dictionary<string, object?>());

        result.IsError.Should().BeTrue();
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Be(
                $"Invalid arguments for tool '{toolName}': a required argument is missing.");
        logProvider.Entries.Should().NotContain(entry =>
            entry.Level == LogLevel.Error
            && entry.Message.Contains("threw an unhandled exception", StringComparison.Ordinal));
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> WithQueryService(
        ICodebaseQueryService queryService) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodebaseQueryService>();
                services.AddSingleton(queryService);
            }));

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(Entries, categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        ConcurrentQueue<LogEntry> entries,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue(new LogEntry(logLevel, categoryName, formatter(state, exception)));
    }

    private sealed record LogEntry(LogLevel Level, string Category, string Message);
}
