using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ScopedDocumentPolicyTests
{
    [Theory]
    [InlineData("docs", "ready")]
    [InlineData("docs/design", "ready")]
    [InlineData(".", "unknown")]
    [InlineData("other", "not_indexed")]
    [InlineData("Docs", "not_indexed")]
    [InlineData("docs-archive", "not_indexed")]
    public void RootCoverageAuthorizesOnlyConfinedQueries(string root, string expected)
        => ContextWorkflowProfilePolicy.EvaluateDocumentSearch(Profile(), new(root)).Status.Should().Be(expected);

    [Fact]
    public void ATargetHintDoesNotNarrowAnUnfilteredQuery()
    {
        var profile = Profile() with { Target = new("docs/a.md", "observed", "markdown") };
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile).Status.Should().Be("unknown");
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile, new(targetPath: "docs/a.md")).Status.Should().Be("ready");
    }

    [Theory]
    [InlineData("other", "ready")]
    [InlineData("docs-archive", "ready")]
    [InlineData("docs/private", "failed")]
    [InlineData("docs", "failed")]
    [InlineData(".", "failed")]
    public void OnlyOverlappingFailuresBlockTheRequestedRoot(string failedScope, string expected)
    {
        var profile = Profile(".");
        profile = profile with { AnalyzerOutcomes = [.. profile.AnalyzerOutcomes, Outcome(failedScope) with { State = "failed", Coverage = "unknown" }] };
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile, new("docs")).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("delta", "unknown")]
    [InlineData("partial", "partial")]
    [InlineData("running", "not_indexed")]
    [InlineData("source", "stale")]
    [InlineData("fingerprint", "stale")]
    public void ScopedQueriesStillRequireFullCurrentBoundEvidence(string limitation, string expected)
    {
        var outcome = Outcome("docs");
        outcome = limitation switch
        {
            "delta" => outcome with { Mode = "delta", Coverage = "unknown" },
            "partial" => outcome with { State = "partial", Coverage = "partial" },
            "running" => outcome with { State = "running", Coverage = "unknown" },
            "source" => outcome with { SourceFingerprint = "sha256:" + new string('b', 64) },
            _ => outcome with { Fingerprint = new("sha256:" + new string('b', 64), "sha256:" + new string('b', 64)) }
        };
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(Profile() with { AnalyzerOutcomes = [outcome] }, new("docs"))
            .Status.Should().Be(expected);
    }

    [Fact]
    public void ExactFileScopeIgnoresSiblingFailuresButRequiresTheObservedTarget()
    {
        var profile = Profile() with { Target = new("docs/a.md", "observed", "markdown"),
            AnalyzerOutcomes = [Outcome("docs"), Outcome("docs/b.md") with { State = "failed", Coverage = "unknown" }] };
        var scope = new DocumentQueryScope(targetPath: "docs/a.md");
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile, scope).Status.Should().Be("ready");
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile with { Target = null }, scope).Status.Should().Be("unknown");
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile with { Target = profile.Target with { Kind = "csharp" } }, scope)
            .Status.Should().Be("not_applicable");
    }

    internal static ProjectProfileResult Profile(string scope = "docs") => new("available", "Docs", 1, 1, DateTimeOffset.UtcNow,
        new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["docs/a.md"])], [], 0,
            [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0)
            { SourceFingerprint = "sha256:" + new string('a', 64) }, null, []) { AnalyzerOutcomes = [Outcome(scope)] };

    internal static ProjectAnalyzerOutcome Outcome(string scope) => new("documents", scope, "succeeded", "complete", "full",
        DateTimeOffset.UtcNow, "cli/test", []) { SourceFingerprint = "sha256:" + new string('a', 64) };
}
