using System.Text.Json.Serialization;
using CodeMeridian.Evolution.Api;
using CodeMeridian.Evolution.Application;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<EvolutionExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .AllowAnyHeader()
        .AllowAnyMethod()
        .SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
            (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))));
});
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEvolutionApplication();
builder.Services.AddEvolutionInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseCors();
app.Use(async (context, next) =>
{
    var configuredKey = app.Configuration["Evolution:ApiKey"];
    var requiresKey = !HttpMethods.IsGet(context.Request.Method) &&
                      context.Request.Path.StartsWithSegments("/api");

    if (requiresKey &&
        !string.IsNullOrWhiteSpace(configuredKey) &&
        !string.Equals(
            context.Request.Headers["X-Evolution-Key"],
            configuredKey,
            StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(
            new { error = "A valid X-Evolution-Key header is required." },
            context.RequestAborted);
        return;
    }

    await next(context);
});

app.MapOpenApi();
app.MapEvolutionEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider
        .GetRequiredService<CognitiveLedgerService>()
        .InitializeAsync();
}

app.Run();

public partial class Program;
