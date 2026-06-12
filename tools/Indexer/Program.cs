using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Composition;
using Microsoft.Extensions.DependencyInjection;

if (args.Length == 1 && (string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "-v", StringComparison.OrdinalIgnoreCase)))
    args = ["version"];

var services = new ServiceCollection();
services.AddIndexerCli();
await using var provider = services.BuildServiceProvider();

var rootCommand = provider.GetRequiredService<RootCommandFactory>().Create();
var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
