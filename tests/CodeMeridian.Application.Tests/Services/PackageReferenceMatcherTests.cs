using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class PackageReferenceMatcherTests
{
    private const string Assembly = "Shared, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private static PackageBuildScope Scope(string id = "scope") => new(id, "Shared.csproj", "net10.0", "Debug", "Shared", Assembly,
        "Company.Shared", "1.0.0", "https://example.com/shared", "commit", false, "msbuild.workspace");
    private static PendingPackageReference Pending() => new(new("reference", "consumer", "caller", "csharp.symbol.v1:M:Shared.Run(System.String)|None",
        "Shared", Assembly, "Company.Shared", "1.0.0", "https://example.com/shared", "commit", "Calls", "App.cs", 2, 1, 2, 5),
        Scope("consumer"), "App", 1);
    private static PackageExportCandidate Export(string project = "Library") => new(new("export", "scope", "target",
        Pending().Reference.SymbolKey, "Shared.cs"), Scope(), project);

    [Fact]
    public void Match_VerifiedRevision_ProducesVerifiedSource()
    {
        new PackageReferenceMatcher().Match(Pending(), [Export()]).Status.Should().Be("verified_source");
    }

    [Theory]
    [InlineData("2.0.0", "commit", false)]
    [InlineData("1.0.0", "other-commit", false)]
    [InlineData("1.0.0", "commit", true)]
    public void Match_VersionRevisionOrDirtyDifference_RemainsCurrentSource(string version, string commit, bool dirty)
    {
        var export = Export() with { Scope = Scope() with { PackageVersion = version, Commit = commit, Dirty = dirty } };
        var result = new PackageReferenceMatcher().Match(Pending(), [export]);
        result.Status.Should().Be("associated_current_source");
        result.ProducerVersion.Should().Be(version);
    }

    [Fact]
    public void Match_MultipleProducers_DoesNotChooseFirst()
    {
        new PackageReferenceMatcher().Match(Pending(), [Export(), Export("OtherLibrary")]).Status.Should().Be("ambiguous");
    }

    [Fact]
    public void Match_ProducerBindingSelectsOwnerWithoutPromotingRevision()
    {
        var pending = Pending() with { Reference = Pending().Reference with { ProducerProject = "Library", RepositoryCommit = null } };
        var result = new PackageReferenceMatcher().Match(pending, [Export(), Export("OtherLibrary")]);
        result.Status.Should().Be("associated_current_source");
        result.ProducerProject.Should().Be("Library");
    }

    [Fact]
    public void Match_NameCollisionDifferentPublicKeyOrOverload_IsNotLinked()
    {
        var wrongOwner = Export() with { Scope = Scope() with { AssemblyIdentity = Assembly.Replace("null", "abcdef") } };
        var wrongSymbol = Export() with { Export = Export().Export with { SymbolKey = "csharp.symbol.v1:M:Shared.Run(System.Int32)|None" } };
        new PackageReferenceMatcher().Match(Pending(), [wrongOwner, wrongSymbol]).Status.Should().Be("source_not_indexed");
    }

    [Fact]
    public void Match_CollidingSourceMapping_IsAmbiguous()
    {
        var export = Export() with { Export = Export().Export with { AmbiguousSource = true } };
        new PackageReferenceMatcher().Match(Pending(), [export]).Status.Should().Be("ambiguous");
    }

    [Fact]
    public void RepositoryIdentity_RemovesCredentialsAndOnlyTrailingGitSuffix()
    {
        PackageRepositoryIdentity.Normalize("https://user:secret@example.com/my.git.project.git?token=secret")
            .Should().Be("https://example.com/my.git.project");
    }
}
