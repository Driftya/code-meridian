using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ProjectAnalysisOptionsResolverTests
{
    [Fact]
    public async Task ResolveAsync_ProjectEntriesOverrideGlobalEntries()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var globalSource = Substitute.For<IGlobalAnalysisConfigurationSource>();
        globalSource.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new AnalysisConfigurationSourceResult(
                [
                    new AnalysisConfigurationEntry("analysis:ranking:productionOnlyByDefault", "false"),
                    new AnalysisConfigurationEntry("analysis:ranking:includeSuppressedNoise", "true")
                ],
                [])));
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                ConfigEntry("analysis:ranking:productionOnlyByDefault", "true")
            ]);

        var sut = new ProjectAnalysisOptionsResolver(
            graph,
            globalSource,
            Options.Create(new CodebaseAnalysisOptions()),
            NullLogger<ProjectAnalysisOptionsResolver>.Instance);

        var result = await sut.ResolveAsync("Shop");

        result.Options.Ranking.ProductionOnlyByDefault.Should().BeTrue();
        result.Options.Ranking.IncludeSuppressedNoise.Should().BeTrue();
        result.Metadata.UsedGlobalConfig.Should().BeTrue();
        result.Metadata.UsedProjectConfig.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_InvalidEntryDoesNotDiscardSiblingOverride()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var globalSource = Substitute.For<IGlobalAnalysisConfigurationSource>();
        globalSource.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new AnalysisConfigurationSourceResult([], [])));
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                ConfigEntry("analysis:ranking:productionOnlyByDefault", "false"),
                ConfigEntry("analysis:ranking:includeSuppressedNoise", "not-a-bool")
            ]);

        var sut = new ProjectAnalysisOptionsResolver(
            graph,
            globalSource,
            Options.Create(new CodebaseAnalysisOptions()),
            NullLogger<ProjectAnalysisOptionsResolver>.Instance);

        var result = await sut.ResolveAsync("Shop");

        result.Options.Ranking.ProductionOnlyByDefault.Should().BeFalse();
        result.Options.Ranking.IncludeSuppressedNoise.Should().BeFalse();
        result.Metadata.Warnings.Should().Contain(warning => warning.Contains("includeSuppressedNoise", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResolveAsync_ListOverrideReplacesLowerLayerList()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var globalSource = Substitute.For<IGlobalAnalysisConfigurationSource>();
        globalSource.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new AnalysisConfigurationSourceResult(
                [
                    new AnalysisConfigurationEntry("analysis:ranking:infrastructureNames:0", "GlobalOnly"),
                    new AnalysisConfigurationEntry("analysis:ranking:infrastructureNames:1", "GlobalShared")
                ],
                [])));
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                ConfigEntry("analysis:ranking:infrastructureNames:0", "ProjectOnly")
            ]);

        var sut = new ProjectAnalysisOptionsResolver(
            graph,
            globalSource,
            Options.Create(new CodebaseAnalysisOptions()),
            NullLogger<ProjectAnalysisOptionsResolver>.Instance);

        var result = await sut.ResolveAsync("Shop");

        result.Options.Ranking.InfrastructureNames.Should().Equal("ProjectOnly");
    }

    private static CodeNode ConfigEntry(string canonicalKey, string rawValuePreview) => new()
    {
        Id = canonicalKey,
        Name = canonicalKey,
        Type = CodeNodeType.ConfigurationEntry,
        FilePath = "meridian.json",
        Properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["canonicalKey"] = canonicalKey,
            ["rawValuePreview"] = rawValuePreview
        }
    };
}
