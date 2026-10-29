namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed record ProducerScopeSelection(string Scope, DirectoryInfo Directory, IReadOnlyList<string> Paths);
