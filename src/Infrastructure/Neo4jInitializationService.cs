using CodeMeridian.Infrastructure.Graph;
using CodeMeridian.Infrastructure.Knowledge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.Infrastructure;

/// <summary>
/// Runs once at startup to create Neo4j constraints, indexes, and the vector index.
/// Safe to re-run — all DDL uses IF NOT EXISTS guards.
/// </summary>
internal sealed class Neo4jInitializationService(
    Neo4jCodeGraphRepository codeGraph,
    Neo4jKeywordGraphRepository keywordGraph,
    Neo4jVectorRepository vectorStore,
    ILogger<Neo4jInitializationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await codeGraph.InitializeAsync(stoppingToken);
            await keywordGraph.InitializeAsync(stoppingToken);
            await vectorStore.InitializeAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Neo4j initialization failed. Ensure Neo4j is running and reachable.");
            throw;
        }
    }
}
