using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProducerReportFileTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("nonce")]
    [InlineData("worker")]
    [InlineData("scope")]
    [InlineData("oversized")]
    public async Task ReceiptsMustBelongToTheActualInvocationAndRespectBounds(string mode)
    {
        var hash = "sha256:" + new string('a', 64);
        var worker = new ProjectAnalyzerFingerprint(hash, hash);
        using var receipt = new ProducerReportFile("frontend", "Fixture", "web", worker);
        var report = new ProducerExecutionReport("1", receipt.Environment["CODEMERIDIAN_PRODUCER_REQUEST_ID"]!, "frontend", "Fixture", "web", worker,
            false, [], new Dictionary<string, string>(), []);
        if (mode == "nonce") report = report with { RequestId = "old invocation" };
        if (mode == "scope") report = report with { Scope = "." };
        if (mode == "worker") report = report with { Fingerprint = new("sha256:" + new string('b', 64), hash) };
        var path = receipt.Environment["CODEMERIDIAN_PRODUCER_REPORT"]!;
        await File.WriteAllTextAsync(path, mode == "oversized" ? new string('x', 4 * 1024 * 1024 + 1)
            : JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        if (mode == "valid") (await receipt.ReadAsync(CancellationToken.None)).Published.Should().BeFalse();
        else await FluentActions.Awaiting(() => receipt.ReadAsync(CancellationToken.None)).Should().ThrowAsync<InvalidDataException>();
    }
}
