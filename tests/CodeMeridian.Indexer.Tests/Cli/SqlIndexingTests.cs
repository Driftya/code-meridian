using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class SqlIndexingTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "CodeMeridian.Sql", Guid.NewGuid().ToString("N")));

    [Fact]
    public void SelectFiles_UsesSqlExtensionsAndExistingExclusions()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "query.SQL"), "SELECT 1;");
        Directory.CreateDirectory(Path.Combine(_root.FullName, "bin"));
        File.WriteAllText(Path.Combine(_root.FullName, "bin", "ignored.sql"), "SELECT 1;");
        File.WriteAllText(Path.Combine(_root.FullName, "other.ts"), "");
        SqlIndexRunCoordinator.SelectFiles(_root).Select(f => f.Name).Should().Equal("query.SQL");
        IndexExecutionPlanBuilder.EnumerateIndexableFiles(_root, false, false, false, includeSql: true)
            .Select(f => f.Name).Should().Equal("query.SQL");
        IndexExecutionPlanBuilder.EnumerateIndexableFiles(_root, false, false, false).Should().BeEmpty();
    }

    [Fact]
    public void SqlIsOptInAndIndependentOfSkippingTypeScript()
    {
        var settings = new ResolvedIndexerSettings
        {
            RootPath = _root, Project = "SQL", CodeMeridianUrl = "http://fixture", StorageMode = Tooling.Storage.IndexerStorageMode.Repository,
            SkipTypeScript = true, Sql = new CodeMeridianSqlOptions { Enabled = true }
        };
        SqlIndexRunCoordinator.IsEnabled(settings).Should().BeTrue();
        SqlIndexRunCoordinator.IsEnabled(new ResolvedIndexerSettings
        {
            RootPath = _root, Project = "SQL", CodeMeridianUrl = "http://fixture", StorageMode = Tooling.Storage.IndexerStorageMode.Repository,
            SkipSql = true, Sql = settings.Sql
        }).Should().BeFalse();
    }

    [Fact]
    public void LoadConfiguration_PreservesSqlScopeAndSourceRules()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "meridian.json"), """
            {"version":2,"project":"SQL","indexing":{"sql":{"enabled":true,"defaultDialect":"postgresql",
              "databaseScope":"main","searchPath":["app"],"sources":[{"pattern":"analytics/**/*.sql","dialect":"postgresql","databaseScope":"analytics"}]}}}
            """);
        var config = new CodeMeridianConfigFileStore().LoadLocal(_root)!;
        config.Sql!.Enabled.Should().BeTrue();
        config.Sql.SearchPath.Should().Equal("app");
        config.Sql.Sources!.Single().DatabaseScope.Should().Be("analytics");
    }

    public void Dispose() => _root.Delete(recursive: true);
}
