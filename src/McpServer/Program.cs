using CodeMeridian.Application;
using CodeMeridian.Infrastructure;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Tools;
using Microsoft.Extensions.Logging.Console;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddJsonConsole();

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

var apiKey = builder.Configuration["CodeMeridian_Auth_ApiKey"]
    ?? builder.Configuration["CodeMeridian:Auth:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "CodeMeridian_Auth_ApiKey is not configured. Set CodeMeridian_Auth_ApiKey or CodeMeridian:Auth:ApiKey before starting the MCP server.");
}

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next(context);
        return;
    }

    if (!IsAuthorized(context.Request, apiKey))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Missing or invalid CodeMeridian API key.");
        return;
    }

    await next(context);
});

app.MapHealthChecks("/health");

// REST API — used by the Indexer CLI and Sdk (not Copilot)
app.MapKnowledgeApi();
app.MapEmbeddingApi();
app.MapStatusApi();

// MCP SSE endpoint — VS Code connects to http://localhost:5100/sse
app.MapMcp("/sse");

app.Run();

static bool IsAuthorized(HttpRequest request, string expectedApiKey)
{
    var providedApiKey = request.Headers.Authorization.ToString();

    if (providedApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        providedApiKey = providedApiKey["Bearer ".Length..].Trim();
    else
        providedApiKey = request.Headers["X-CodeMeridian-ApiKey"].ToString();

    return FixedTimeEquals(providedApiKey, expectedApiKey);
}

static bool FixedTimeEquals(string provided, string expected)
{
    if (string.IsNullOrEmpty(provided))
        return false;

    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);

    return providedBytes.Length == expectedBytes.Length
        && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}
