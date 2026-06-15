using CodeMeridian.Core.CodeGraph;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class IndexedFileRoleClassifierFactoryTests
{
    [Fact]
    public void Create_WhenConfigurationPatternsAreOverridden_UsesConfigurationOverride()
    {
        var sut = IndexedFileRoleClassifierFactory.Create(new CodeMeridianFileRolePatternSnapshot(
            Test: null,
            Migration: null,
            Snapshot: null,
            Generated: null,
            BuildArtifact: null,
            Configuration: ["**/*.special.ts"]));

        sut.Classify("src/app.special.ts").Should().Be(IndexedFileRole.Configuration);
        sut.Classify("src/app.config.ts").Should().Be(IndexedFileRole.Source);
    }
}
