using CodeMeridian.Api.Endpoints;
using CodeMeridian.Application;
using CodeMeridian.Infrastructure;
using Microsoft.SemanticKernel;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ── Infrastructure (Neo4j) ────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Application (Orchestration + Sub-agents) ─────────────────────────────────
builder.Services.AddApplication();

// ── Semantic Kernel (LLM + Embeddings) ───────────────────────────────────────
var llmSection = builder.Configuration.GetSection("LLM");
var provider = llmSection["Provider"] ?? "openai";
var apiKey = llmSection["ApiKey"] ?? throw new InvalidOperationException("LLM:ApiKey is required.");
var modelId = llmSection["ModelId"] ?? "gpt-4o";
var embeddingModelId = llmSection["EmbeddingModelId"] ?? "text-embedding-3-small";

var kernelBuilder = builder.Services.AddKernel();

if (provider.Equals("azureopenai", StringComparison.OrdinalIgnoreCase))
{
    var endpoint = llmSection["Endpoint"] ?? throw new InvalidOperationException("LLM:Endpoint is required for Azure OpenAI.");
    kernelBuilder
        .AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey)
        .AddAzureOpenAITextEmbeddingGeneration(embeddingModelId, endpoint, apiKey);
}
else
{
    kernelBuilder
        .AddOpenAIChatCompletion(modelId, apiKey)
        .AddOpenAITextEmbeddingGeneration(embeddingModelId, apiKey);
}

// ── HTTP Client (used by extension proxies) ───────────────────────────────────
builder.Services.AddHttpClient("CodeMeridianExtension", client =>
{
    client.Timeout = TimeSpan.FromSeconds(60);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

var publicApiKey = builder.Configuration["CodeMeridian_Auth_ApiKey"]
    ?? builder.Configuration["CodeMeridian:Auth:ApiKey"];

if (!string.IsNullOrWhiteSpace(publicApiKey))
{
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await next(context);
            return;
        }

        if (!IsAuthorized(context.Request, publicApiKey))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Missing or invalid CodeMeridian API key.");
            return;
        }

        await next(context);
    });
}

app.MapOpenApi();
app.MapHealthChecks("/health");

app.MapAgentEndpoints();
app.MapExtensionEndpoints();
app.MapKnowledgeEndpoints();

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
