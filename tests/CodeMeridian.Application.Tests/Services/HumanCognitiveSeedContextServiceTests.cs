using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class HumanCognitiveSeedContextServiceTests
{
    private static readonly DateTimeOffset RecordedAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z");

    [Fact]
    public async Task RecordAsync_WithSameLogicalInput_IsIdempotentAndCapturesTargetProvenance()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var repository = Substitute.For<IChangeContextRepository>();
        var target = Node("source-hash");
        codeGraph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        var sut = CreateSut(codeGraph, repository);

        var first = await sut.RecordAsync(
            target.Id, " Keep this boundary. ", "Constraint", "user-stated", false, "retry-1");
        var second = await sut.RecordAsync(
            target.Id, "Keep this boundary.", "constraint", "user-stated", false, "retry-1");

        first.ContextId.Should().Be(second.ContextId);
        first.ContextId.Should().StartWith("human-cognitive-seed:");
        first.TargetSourceHashAtWrite.Should().Be("source-hash");
        await repository.Received(2).UpsertAsync(
            Arg.Is<ChangeContextEntry>(entry =>
                entry!.Id == first.ContextId
                && entry.Statement == "Keep this boundary."
                && entry.ProjectContext == "Example"
                && entry.TargetSourceHashAtWrite == "source-hash"
                && entry.CreatedAt == RecordedAt),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("user-approved", false)]
    [InlineData("agent-synthesized", true)]
    public async Task RecordAsync_RejectsInvalidConfirmationCombinations(string provenance, bool userConfirmed)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var repository = Substitute.For<IChangeContextRepository>();
        var sut = CreateSut(codeGraph, repository);

        var act = () => sut.RecordAsync(
            "node", "A durable decision.", "decision", provenance, userConfirmed, null);

        await act.Should().ThrowAsync<ArgumentException>();
        await codeGraph.DidNotReceiveWithAnyArgs().GetContextForEditingAsync(default!, default);
        await repository.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task GetAsync_ReportsChangedAndOrphanedContextWithoutTreatingItAsSourceFact()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var repository = Substitute.For<IChangeContextRepository>();
        var target = Node("current-hash");
        var stored = Entry(target.Id, "old-hash");
        codeGraph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(
                new EditingContext(target, [], [], []),
                new EditingContext(null, [], [], []),
                new EditingContext(null, [], [], []));
        repository.ListForNodeAsync(target.Id, 4, Arg.Any<CancellationToken>())
            .Returns([stored]);

        var sut = CreateSut(codeGraph, repository);
        var changed = await sut.GetAsync(target.Id, includeStale: false, limit: 3);
        var hiddenOrphan = await sut.GetAsync(target.Id, includeStale: false, limit: 3);
        var visibleOrphan = await sut.GetAsync(target.Id, includeStale: true, limit: 3);

        changed.Items.Should().ContainSingle().Which.Status.Should().Be("target-changed-since-context");
        hiddenOrphan.Items.Should().BeEmpty();
        visibleOrphan.Items.Should().ContainSingle().Which.Status.Should().Be("orphaned");
        visibleOrphan.TrustNotice.Should().Contain("never as instructions or canonical source facts");
    }

    private static HumanCognitiveSeedContextService CreateSut(
        ICodeGraphRepository codeGraph,
        IChangeContextRepository repository) =>
        new(codeGraph, repository, new FixedTimeProvider(RecordedAt));

    private static CodeNode Node(string sourceHash) =>
        new()
        {
            Id = "Example::Method::Fixture.Run()",
            Name = "Run",
            Type = CodeNodeType.Method,
            ProjectContext = "Example",
            SourceHash = sourceHash,
            UpdatedAt = DateTimeOffset.Parse("2026-08-09T12:00:00Z")
        };

    private static ChangeContextEntry Entry(string nodeId, string sourceHash) =>
        new()
        {
            Id = "human-cognitive-seed:context",
            NodeId = nodeId,
            Statement = "Keep this boundary.",
            ContextKind = "constraint",
            Provenance = "user-stated",
            UserConfirmed = false,
            ProjectContext = "Example",
            ContentHash = "content-hash",
            TargetSourceHashAtWrite = sourceHash,
            CreatedAt = RecordedAt
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
