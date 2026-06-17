using CodeMeridian.Application;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Tests.Services;

public sealed class KeywordClassificationOptionsTests
{
    [Fact]
    public void AddApplication_LoadsKeywordClassificationFromMeridianFile()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteFile(
            ".meridian/keyword-classification.json",
            """
            {
              "KeywordClassification": {
                "Enabled": true,
                "CommonDocumentFrequencyRatio": 0.2,
                "ClassificationVersion": 7,
                "NoiseTerms": [ "alpha", "beta" ],
                "TechnicalTerms": [ "checksum", "payload" ],
                "ToolingTerms": [ "indexer", "rebuild" ],
                "ArchitectureTerms": [ "service", "module" ],
                "DiagnosticTerms": [ "warning", "verify" ],
                "DomainTerms": [ "knowledge", "graph" ]
              }
            }
            """);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(workspace.Root.FullName, ".meridian", "keyword-classification.json"), optional: false, reloadOnChange: false)
            .Build();

        var services = new ServiceCollection();
        services.AddApplication(configuration);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<KeywordClassificationOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.CommonDocumentFrequencyRatio.Should().Be(0.2d);
        options.ClassificationVersion.Should().Be(7);
        options.NoiseTerms.Should().Contain("alpha");
        options.NoiseTerms.Should().Contain("beta");
        options.ToolingTerms.Should().Contain("rebuild");
        options.DomainTerms.Should().Contain("graph");
    }

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-keyword-classification-{Guid.NewGuid():N}"));
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
