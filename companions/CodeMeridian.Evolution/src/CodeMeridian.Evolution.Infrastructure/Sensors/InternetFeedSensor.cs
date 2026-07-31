using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CodeMeridian.Evolution.Application.Sensors;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class InternetFeedSensor(
    IHttpClientFactory httpClientFactory,
    IOptions<InternetFeedOptions> options,
    TimeProvider timeProvider) : ISensor
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    public string Id => "internet-feed";

    public string DisplayName => "Allowlisted internet feeds";

    public Task<SensorHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = options.Value.Enabled ? "ready" : "disabled";
        return Task.FromResult(new SensorHealth(
            IsHealthy: options.Value.Enabled,
            status,
            timeProvider.GetUtcNow()));
    }

    public async Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        var configured = options.Value;

        if (!configured.Enabled)
        {
            return [];
        }

        if (configured.MaximumResponseBytes is < 1 or > 4_194_304)
        {
            throw new InvalidOperationException(
                "The feed response limit must be between 1 byte and 4 MiB.");
        }

        var observations = new List<SensorObservation>();
        var client = httpClientFactory.CreateClient("evolution-internet-feed");

        foreach (var configuredUrl in configured.FeedUrls)
        {
            var feedUri = ValidateUri(configuredUrl, configured);
            using var response = await client.GetAsync(
                feedUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > configured.MaximumResponseBytes)
            {
                throw new InvalidOperationException(
                    $"Feed '{feedUri.Host}' exceeded the configured response limit.");
            }

            await using var responseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var bounded = new MemoryStream();
            await CopyBoundedAsync(
                responseStream,
                bounded,
                configured.MaximumResponseBytes,
                cancellationToken).ConfigureAwait(false);
            bounded.Position = 0;

            using var reader = XmlReader.Create(bounded, new XmlReaderSettings
            {
                Async = false,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            observations.AddRange(Parse(
                document,
                feedUri,
                configured.ProjectId,
                Math.Clamp(configured.MaximumItemsPerFeed, 1, 100),
                timeProvider.GetUtcNow()));
        }

        return Array.AsReadOnly(observations.ToArray());
    }

    private static IEnumerable<SensorObservation> Parse(
        XDocument document,
        Uri feedUri,
        string projectId,
        int maximumItems,
        DateTimeOffset collectedAt)
    {
        var rssItems = document.Descendants("item")
            .Select(item => new
            {
                Title = item.Element("title")?.Value,
                Link = item.Element("link")?.Value,
                StableKey = item.Element("guid")?.Value,
                Published = item.Element("pubDate")?.Value
            });
        var atomItems = document.Descendants(Atom + "entry")
            .Select(item => new
            {
                Title = item.Element(Atom + "title")?.Value,
                Link = item.Elements(Atom + "link")
                    .FirstOrDefault(link => link.Attribute("href") is not null)
                    ?.Attribute("href")?.Value,
                StableKey = item.Element(Atom + "id")?.Value,
                Published = item.Element(Atom + "updated")?.Value
            });

        return rssItems
            .Concat(atomItems)
            .Where(item => !string.IsNullOrWhiteSpace(item.Title))
            .Take(maximumItems)
            .Select(item =>
            {
                var link = ResolveLink(feedUri, item.Link);
                var stableKey = item.StableKey ?? link?.AbsoluteUri ?? item.Title!;
                var observedAt = DateTimeOffset.TryParse(
                    item.Published,
                    out var published)
                    ? published
                    : collectedAt;
                return new SensorObservation(
                    StableId(feedUri, stableKey),
                    "internet-feed-item",
                    link is null ? item.Title! : $"{item.Title} — {link.AbsoluteUri}",
                    "information",
                    observedAt,
                    0.55m)
                {
                    ProjectId = projectId,
                    TrustLevel = "untrusted-internet",
                    SourceUri = link?.AbsoluteUri ?? feedUri.AbsoluteUri
                };
            });
    }

    private static Uri ValidateUri(string configuredUrl, InternetFeedOptions configured)
    {
        if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Internet feed URLs must be absolute HTTPS URLs.");
        }

        var allowedHosts = configured.AllowedHosts.Length == 0
            ? configured.FeedUrls
                .Select(url => Uri.TryCreate(url, UriKind.Absolute, out var item) ? item.Host : null)
                .Where(host => host is not null)
            : configured.AllowedHosts;

        if (!allowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Internet feed host '{uri.Host}' is not allowlisted.");
        }

        return uri;
    }

    private static Uri? ResolveLink(Uri feedUri, string? link)
    {
        return Uri.TryCreate(link, UriKind.Absolute, out var absolute)
            ? absolute
            : Uri.TryCreate(feedUri, link, out var relative)
                ? relative
                : null;
    }

    private static string StableId(Uri feedUri, string stableKey)
    {
        var bytes = Encoding.UTF8.GetBytes($"{feedUri.AbsoluteUri}|{stableKey}");
        return $"feed:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16_384];
        var total = 0;
        int read;

        while ((read = await source
                   .ReadAsync(buffer, cancellationToken)
                   .ConfigureAwait(false)) > 0)
        {
            total += read;

            if (total > maximumBytes)
            {
                throw new InvalidOperationException(
                    "Internet feed exceeded the configured response limit.");
            }

            await destination
                .WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
