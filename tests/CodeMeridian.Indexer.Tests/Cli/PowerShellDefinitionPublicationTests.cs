using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed partial class PowerShellPublicationTests
{
    [Theory]
    [InlineData("complete", "ok")]
    [InlineData("partial", "partial")]
    [InlineData("unbound", "stale")]
    [InlineData("worker", "stale")]
    [InlineData("missing", "not_indexed")]
    [InlineData("delta", "unknown")]
    [InlineData("removed", "ok")]
    [InlineData("stale_node", "ok")]
    [InlineData("foreign", "ok")]
    [InlineData("wrong_path", "ok")]
    [InlineData("wrong_kind", "ok")]
    [InlineData("wrong_owner", "ok")]
    [InlineData("wrong_language", "ok")]
    [InlineData("wrong_id", "ok")]
    [InlineData("profile_worker_missing", "unknown")]
    [InlineData("wrong_generation", "failed")]
    public async Task NativeDefinitionLookupRequiresPublishedVerifiedFullCoverage(string evidence, string expected)
    {
        Write("main.ps1", "function Work {}; Work");
        (await Run()).Should().Be(0);
        var method = graphs.Single().Files.Single().Nodes.Single(node => node.Name == "Work");
        method = method with { Properties = new(method.Properties)
            { ["analysisStatus"] = "complete", ["powershellGeneration"] = generation.ToString(System.Globalization.CultureInfo.InvariantCulture) } };
        if (evidence == "wrong_generation") method.Properties["powershellGeneration"] = "0";
        if (evidence is "removed" or "stale_node") method.Properties["analysisStatus"] = evidence == "removed" ? "removed" : "stale";
        if (evidence == "foreign") method = method with { ProjectContext = "Other" };
        if (evidence == "wrong_path") method = method with { FilePath = "other/main.ps1" };
        if (evidence == "wrong_kind") method = method with { Type = CodeNodeType.DatabaseFunction };
        if (evidence == "wrong_owner") method.Properties["analyzer"] = "other";
        if (evidence == "wrong_language") method.Properties["language"] = "csharp";
        if (evidence == "wrong_id") method = method with { Id = "Other::PowerShellBody::foreign" };
        var terminal = outcomes.Last().Outcomes.Single();
        terminal.Coverage.Should().Be("complete");
        var hash = "sha256:" + new string('b', 64);
        terminal = evidence switch
        {
            "partial" => terminal with { State = "partial", Coverage = "partial" },
            "unbound" => terminal with { SourceFingerprint = null },
            "worker" => terminal with { Fingerprint = new(hash, hash) },
            "delta" => terminal with { Mode = "delta" },
            "profile_worker_missing" => terminal with { Fingerprint = null },
            _ => terminal
        };
        var profiles = Substitute.For<IProjectProfileRepository>();
        var publishedProfile = profile!.Profile;
        if (evidence == "profile_worker_missing") publishedProfile = publishedProfile with
        { Analyzers = publishedProfile.Analyzers.Select(analyzer => analyzer.Id == "powershell" ? analyzer with { Fingerprint = null } : analyzer).ToArray() };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new StoredProjectProfile(
            generation, generation, DateTimeOffset.UtcNow, publishedProfile, new("main.ps1", "powershell"))
            { AnalyzerOutcomes = evidence == "missing" ? [] : [terminal] });
        var graph = Substitute.For<ICodeGraphRepository>();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([method]);
        var service = new CodebaseQueryService(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(), Substitute.For<IProjectAnalysisOptionsResolver>(), new ProjectProfileService(profiles));

        var result = await service.ResolveExactSymbolAsync("Work", "main.ps1", projectContext: "Fixture");

        result.Should().Contain("status: " + expected);
        if (expected == "ok" && evidence == "complete") result.Should().Contain(method.Id).And.Contain("definition lookup only");
        else if (expected == "ok") result.Should().NotContain(method.Id).And.Contain("No exact symbol candidates");
        else
        {
            result.Should().NotContain(method.Id);
            if (evidence != "wrong_generation") await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
        }
        result.Should().NotContain("function Work {}");
    }
}
