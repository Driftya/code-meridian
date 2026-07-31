using CodeMeridian.Evolution.Application;
using CodeMeridian.Evolution.Infrastructure;
using CodeMeridian.Evolution.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddEvolutionApplication();
builder.Services.AddEvolutionInfrastructure(builder.Configuration);
builder.Services.Configure<EvolutionWorkerOptions>(
    builder.Configuration.GetSection("Evolution:Worker"));
builder.Services.AddHostedService<CognitiveWorker>();

var host = builder.Build();
await host.RunAsync();
