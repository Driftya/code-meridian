using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.Projects;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ToolCapabilityPolicyTests
{
    [Theory]
    [InlineData("succeeded", "complete", "full", "ready")]
    [InlineData("succeeded", "unknown", "full", "unknown")]
    [InlineData("succeeded", "unknown", "delta", "unknown")]
    [InlineData("running", "unknown", "full", "not_indexed")]
    [InlineData("partial", "partial", "full", "partial")]
    [InlineData("failed", "unknown", "full", "failed")]
    public void OutcomeCoverageControlsCodeReadiness(string state, string coverage, string mode, string expected)
    {
        var profile = Profile() with { AnalyzerOutcomes = [Outcome() with { State = state, Coverage = coverage, Mode = mode }] };
        ContextWorkflowToolPolicy.Evaluate("find_impact", profile).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("csharp", "csharp", "ready")]
    [InlineData("typescript", "typescript", "ready")]
    [InlineData("javascript", "typescript", "ready")]
    [InlineData("powershell", "powershell", "unsupported")]
    [InlineData("sql", "sql", "not_applicable")]
    [InlineData("markdown", "documents", "not_applicable")]
    public void ObservedTargetControlsLanguageApplicability(string kind, string analyzer, string expected)
    {
        var profile = Profile(kind, analyzer);
        ContextWorkflowToolPolicy.Evaluate("find_impact", profile).Status.Should().Be(expected);
    }

    [Fact]
    public void MissingPartialAndStaleDiscoveryDoNotProveLanguageAbsence()
    {
        ContextWorkflowToolPolicy.Evaluate("query_codebase", Profile() with { Profile = null }).Status.Should().Be("unknown");
        var profile = Profile();
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { Profile = profile.Profile! with { DiscoveryState = "partial", FileKinds = [] } }).Status.Should().Be("unknown");
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { Status = "stale" }).Status.Should().Be("stale");
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { Status = "failed", Profile = null }).Status.Should().Be("failed");
    }

    [Fact]
    public void DisabledAndUnpublishedAnalyzersRemainDistinct()
    {
        var profile = Profile();
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { Profile = profile.Profile! with
            { Analyzers = [profile.Profile.Analyzers[0] with { ConfigurationState = "disabled" }] } }).Status.Should().Be("disabled");
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { AnalyzerOutcomes = [] }).Status.Should().Be("not_indexed");
    }

    [Fact]
    public void RootEvidenceOnlyAuthorizesQueriesActuallyFilteredToThatTarget()
    {
        var profile = Profile() with { AnalyzerOutcomes = [Outcome() with { Scope = "src" }] };
        ContextWorkflowToolPolicy.Evaluate("resolve_exact_symbol", profile).Status.Should().Be("ready");
        ContextWorkflowToolPolicy.Evaluate("find_impact", profile).Status.Should().Be("unknown");
        ContextWorkflowToolPolicy.Evaluate("query_codebase", profile with { Target = null }).Status.Should().Be("unknown");
    }

    [Fact]
    public void BasicDependenciesDoNotInventTestArchitectureOrSqlEvidence()
    {
        var profile = Profile();
        ContextWorkflowToolPolicy.Evaluate("find_test_shield", profile).Status.Should().Be("unknown");
        ContextWorkflowToolPolicy.Evaluate("find_architecture_violations", profile).Status.Should().Be("unknown");
        ContextWorkflowToolPolicy.Evaluate("find_connection", Profile("sql", "sql")).Status.Should().Be("not_applicable");
    }

    [Fact]
    public void ConfigurationDefinitionsUseTheirOwnAnalyzer()
    {
        var profile = Profile("configuration", "configuration", ["configuration_definitions"]);
        ContextWorkflowToolPolicy.Evaluate("find_config_definitions", profile).Status.Should().Be("ready");
        ContextWorkflowToolPolicy.Evaluate("find_config_usage", profile).Status.Should().NotBe("ready");
    }

    [Fact]
    public void FrontendConnectionsUseDeclaredFrontendRelationships()
    {
        var profile = Profile("css", "frontend", ["frontend_relationships"]);
        ContextWorkflowToolPolicy.Evaluate("find_connection", profile).Status.Should().Be("ready");
        ContextWorkflowToolPolicy.Evaluate("find_frontend_cascade_conflicts", profile).Status.Should().Be("unknown");
    }

    [Fact]
    public void OptionalUnavailableStepsAreExplainedAndRequiredStepsAreNeverTruncated()
    {
        var profile = Profile();
        var planner = new ContextWorkflowPlanner();
        var plan = planner.Plan(new("Find examples", ProjectContext: "Fixture", WorkflowType: "feature_implementation", IncludeOptionalSteps: true), profile with { Target = null });
        plan.OmittedSteps.Should().NotBeEmpty();
        plan.OmittedSteps.Should().OnlyContain(step => !step.Required && step.Applicability!.Status != "ready");
        plan.Warnings.Should().Contain(warning => warning.Contains("Omitted optional"));
        var truncated = planner.Plan(new("Edit", "src/A.cs", "Fixture", "before_edit", MaxSteps: 1), profile);
        truncated.Status.Should().Be("blocked");
        truncated.Error.Should().Contain("maxSteps");
        truncated.Steps.Count(step => step.Required).Should().BeGreaterThan(1);
    }

    [Fact]
    public void FingerprintMismatchAndASecondFailedScopePreventReadyResults()
    {
        var profile = Profile();
        var hash = "sha256:" + new string('a', 64);
        var fingerprint = new ProjectAnalyzerFingerprint(hash, hash);
        var mismatched = profile with { Profile = profile.Profile! with { Analyzers = [profile.Profile.Analyzers[0] with { Fingerprint = fingerprint }] } };
        ContextWorkflowToolPolicy.Evaluate("find_impact", mismatched).Status.Should().Be("stale");
        ContextWorkflowToolPolicy.Evaluate("find_impact", profile with { AnalyzerOutcomes = [Outcome(), Outcome() with { Scope = "other", State = "failed", Coverage = "unknown" }] })
            .Status.Should().Be("failed");
    }

    [Fact]
    public void DiscoveryIsExemptFromItsOwnReadinessGate()
    {
        ContextWorkflowToolPolicy.Evaluate("get_project_profile", Profile() with { Profile = null }).Status.Should().Be("ready");
        ContextWorkflowToolCatalog.All.Should().OnlyContain(tool => tool.ReadinessExempt || tool.RequiredCapabilities.Count > 0);
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("profile")]
    [InlineData("legacy")]
    [InlineData("mismatch")]
    public void UnboundAndMismatchedSourceContentCannotAuthorizeCallableQueries(string binding)
    {
        var profile = Profile();
        profile = profile with
        {
            Profile = profile.Profile! with { SourceFingerprint = binding is "profile" or "legacy" ? null : profile.Profile.SourceFingerprint },
            AnalyzerOutcomes = [Outcome() with { SourceFingerprint = binding is "outcome" or "legacy" ? null
                : binding == "mismatch" ? "sha256:" + new string('b', 64) : Outcome().SourceFingerprint }]
        };
        ContextWorkflowToolPolicy.Evaluate("find_impact", profile).Status.Should().Be("stale");
    }

    internal static ProjectProfileResult Profile(string kind = "csharp", string analyzer = "csharp", string[]? capabilities = null) =>
        new("available", "Fixture", 1, 1, DateTimeOffset.UtcNow,
            new("Fixture", DateTimeOffset.UtcNow, "complete", 1, [new(kind, 1, ["src/A.cs"])], [], 0,
                [new(analyzer, analyzer != "powershell", analyzer == "powershell" ? "unsupported" : "enabled", "unknown",
                    capabilities ?? ["callable_definitions", "callable_dependencies"], [])], [], 0)
                { SourceFingerprint = "sha256:" + new string('a', 64) },
            new("src/A.cs", "observed", kind), [])
        { AnalyzerOutcomes = [Outcome() with { Analyzer = analyzer }] };

    internal static ProjectAnalyzerOutcome Outcome() => new("csharp", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
        { SourceFingerprint = "sha256:" + new string('a', 64) };
}
