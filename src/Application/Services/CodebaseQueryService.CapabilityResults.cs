using CodeMeridian.Application.Services.ContextWorkflows;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<ImpactAnalysisResult> FindImpactResultAsync(string nodeId, int depth = 5, bool includeConfidence = false,
        CancellationToken cancellationToken = default) => ExecuteCapabilityQueryAsync("find_impact", null, nodeId,
        () => FindImpactResultCoreAsync(nodeId, depth, includeConfidence, cancellationToken),
        applicability => new ImpactAnalysisResult("1.0", nodeId, depth, false, false, "Unknown",
            new("Unknown", "Analysis did not run.", null, null, 0, 0, 0, 0, 0, []), [], false) { Applicability = applicability },
        (result, applicability) => result with { Applicability = applicability }, cancellationToken);

    public Task<ConnectionAnalysisResult> FindConnectionResultAsync(string fromId, string toId,
        CancellationToken cancellationToken = default) => ExecuteCapabilityQueryAsync("find_connection", null, fromId,
        () => FindConnectionResultCoreAsync(fromId, toId, cancellationToken),
        applicability => BlockedConnection(fromId, toId, applicability),
        (result, applicability) => result with { Applicability = applicability }, cancellationToken, toId);

    private static ConnectionAnalysisResult BlockedConnection(string fromId, string toId, ContextWorkflowApplicability applicability) =>
        new("1.0", fromId, toId, 10, false, 0, false, [], [], []) { Applicability = applicability };

    public Task<TestShieldResult> FindTestShieldResultAsync(string nodeId, string? projectContext = null, int depth = 2,
        int limit = 20, CancellationToken cancellationToken = default) => ExecuteCapabilityQueryAsync("find_test_shield", projectContext, nodeId,
        () => FindTestShieldResultCoreAsync(nodeId, projectContext, depth, limit, cancellationToken),
        applicability => new TestShieldResult("1.0", nodeId, projectContext, Math.Clamp(depth, 1, 8), false, null, null,
            [], [], [], [], [], null, false) { Applicability = applicability },
        (result, applicability) => result with { Applicability = applicability }, cancellationToken);

    public Task<CrossProjectDependencyResult> FindCrossProjectDependenciesResultAsync(string? projectContext = null,
        CancellationToken cancellationToken = default) => ExecuteCapabilityQueryAsync("find_cross_project_dependencies", projectContext, null,
        () => FindCrossProjectDependenciesResultCoreAsync(projectContext, cancellationToken),
        applicability => new CrossProjectDependencyResult("1.0", projectContext, [], [], false) { Applicability = applicability },
        (result, applicability) => result with { Applicability = applicability }, cancellationToken);

    public Task<MinimalContextResult> BuildMinimalContextResultAsync(string target, string? goal = null, int maxTokens = 3000,
        bool includeTests = true, bool includeExternalConcepts = true, bool includeSourceSnippets = false,
        bool explainPaths = false, ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default) => ExecuteCapabilityQueryAsync("build_minimal_context", null, target,
        async () =>
        {
            var tests = includeTests ? await EvaluateToolAsync("find_test_shield", null, target, cancellationToken) : null;
            var includeVerifiedTests = includeTests && (tests is null || tests.Status == "ready");
            var result = await BuildMinimalContextResultCoreAsync(target, goal, maxTokens, includeVerifiedTests,
                includeExternalConcepts, includeSourceSnippets, explainPaths, detailLevel, cancellationToken);
            if (includeVerifiedTests && tests is not null)
            {
                tests = await RevalidateToolAsync("find_test_shield", tests, target, cancellationToken);
                if (tests.Status != "ready")
                {
                    includeVerifiedTests = false;
                    result = await BuildMinimalContextResultCoreAsync(target, goal, maxTokens, false,
                        includeExternalConcepts, includeSourceSnippets, explainPaths, detailLevel, cancellationToken);
                }
            }
            return includeTests && !includeVerifiedTests ? result with
            {
                Degradations = result.Degradations.Append(new("test_context_omitted", tests!.Status + ": " + string.Join(" ", tests.Reasons))).ToArray()
            } : result;
        },
        applicability => MinimalContextResult.NotFound(target, goal, maxTokens, includeTests,
            includeExternalConcepts, includeSourceSnippets, explainPaths, detailLevel) with { Applicability = applicability },
        (result, applicability) => result with { Applicability = applicability }, cancellationToken);
}
