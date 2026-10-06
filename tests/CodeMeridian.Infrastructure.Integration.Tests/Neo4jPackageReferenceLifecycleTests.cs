using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jPackageReferenceLifecycleTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    private string _sourceId = null!;
    private string _targetId = null!;
    private const string Assembly = "Shared, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
    private PackageBuildScope Scope => new("scope", "Shared.csproj", "net10.0", "Debug", "Shared", Assembly,
        "Shared", "1.0.0", "https://example.com/shared", "commit", false, "msbuild.workspace");
    private PackageReferenceSnapshot Producer(string project) => new("1.0", project, Guid.NewGuid().ToString(), [Scope],
        [new("export", "scope", _targetId, "symbol", "src/Shared.cs")], [], []);
    private PackageReferenceSnapshot Consumer(string project) => new("1.0", project, Guid.NewGuid().ToString(), [Scope], [],
        [new("reference", "scope", _sourceId, "symbol", "Shared", Assembly, "Shared", "1.0.0", "https://example.com/shared", "commit", "Calls", "src/Service.cs", 1, 1, 1, 10)], []);

    private async Task SeedPackageNodes(string consumer, string producer)
    {
        _sourceId = consumer + ".Service.Run";
        _targetId = producer + ".Shared.Validate";
        await _repository!.UpsertNodeAsync(BaselineMethod with { Id = _sourceId, ProjectContext = consumer, FilePath = "src/Service.cs" });
        await _repository.UpsertNodeAsync(BaselineDependency with { Id = _targetId, ProjectContext = producer, FilePath = "src/Shared.cs" });
    }
    private async Task Publish(PackageReferenceSnapshot snapshot)
    {
        await _repository!.BeginPackageReferenceIndexAsync(snapshot.ProjectContext, snapshot.Generation);
        await _repository.PublishPackageReferencesAsync(snapshot);
    }
    private async Task ResolvePending(string producer)
    {
        foreach (var pending in await _repository!.GetPendingPackageReferencesAsync(100))
        {
            if (pending.Reference.SourceId != _sourceId) continue;
            var candidates = await _repository.GetPackageExportCandidatesAsync(pending);
            var candidate = candidates.Single(item => item.ProjectContext == producer);
            await _repository.TryResolvePackageReferenceAsync(pending, new("verified_source", "Fixture provenance matches.", candidate.Export.SourceId, producer, "1.0.0"));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Publish_EitherOrder_RetryAndProducerRemovalPreserveDependency(bool consumerFirst)
    {
        var consumer = "Integration.Consumer." + Guid.NewGuid().ToString("N");
        var producer = "Integration.Producer." + Guid.NewGuid().ToString("N");
        try
        {
            await SeedPackageNodes(consumer, producer);
            if (consumerFirst) await Publish(Consumer(consumer));
            await Publish(Producer(producer));
            if (!consumerFirst) await Publish(Consumer(consumer));
            await ResolvePending(producer);
            (await _repository!.GetPackageDependenciesAsync(consumer)).Should().ContainSingle()
                .Which.Resolution.Status.Should().Be("verified_source");
            (await _repository.QueryEdgesAsync(_sourceId)).Should().Contain(edge => edge.Resolver == "codemeridian.package");
            (await _repository.FindConnectionAsync(_sourceId, _targetId)).Should().NotBeEmpty();
            (await _repository.FindImpactAsync(_targetId)).Should().Contain(item => item.Node.Id == _sourceId);
            (await _repository.GetContextForEditingAsync(_targetId)).Callers.Should().Contain(node => node.Id == _sourceId);
            await Publish(Producer(producer));
            await ResolvePending(producer);
            (await _repository!.GetPackageDependenciesAsync(consumer)).Should().ContainSingle();
            await _repository.DeleteProjectAsync(producer);
            (await _repository!.GetPackageDependenciesAsync(consumer)).Should().ContainSingle()
                .Which.Resolution.Status.Should().Be("pending");
            (await _repository.QueryEdgesAsync(_sourceId)).Should().NotContain(edge => edge.Resolver == "codemeridian.package");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(consumer);
            await _repository.DeleteProjectAsync(producer);
        }
    }

    [Fact]
    public async Task Publication_SupersededGenerationCannotReplaceLiveSnapshot_AndChangedSourceInvalidatesEdges()
    {
        var consumer = "Integration.Consumer." + Guid.NewGuid().ToString("N");
        var producer = "Integration.Producer." + Guid.NewGuid().ToString("N");
        try
        {
            await SeedPackageNodes(consumer, producer);
            var file = BaselineDependency with { Id = producer + ".File", Type = CodeNodeType.File,
                ProjectContext = producer, FilePath = "src/Shared.cs", SourceHash = "original" };
            await _repository!.UpsertNodeAsync(file);
            await Publish(Producer(producer));
            await Publish(Consumer(consumer));
            await ResolvePending(producer);
            var older = Producer(producer);
            await _repository.BeginPackageReferenceIndexAsync(producer, older.Generation);
            await _repository.BeginPackageReferenceIndexAsync(producer, Guid.NewGuid().ToString());
            Func<Task> publish = () => _repository.PublishPackageReferencesAsync(older);
            await publish.Should().ThrowAsync<InvalidOperationException>();
            (await _repository.GetPackageDependenciesAsync(consumer)).Single().Resolution.Status.Should().Be("verified_source");
            await _repository.UpsertNodeAsync(file with { SourceHash = "changed" });
            (await _repository.GetPackageDependenciesAsync(consumer)).Single().Resolution.Status.Should().Be("pending");
            (await _repository.QueryEdgesAsync(_sourceId)).Should().NotContain(edge => edge.Resolver == "codemeridian.package");
            (await _repository.GetPackageExportCandidatesAsync((await _repository.GetPendingPackageReferencesAsync(100))
                .Single(item => item.ProjectContext == consumer))).Should().BeEmpty();
        }
        finally
        {
            await _repository!.DeleteProjectAsync(consumer);
            await _repository.DeleteProjectAsync(producer);
        }
    }

    [Fact]
    public async Task Resolve_ConcurrentPublicationRejectsOldDecision_AndPreservesManualEdge()
    {
        var consumer = "Integration.Consumer." + Guid.NewGuid().ToString("N");
        var producer = "Integration.Producer." + Guid.NewGuid().ToString("N");
        try
        {
            await SeedPackageNodes(consumer, producer);
            await _repository!.UpsertEdgeAsync(new() { SourceId = _sourceId, TargetId = _targetId, Type = CodeEdgeType.Calls });
            await Publish(Producer(producer));
            await Publish(Consumer(consumer));
            var old = (await _repository.GetPendingPackageReferencesAsync(100)).Single(reference => reference.ProjectContext == consumer);
            await Publish(Producer(producer));
            (await _repository.TryResolvePackageReferenceAsync(old, new("verified_source", "old", _targetId))).Should().BeFalse();
            await ResolvePending(producer);
            await _repository.UpsertEdgeAsync(new() { SourceId = _sourceId, TargetId = _targetId, Type = CodeEdgeType.Calls });
            var edges = await _repository.QueryEdgesAsync(_sourceId);
            edges.Should().ContainSingle(edge => edge.Resolver == "codemeridian.package");
            edges.Should().Contain(edge => edge.Type == CodeEdgeType.Calls && edge.Resolver != "codemeridian.package");
            await Publish(Consumer(consumer) with { References = [] });
            (await _repository.QueryEdgesAsync(_sourceId)).Should().Contain(edge => edge.Type == CodeEdgeType.Calls && edge.Resolver != "codemeridian.package");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(consumer);
            await _repository.DeleteProjectAsync(producer);
        }
    }
}
