using CodeMeridian.Application;
using CodeMeridian.Infrastructure;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// ── Neo4j knowledge graph ────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Application services (no LLM — reasoning lives in GitHub Copilot) ────────
builder.Services.AddApplication();

// ── MCP server — GitHub Copilot connects here via .vscode/mcp.json ───────────
// Copilot discovers and calls these tools automatically during chat.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Give idle sessions a long timeout so VS Code's SSE connection
        // is not cleaned up between chat interactions
        options.IdleTimeout = TimeSpan.FromHours(2);
    })
    .WithTools<CodebaseTools>()
    .WithTools<KnowledgeTools>()
    .WithTools<ExtensionTools>();

// ── Kestrel — extend keep-alive so long-lived SSE connections aren't dropped ──
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.KeepAliveTimeout = TimeSpan.FromHours(1);
});

// ── HTTP client — used by ExtensionTools to call registered project agents ───
builder.Services.AddHttpClient("CodeMeridianExtension", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

// REST API — used by the Indexer CLI and Sdk (not Copilot)
app.MapKnowledgeApi();

// MCP SSE endpoint — VS Code connects to http://localhost:5100/sse
app.MapMcp("/sse");

app.Run();
