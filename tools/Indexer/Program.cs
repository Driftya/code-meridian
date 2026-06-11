using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Composition;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddIndexerCli();
await using var provider = services.BuildServiceProvider();

var rootCommand = provider.GetRequiredService<RootCommandFactory>().Create();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
