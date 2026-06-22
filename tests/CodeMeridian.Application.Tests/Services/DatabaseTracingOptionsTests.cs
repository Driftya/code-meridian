using CodeMeridian.Application;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Tests.Services;

public sealed class DatabaseTracingOptionsTests
{
    [Fact]
    public void AddApplication_LoadsDatabaseTracingFromMeridianFile()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".meridian/database-tracing.json",
            """
            {
              "DatabaseTracing": {
                "Enabled": true,
                "ConfigurationVersion": 7,
                "MaxTablesPerOperation": 2,
                "Presets": [
                  {
                    "Id": "custom-sql",
                    "Strategy": "RawSql",
                    "Provider": "CustomSql",
                    "Enabled": true,
                    "ReadMethods": [ "RunReader" ],
                    "WriteMethods": [ "RunWriter" ],
                    "SqlArgumentIndexes": [ 1 ],
                    "ReceiverTextHints": [ "customCommand" ],
                    "CommandCreationTypeHints": [ "CustomCommand" ],
                    "CommandTextProperties": [ "Sql" ],
                    "TableSources": [ "SqlText" ]
                  }
                ]
              }
            }
            """);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(workspace.Root.FullName, ".meridian", "database-tracing.json"), optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddApplication(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<DatabaseTracingOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.ConfigurationVersion.Should().Be(7);
        options.MaxTablesPerOperation.Should().Be(2);
        options.Presets.Should().Contain(preset => preset.Id == "custom-sql" && preset.Provider == "CustomSql");
        var customPreset = options.Presets.Single(preset => preset.Id == "custom-sql");
        customPreset.SqlArgumentIndexes.Should().Contain(1);
        customPreset.CommandTextProperties.Should().Contain("Sql");
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-database-tracing-{Guid.NewGuid():N}"));
        }

        public DirectoryInfo Root { get; }

        public FileInfo WriteFile(string relativePath, string content)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);
            return file;
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
