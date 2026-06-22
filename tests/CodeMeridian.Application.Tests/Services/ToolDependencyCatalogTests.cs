using CodeMeridian.Application.Services;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ToolDependencyCatalogTests
{
    [Fact]
    public void FindSubject_NormalizesIdsAndAliases()
    {
        ToolDependencyCatalog.FindSubject("  MCP__CodeMeridian.BUILD_MINIMAL_CONTEXT  ")
            .Should()
            .BeSameAs(ToolDependencyCatalog.Subjects["build_minimal_context"]);

        ToolDependencyCatalog.FindSubject(" CodeMeridian.find_test_shield ")
            .Should()
            .BeSameAs(ToolDependencyCatalog.Subjects["find_test_shield"]);

        ToolDependencyCatalog.FindSubject("  report pr-context  ")
            .Should()
            .BeSameAs(ToolDependencyCatalog.Subjects["pr_context_report"]);
    }

    [Fact]
    public void Subjects_ExposeUniqueNormalizedLookupKeys()
    {
        var ownersByLookupKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subject in ToolDependencyCatalog.Subjects.Values)
        {
            subject.Id.Should().Be(subject.Id.Trim().ToLowerInvariant());
            RegisterLookupKey(subject.Id, subject.Id, ownersByLookupKey);

            foreach (var alias in subject.Aliases)
            {
                alias.Should().Be(alias.Trim().ToLowerInvariant());
                RegisterLookupKey(alias, subject.Id, ownersByLookupKey);
                ToolDependencyCatalog.FindSubject($"  {alias.ToUpperInvariant()}  ")
                    .Should()
                    .BeSameAs(subject);
            }
        }
    }

    [Fact]
    public void Edges_ReferenceKnownSubjects_AndExistingArtifacts()
    {
        var repositoryRoot = FindRepositoryRoot();

        ToolDependencyCatalog.Edges.Should().NotBeEmpty();

        foreach (var edge in ToolDependencyCatalog.Edges)
        {
            ToolDependencyCatalog.Subjects.Should().ContainKey(edge.ProducerId);
            ToolDependencyCatalog.Subjects.Should().ContainKey(edge.ConsumerId);
            edge.ContractType.Should().NotBeNullOrWhiteSpace();
            edge.ImpactLevel.Should().BeOneOf("hard", "awareness");
            edge.Reason.Should().NotBeNullOrWhiteSpace();
            edge.RegressionSuites.Should().OnlyHaveUniqueItems();
            edge.ReviewArtifacts.Should().OnlyHaveUniqueItems();

            foreach (var relativePath in edge.RegressionSuites.Concat(edge.ReviewArtifacts))
            {
                var fullPath = Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                File.Exists(fullPath).Should().BeTrue($"catalog artifact `{relativePath}` should exist for `{edge.ProducerId}` -> `{edge.ConsumerId}`");
            }
        }
    }

    [Fact]
    public void EverySubject_ParticipatesInTrackedDependencies_WithRegressionCoverage()
    {
        foreach (var subject in ToolDependencyCatalog.Subjects.Values)
        {
            var incidentEdges = ToolDependencyCatalog.Edges
                .Where(edge =>
                    edge.ProducerId.Equals(subject.Id, StringComparison.OrdinalIgnoreCase)
                    || edge.ConsumerId.Equals(subject.Id, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            incidentEdges.Should().NotBeEmpty($"`{subject.Id}` should participate in at least one tracked dependency");
            incidentEdges.SelectMany(edge => edge.RegressionSuites)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Should()
                .NotBeEmpty($"`{subject.Id}` should point at at least one regression suite through its tracked dependencies");
        }
    }

    private static void RegisterLookupKey(string lookupKey, string subjectId, IDictionary<string, string> ownersByLookupKey)
    {
        ownersByLookupKey.TryGetValue(lookupKey, out var existingOwner).Should().BeFalse(
            $"lookup key `{lookupKey}` is already owned by `{existingOwner}` and cannot also belong to `{subjectId}`");
        ownersByLookupKey[lookupKey] = subjectId;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMeridian.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must run from inside the repository checkout");
        return directory!.FullName;
    }
}
