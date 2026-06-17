using CodeMeridian.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class KeywordGraphJobServiceTests
{
    [Fact]
    public async Task StartRebuildAsync_GuardsConcurrentJobsUntilLeaseExpires()
    {
        var timeProvider = new MutableTimeProvider(DateTimeOffset.Parse("2026-06-17T10:00:00Z"));
        var taskCompletionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keywordGraphService = Substitute.For<IKeywordGraphService>();
        keywordGraphService
            .RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(_ => taskCompletionSource.Task);

        var sut = CreateSut(timeProvider, keywordGraphService);

        var first = await sut.StartRebuildAsync("CodeMeridian", TimeSpan.FromMinutes(10));
        first.Accepted.Should().BeTrue();
        first.Job.Operation.Should().Be("rebuild");

        var conflict = await sut.StartClassifyAsync("CodeMeridian", TimeSpan.FromMinutes(10));
        conflict.Accepted.Should().BeFalse();
        conflict.Job.JobId.Should().Be(first.Job.JobId);
        conflict.Job.State.Should().Be("Running");

        timeProvider.Advance(TimeSpan.FromMinutes(11));

        var second = await sut.StartClassifyAsync("CodeMeridian", TimeSpan.FromMinutes(10));
        second.Accepted.Should().BeTrue();
        second.Job.Operation.Should().Be("classify");
        second.Job.JobId.Should().NotBe(first.Job.JobId);

        taskCompletionSource.SetResult("rebuild complete");
        await WaitForCompletionAsync(sut, first.Job.JobId);

        var completed = await sut.GetStatusAsync(first.Job.JobId);
        completed.Should().NotBeNull();
        completed!.State.Should().Be("Completed");
    }

    private static IKeywordGraphJobService CreateSut(TimeProvider timeProvider, IKeywordGraphService keywordGraphService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(timeProvider);
        services.AddSingleton(keywordGraphService);
        services.AddSingleton<IKeywordGraphJobService, KeywordGraphJobService>();
        return services.BuildServiceProvider().GetRequiredService<IKeywordGraphJobService>();
    }

    private static async Task WaitForCompletionAsync(IKeywordGraphJobService sut, Guid jobId)
    {
        for (var i = 0; i < 50; i++)
        {
            var status = await sut.GetStatusAsync(jobId);
            if (status is { State: "Completed" or "Failed" or "Expired" })
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("Job did not complete in time.");
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
