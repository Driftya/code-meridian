using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class CrossSolutionNuGetGraphTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Index_TwoRepositoriesWithLocalPackage_ReconcilesExtractedReferencesInEitherOrder(bool consumerFirst)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-nuget-graph", Guid.NewGuid().ToString("N"));
        var producer = Path.Combine(root, "library");
        var consumer = Path.Combine(root, "application");
        var feed = Path.Combine(root, "feed");
        var producerContext = "Integration.Library." + Guid.NewGuid().ToString("N");
        var consumerContext = "Integration.Application." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(feed);
        var packageId = "Fixture.Shared." + Guid.NewGuid().ToString("N");
        try
        {
            Write(producer, "Shared.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><PackageId>{packageId}</PackageId><Version>1.0.0</Version></PropertyGroup></Project>");
            Write(producer, "Library.cs", """
                namespace Shared;
                public class Validator
                {
                    public void Check(string value) { }
                    public void Check(int value) { }
                }
                """);
            await Run(producer, "pack", "Shared.csproj", "-o", feed);
            Write(consumer, "App.csproj", $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"{packageId}\" Version=\"1.0.0\" /></ItemGroup></Project>");
            Write(consumer, "Service.cs", """
                namespace App;
                public class Service
                {
                    public void Run()
                    {
                        var validator = new Shared.Validator();
                        validator.Check("value");
                        validator.Check(42);
                    }
                }
                """);
            await Run(consumer, "restore", "App.csproj", "--source", feed, "--packages", Path.Combine(root, "packages"));
            using var handler = new GraphIngestionHandler(_repository!);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("http://fixture") };
            var indexer = new CSharpIndexer(new CodeMeridianClient(http), NullLogger<CSharpIndexer>.Instance,
                Options.Create(new PackageIndexingOptions { AllowProjectEvaluation = true }));
            async Task Index(string directory, string project) => await indexer.IndexAsync(
                Directory.GetFiles(directory, "*.cs").Select(path => new FileInfo(path)).ToArray(), project, directory);
            if (consumerFirst)
            {
                await Index(consumer, consumerContext);
                (await _repository!.GetPackageDependenciesAsync(consumerContext)).Should().OnlyContain(dependency => dependency.Resolution.Status == "source_not_indexed");
            }
            await Index(producer, producerContext);
            if (!consumerFirst) await Index(consumer, consumerContext);
            var dependencies = await _repository!.GetPackageDependenciesAsync(consumerContext);
            dependencies.Should().Contain(dependency => dependency.Reference.SymbolKey.Contains("Check(System.String)", StringComparison.Ordinal)
                && dependency.Target!.ProjectContext == producerContext && dependency.Resolution.Status == "associated_current_source");
            dependencies.Should().Contain(dependency => dependency.Reference.SymbolKey.Contains("Check(System.Int32)", StringComparison.Ordinal));
            dependencies.Should().Contain(dependency => dependency.Reference.SymbolKey.Contains("#ctor", StringComparison.Ordinal) && dependency.Target != null);
            dependencies.Should().OnlyContain(dependency => dependency.Reference.PackageVersion == "1.0.0");
            var count = dependencies.Count;
            await Index(producer, producerContext);
            (await _repository.GetPackageDependenciesAsync(consumerContext)).Count.Should().Be(count);
            await _repository.DeleteProjectAsync(producerContext);
            (await _repository.GetPackageDependenciesAsync(consumerContext)).Should().OnlyContain(dependency => dependency.Target == null);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(consumerContext);
            await _repository.DeleteProjectAsync(producerContext);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), content);
    }

    private static async Task Run(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        process.ExitCode.Should().Be(0, await error + await output);
    }

    private sealed class GraphIngestionHandler(ICodeGraphRepository repository) : HttpMessageHandler
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/package-references/begin", StringComparison.Ordinal))
                await repository.BeginPackageReferenceIndexAsync(json.RootElement.GetProperty("projectContext").GetString()!, json.RootElement.GetProperty("generation").GetString()!, cancellationToken);
            else if (path.EndsWith("/package-references", StringComparison.Ordinal))
                await new PackageReferenceService(repository, new PackageReferenceMatcher()).PublishAsync(json.RootElement.Deserialize<PackageReferenceSnapshot>(JsonOptions)!, cancellationToken);
            else if (path.EndsWith("/nodes/bulk", StringComparison.Ordinal))
            {
                foreach (var node in json.RootElement.EnumerateArray())
                {
                    var packet = JsonNode.Parse(node.GetRawText())!.AsObject();
                    if (packet["fileRole"] is null) packet.Remove("fileRole");
                    if (packet["properties"] is null) packet.Remove("properties");
                    await repository.UpsertNodeAsync(packet.Deserialize<CodeNode>(JsonOptions)!, cancellationToken);
                }
            }
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new { }) };
        }
    }
}
