using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed partial class RootCommandFactory
{
    private Command CreateProfileCommand()
    {
        var command = new Command("profile", "Discover repository contents and analyzer configuration. Server publication requires --publish.");
        var publishOption = new Option<bool>("--publish") { Description = "Publish complete discovery to the server without semantic indexing." };
        var urlOption = new Option<string?>("--url") { Description = "CodeMeridian server URL for publication." };
        var pathArgument = new Argument<string?>("path") { DefaultValueFactory = _ => null, Description = "Root directory to scan. Defaults to the current directory." };
        var projectOption = new Option<string?>("--project") { Description = "Project context name." };
        var formatOption = new Option<string>("--format") { DefaultValueFactory = _ => "text", Description = "Output format: text or json." };
        var skipCSharpOption = new Option<bool>("--skip-csharp") { Description = "Report C# indexing as disabled; retain detected files." };
        var skipTypeScriptOption = new Option<bool>("--skip-typescript") { Description = "Report TypeScript/frontend indexing as disabled; retain detected files." };
        var skipDocsOption = new Option<bool>("--skip-docs") { Description = "Report document indexing as disabled; retain detected files." };
        var skipConfigurationOption = new Option<bool>("--skip-config") { Description = "Report configuration indexing as disabled; retain detected files." };
        var skipSqlOption = new Option<bool>("--skip-sql") { Description = "Report SQL indexing as disabled; retain detected files." };
        command.Add(pathArgument);
        command.Add(publishOption);
        command.Add(urlOption);
        command.Add(projectOption);
        command.Add(formatOption);
        command.Add(skipCSharpOption);
        command.Add(skipTypeScriptOption);
        command.Add(skipDocsOption);
        command.Add(skipConfigurationOption);
        command.Add(skipSqlOption);
        command.SetAction((parseResult, cancellationToken) =>
            services.GetRequiredService<ProjectProfileCommand>().RunAsync(
                parseResult.GetValue(pathArgument), parseResult.GetValue(projectOption), parseResult.GetValue(formatOption)!,
                parseResult.GetValue(skipCSharpOption), parseResult.GetValue(skipTypeScriptOption),
                parseResult.GetValue(skipDocsOption), parseResult.GetValue(skipConfigurationOption),
                parseResult.GetValue(skipSqlOption), parseResult.GetValue(publishOption),
                parseResult.GetValue(urlOption), cancellationToken));
        return command;
    }
}
