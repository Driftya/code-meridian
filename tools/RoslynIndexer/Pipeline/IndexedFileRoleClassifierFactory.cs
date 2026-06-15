using CodeMeridian.Application.Services;
using CodeMeridian.Tooling.Configuration;
using Microsoft.Extensions.Options;

namespace CodeMeridian.RoslynIndexer.Pipeline;

public static class IndexedFileRoleClassifierFactory
{
    public static IIndexedFileRoleClassifier Create(CodeMeridianFileRolePatternSnapshot? snapshot)
    {
        var options = new CodebaseIndexingOptions();
        Apply(snapshot, options.FileRoles);
        return new ConfiguredIndexedFileRoleClassifier(Options.Create(options));
    }

    private static void Apply(CodeMeridianFileRolePatternSnapshot? snapshot, FileRolePatternOptions target)
    {
        if (snapshot is null)
            return;

        ReplaceIfPresent(target.Test, snapshot.Test);
        ReplaceIfPresent(target.Migration, snapshot.Migration);
        ReplaceIfPresent(target.Snapshot, snapshot.Snapshot);
        ReplaceIfPresent(target.Generated, snapshot.Generated);
        ReplaceIfPresent(target.BuildArtifact, snapshot.BuildArtifact);
        ReplaceIfPresent(target.Configuration, snapshot.Configuration);
    }

    private static void ReplaceIfPresent(List<string> target, IReadOnlyList<string>? source)
    {
        if (source is null)
            return;

        target.Clear();
        target.AddRange(source);
    }
}
