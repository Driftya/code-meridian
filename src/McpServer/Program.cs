using CodeMeridian.Application;
using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Application.Services;
using CodeMeridian.Infrastructure;
using CodeMeridian.McpServer;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Apps;
using CodeMeridian.McpServer.Configuration;
using CodeMeridian.McpServer.GraphQl;
using CodeMeridian.McpServer.Keywording;
using CodeMeridian.McpServer.Tasks;
using CodeMeridian.McpServer.Tools;
using CodeMeridian.Tooling.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Extensions.Tasks;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

const string ApiKeyScheme = "CodeMeridianApiKey";
var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("meridian.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(".meridian/keyword-classification.json", optional: true, reloadOnChange: true);
builder.Configuration.AddJsonFile(".meridian/database-tracing.json", optional: true, reloadOnChange: true);
builder.Logging.AddJsonConsole();

// ── Neo4j knowledge graph ────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

// ── Application services (no LLM — reasoning lives in GitHub Copilot) ────────
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddSingleton<CodeMeridianConfigFileStore>();
builder.Services.AddSingleton<IGlobalAnalysisConfigurationSource, GlobalMeridianAnalysisConfigurationSource>();
builder.Services.Configure<KeywordRefreshQueueOptions>(builder.Configuration.GetSection("KeywordRefreshQueue"));
builder.Services.AddSingleton<BackgroundKeywordRefreshQueue>();
builder.Services.AddSingleton<IKeywordRefreshQueue>(sp => sp.GetRequiredService<BackgroundKeywordRefreshQueue>());
builder.Services.AddHostedService<KeywordRefreshWorker>();
builder.Services.AddSingleton<HumanCognitiveSeedChallengeStore>();

// ── MCP server — GitHub Copilot connects here via .vscode/mcp.json ───────────
// Copilot discovers and calls these tools automatically during chat.
var mcpServerBuilder = builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithCodeMeridianFilters();

var mcpTaskOptions = builder.Configuration
    .GetSection(McpTaskRuntimeOptions.SectionName)
    .Get<McpTaskRuntimeOptions>() ?? new McpTaskRuntimeOptions();
builder.Services.AddSingleton(mcpTaskOptions);

if (mcpTaskOptions.Enabled)
{
    var taskStore = new CodeMeridianMcpTaskStore(mcpTaskOptions);
    builder.Services.AddSingleton(taskStore);
    builder.Services.AddSingleton<IMcpTaskStore>(taskStore);
    mcpServerBuilder.WithTasks(
        taskStore,
        options => options.ExecutionModeSelector = request =>
            request.MatchedPrimitive?.Id is "rebuild_keyword_graph" or "classify_keywords"
                ? McpTaskExecutionMode.Optional
                : McpTaskExecutionMode.Synchronous);
}

mcpServerBuilder
    .WithTools<CodebaseTools>()
    .WithTools<KeywordTools>()
    .WithTools<KnowledgeTools>()
    .WithTools<HumanCognitiveSeedTools>()
    .WithTools<HumanCognitiveSeedChallengeTools>()
    .WithTools<ClientExtensionTools>()
    .WithTools<ExtensionTools>();

if (builder.Configuration.GetValue<bool>("Mcp:Apps:Enabled"))
{
#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
    mcpServerBuilder
        .WithResources<ClientExtensionAppResources>()
        .WithResources<ConnectionAppResources>()
        .WithResources<HumanCognitiveSeedChallengeAppResources>()
        .WithMcpApps();
#pragma warning restore MCPEXP003
}

// ── GraphQL transport - shared bounded read surface for client extensions ───
builder.Services
    .AddGraphQLServer()
    .AddQueryType<GraphQueryType>()
    .AddTypeExtension<GraphNodeTypeExtensions>()
    .AddAuthorization()
    .ModifyParserOptions(options =>
    {
        options.MaxAllowedFields = GraphQlReadContract.MaxAllowedFields;
        options.MaxAllowedRecursionDepth = GraphQlReadContract.MaxAllowedRecursionDepth;
    })
    .ModifyRequestOptions(options =>
    {
        options.ExecutionTimeout = GraphQlReadContract.ExecutionTimeout;
    });

var healthChecks = builder.Services.AddHealthChecks();
if (mcpTaskOptions.Enabled)
    healthChecks.AddCheck<CodeMeridianMcpTaskStoreHealthCheck>("mcp_tasks");
builder.Services.AddOpenApi();
builder.Services
    .AddAuthentication(ApiKeyScheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyScheme, options => { });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder(ApiKeyScheme)
        .RequireAuthenticatedUser()
    .Build();
});

var app = builder.Build();

var apiKey = builder.Configuration["CodeMeridian_Auth_ApiKey"]
    ?? builder.Configuration["CodeMeridian:Auth:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    throw new InvalidOperationException(
        "CodeMeridian_Auth_ApiKey is not configured. Set CodeMeridian_Auth_ApiKey or CodeMeridian:Auth:ApiKey before starting the MCP server.");
}

app.UseSwaggerUI(options =>
{
    options.DocumentTitle = "CodeMeridian API";
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/openapi/v1.json", "CodeMeridian API v1");
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapOpenApi().AllowAnonymous();

// REST API — used by the Indexer CLI and Sdk (not Copilot)
app.MapKnowledgeApi();
app.MapEmbeddingApi();
app.MapStatusApi();
app.MapGraphQL(GraphQlReadContract.EndpointPath).AllowAnonymous();

// MCP Streamable HTTP endpoint. The historical /sse path is retained for client configuration compatibility.
app.MapMcp("/sse");

app.Run();

sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var expectedApiKey = _configuration["CodeMeridian_Auth_ApiKey"]
            ?? _configuration["CodeMeridian:Auth:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("CodeMeridian API key is not configured."));

        var providedApiKey = Request.Headers.Authorization.ToString();

        if (providedApiKey.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            providedApiKey = providedApiKey["Bearer ".Length..].Trim();
        else
            providedApiKey = Request.Headers["X-CodeMeridian-ApiKey"].ToString();

        if (!FixedTimeEquals(providedApiKey, expectedApiKey))
            return Task.FromResult(AuthenticateResult.Fail("Missing or invalid CodeMeridian API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "CodeMeridian API key")],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsync("Missing or invalid CodeMeridian API key.");
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        if (string.IsNullOrEmpty(provided))
            return false;

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}

public partial class Program
{
}

