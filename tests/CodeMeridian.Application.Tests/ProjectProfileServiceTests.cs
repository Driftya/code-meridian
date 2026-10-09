using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests;

public sealed class ProjectProfileServiceTests
{
    [Fact]
    public async Task MissingProfileAndTargetRemainUnknown()
    {
        var service = new ProjectProfileService(Substitute.For<IProjectProfileRepository>());
        var result = await service.GetAsync("Docs", "docs\\README.md");
        result.Status.Should().Be("unknown");
        result.Target!.Path.Should().Be("docs/README.md");
        result.Target.Status.Should().Be("unknown");
        result.Profile.Should().BeNull();
        result.ToMarkdown().Should().Contain("unknown");
    }

    [Theory]
    [InlineData(1, "available", "absent")]
    [InlineData(2, "stale", "unknown")]
    public async Task UnpublishedGenerationPreservesProfileAndDoesNotClaimTargetAbsence(long latest, string status, string targetStatus)
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        var profile = new ProjectProfile("Docs", DateTimeOffset.UtcNow, "complete", 0, [], [], 0, [], [], 0);
        repository.GetAsync("Docs", "README.md", Arg.Any<CancellationToken>())
            .Returns(new StoredProjectProfile(1, latest, DateTimeOffset.UtcNow, profile, null));
        var result = await new ProjectProfileService(repository).GetAsync("Docs", "README.md");
        result.Status.Should().Be(status);
        result.Target!.Status.Should().Be(targetStatus);
        result.Profile.Should().BeSameAs(profile);
    }

    [Fact]
    public async Task ObservedTargetIsReportedFromRetainedInventoryAndCancellationIsForwarded()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        using var cancellation = new CancellationTokenSource();
        var profile = new ProjectProfile("Docs", DateTimeOffset.UtcNow, "complete", 1, [], [], 0, [], [], 0);
        repository.GetAsync("Docs", "README.md", cancellation.Token)
            .Returns(new StoredProjectProfile(1, 2, DateTimeOffset.UtcNow, profile, new("README.md", "markdown")));
        var result = await new ProjectProfileService(repository).GetAsync("Docs", "README.md", cancellation.Token);
        result.Status.Should().Be("stale");
        result.Target!.Status.Should().Be("observed");
        result.Target.Kind.Should().Be("markdown");
        await repository.Received(1).GetAsync("Docs", "README.md", cancellation.Token);
    }

    [Fact]
    public async Task InvalidTargetIsRejectedBeforeQueryingRepository()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        var act = () => new ProjectProfileService(repository).GetAsync("Docs", "../secret.md");
        await act.Should().ThrowAsync<ArgumentException>();
        await repository.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }
}
