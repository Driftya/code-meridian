using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Tests.Services;

public sealed class FileRolePolicyTests
{
    private static readonly IIndexedFileRoleClassifier Classifier =
        new ConfiguredIndexedFileRoleClassifier(Options.Create(new CodebaseIndexingOptions()));

    [Theory]
    [InlineData("src/MyApp/Services/UserService.cs", IndexedFileRole.Source)]
    [InlineData("tests/MyApp.Tests/UserServiceTests.cs", IndexedFileRole.Test)]
    [InlineData("src/MyApp.Infrastructure/Migrations/20260101000000_CreateUsers.cs", IndexedFileRole.Migration)]
    [InlineData("src/MyApp.Infrastructure/Migrations/MyDbContextModelSnapshot.cs", IndexedFileRole.Snapshot)]
    [InlineData("src/MyApp/Generated/Foo.g.cs", IndexedFileRole.Generated)]
    [InlineData("src/MyApp/obj/Debug/net10.0/Foo.cs", IndexedFileRole.BuildArtifact)]
    [InlineData("src/MyApp/Configuration/Neo4jOptions.cs", IndexedFileRole.Configuration)]
    [InlineData("src/MyApp/Bootstrap/ServiceConfiguration.cs", IndexedFileRole.Configuration)]
    [InlineData("src/MyApp/Bootstrap/AppSettings.cs", IndexedFileRole.Configuration)]
    [InlineData("src/web/app.config.ts", IndexedFileRole.Configuration)]
    [InlineData("src/web/orders.options.ts", IndexedFileRole.Configuration)]
    [InlineData("src/web/AppConfig.tsx", IndexedFileRole.Configuration)]
    public void Classify_DefaultPatterns_ReturnExpectedRole(string path, IndexedFileRole expected)
    {
        Classifier.Classify(path).Should().Be(expected);
    }

    [Fact]
    public void Classify_ConfigOverrides_AreUsed()
    {
        var sut = new ConfiguredIndexedFileRoleClassifier(Options.Create(new CodebaseIndexingOptions
        {
            FileRoles = new FileRolePatternOptions
            {
                Generated = ["**/*.gen.cs"]
            }
        }));

        sut.Classify("src/Generated/Widget.gen.cs").Should().Be(IndexedFileRole.Generated);
        sut.Classify("src/Generated/Widget.g.cs").Should().Be(IndexedFileRole.Source);
    }

    [Fact]
    public void DesignSmells_AllowsOnlySource()
    {
        var sut = new DefaultAnalysisProfilePolicy();

        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Source).Should().BeTrue();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Unknown).Should().BeTrue();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Test).Should().BeFalse();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Migration).Should().BeFalse();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Snapshot).Should().BeFalse();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.Generated).Should().BeFalse();
        sut.Allows(AnalysisProfile.DesignSmells, IndexedFileRole.BuildArtifact).Should().BeFalse();
    }

    [Fact]
    public void TestShield_AllowsSourceTestAndUnknown()
    {
        var sut = new DefaultAnalysisProfilePolicy();

        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.Source).Should().BeTrue();
        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.Test).Should().BeTrue();
        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.Unknown).Should().BeTrue();
        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.Migration).Should().BeFalse();
        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.Generated).Should().BeFalse();
        sut.Allows(AnalysisProfile.TestShield, IndexedFileRole.BuildArtifact).Should().BeFalse();
    }

    [Fact]
    public void CoverageGaps_AllowsSourceTestAndUnknown()
    {
        var sut = new DefaultAnalysisProfilePolicy();

        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.Source).Should().BeTrue();
        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.Test).Should().BeTrue();
        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.Unknown).Should().BeTrue();
        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.Migration).Should().BeFalse();
        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.Generated).Should().BeFalse();
        sut.Allows(AnalysisProfile.CoverageGaps, IndexedFileRole.BuildArtifact).Should().BeFalse();
    }

    [Fact]
    public void FullInventory_AllowsAllRoles()
    {
        var sut = new DefaultAnalysisProfilePolicy();

        foreach (var role in Enum.GetValues<IndexedFileRole>())
            sut.Allows(AnalysisProfile.FullInventory, role).Should().BeTrue();
    }
}
