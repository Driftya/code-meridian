using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class ProjectProfilePublicationFingerprintTests
{
    [Fact]
    public void LegacyRetriesKeepTheFingerprintRecordedByOlderServers()
    {
        var snapshot = Snapshot();
        const string originalPayload = """
            {"generation":1,"profile":{"project":"Docs","observedAt":"1970-01-01T00:00:00+00:00","discoveryState":"complete","fileCount":0,"fileKinds":[],"analysisRoots":[],"analysisRootCount":0,"analyzers":[{"id":"documents","supported":true,"configurationState":"enabled","indexingState":"unknown","supportedCapabilities":["document_search"],"limitations":[]}],"warnings":[],"warningCount":0,"contractVersion":"1.0","evidenceSource":"local_discovery"},"files":[],"contractVersion":"1.0"}
            """;
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalPayload)));

        ProjectProfilePublicationFingerprint.Legacy(snapshot).Should().Be(expected);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("configuration")]
    [InlineData("analyzer")]
    public void KnownMetadataCannotBeStrippedToAcceptAConflictingRetry(string metadata)
    {
        var snapshot = Snapshot();
        var hash = "sha256:" + new string('a', 64);
        var profile = snapshot.Profile;
        snapshot = snapshot with { Profile = metadata switch
        {
            "revision" => profile with { Revision = new(new string('a', 40), false) },
            "configuration" => profile with { ConfigurationFingerprint = hash },
            _ => profile with { Analyzers = [profile.Analyzers[0] with { Fingerprint = new(hash, hash) }] }
        } };

        ProjectProfilePublicationFingerprint.Legacy(snapshot).Should().BeNull();
    }

    private static ProjectProfileSnapshot Snapshot() => new(1,
        new("Docs", DateTimeOffset.UnixEpoch, "complete", 0, [], [], 0,
            [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0), []);
}
