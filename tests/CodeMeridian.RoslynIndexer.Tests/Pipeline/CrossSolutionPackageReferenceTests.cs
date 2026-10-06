using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CrossSolutionPackageReferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-package-fixture", Guid.NewGuid().ToString("N"));
    private readonly RecordingHandler _handler = new();

    [Fact]
    public async Task Index_LocalNuGetAcrossIndependentRepositories_ExtractsExactSymbolsAndPreservesInstalledVersion()
    {
        var producer = Path.Combine(_root, "producer");
        var consumer = Path.Combine(_root, "consumer");
        var feed = Path.Combine(_root, "feed");
        Directory.CreateDirectory(feed);
        Write(producer, "Shared.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
            <TargetFramework>net10.0</TargetFramework><PackageId>Fixture.Shared</PackageId><AssemblyName>Shared</AssemblyName>
            <Version>1.0.0</Version><RepositoryUrl>https://example.com/shared</RepositoryUrl>
            </PropertyGroup></Project>
            """);
        Write(producer, ".gitignore", "bin/\nobj/\n");
        Write(producer, "Shared.cs", """
            namespace Shared;
            public class Validator
            {
                public Validator() { }
                public void Validate(string value) { }
                public void Validate(int value) { }
                public static T Echo<T>(T value) => value;
            }
            public interface IValidator { void Validate(string value); }
            """);
        await Run(producer, "git", "init");
        await Run(producer, "git", "add", ".");
        await Run(producer, "git", "-c", "user.name=Fixture", "-c", "user.email=fixture@example.com", "commit", "-m", "fixture");
        var commit = (await Run(producer, "git", "rev-parse", "HEAD")).Trim();
        await Run(producer, "dotnet", "pack", "Shared.csproj", "-o", feed, "-p:RepositoryCommit=" + commit);
        Write(consumer, "App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            <ItemGroup><PackageReference Include="Fixture.Shared" Version="1.0.0" /></ItemGroup></Project>
            """);
        Write(consumer, "App.cs", """
            namespace App;
            public class Service : Shared.IValidator
            {
                public void Run()
                {
                    var validator = new Shared.Validator();
                    validator.Validate("value");
                    validator.Validate(42);
                    Shared.Validator.Echo("value");
                }
                public void Validate(string value) { }
            }
            """);
        await Run(consumer, "dotnet", "restore", "App.csproj", "--source", feed, "--packages", Path.Combine(_root, "packages"));

        // Reproduce extraction with consumer first: source need not already be indexed.
        await Index(consumer, "Application");
        var consumerSnapshot = _handler.Snapshots.Single();
        consumerSnapshot.References.Should().Contain(reference => reference.SymbolKey.Contains("Validate(System.String)", StringComparison.Ordinal));
        consumerSnapshot.References.Should().Contain(reference => reference.SymbolKey.Contains("Validate(System.Int32)", StringComparison.Ordinal));
        consumerSnapshot.References.Should().Contain(reference => reference.SymbolKey.Contains("#ctor", StringComparison.Ordinal));
        consumerSnapshot.References.Should().Contain(reference => reference.Relationship == "Implements");
        consumerSnapshot.References.Should().OnlyContain(reference => reference.PackageVersion == "1.0.0");

        await Index(producer, "Library");
        var producerSnapshot = _handler.Snapshots.Last();
        var method = consumerSnapshot.References.First(reference => reference.SymbolKey.Contains("Validate(System.String)", StringComparison.Ordinal));
        var pending = new PendingPackageReference(method, consumerSnapshot.Scopes.Single(scope => scope.Id == method.ScopeId), "Application", 1);
        var candidates = producerSnapshot.Exports.Where(export => export.SymbolKey == method.SymbolKey).Select(export =>
            new PackageExportCandidate(export, producerSnapshot.Scopes.Single(scope => scope.Id == export.ScopeId), "Library")).ToArray();
        new PackageReferenceMatcher().Match(pending, candidates).Status.Should().Be("verified_source");

        // Current checkout moves ahead; the compiled consumer dependency must remain v1.
        Write(producer, "Directory.Build.props", "<Project><PropertyGroup><PackageVersion>2.0.0</PackageVersion></PropertyGroup></Project>");
        await Index(producer, "Library");
        var current = _handler.Snapshots.Last();
        candidates = current.Exports.Where(export => export.SymbolKey == method.SymbolKey).Select(export =>
            new PackageExportCandidate(export, current.Scopes.Single(scope => scope.Id == export.ScopeId), "Library")).ToArray();
        new PackageReferenceMatcher().Match(pending, candidates).Status.Should().Be("associated_current_source");
        new PackageReferenceMatcher().Match(pending, candidates).ProducerVersion.Should().Be("2.0.0");
        method.PackageVersion.Should().Be("1.0.0");
    }

    private async Task Index(string root, string project)
    {
        using var http = new HttpClient(_handler, disposeHandler: false) { BaseAddress = new Uri("http://fixture") };
        var indexer = new CSharpIndexer(new CodeMeridianClient(http), NullLogger<CSharpIndexer>.Instance,
            Options.Create(new PackageIndexingOptions { AllowProjectEvaluation = true }));
        await indexer.IndexAsync(Directory.GetFiles(root, "*.cs").Select(path => new FileInfo(path)).ToArray(), project, root);
    }

    [Fact]
    public async Task Index_SourceProjectOutsideRoot_PreservesReferenceWithoutCopyingProducerOwnership()
    {
        var library = Path.Combine(_root, "library");
        var app = Path.Combine(_root, "app");
        Write(library, "Shared.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        Write(library, "Shared.cs", "namespace Shared;\npublic class Library\n{\n public void Run() { }\n}\n");
        Write(app, "App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup><ProjectReference Include=\"../library/Shared.csproj\" /></ItemGroup></Project>");
        Write(app, "App.cs", "namespace App;\npublic class Service\n{\n public void Run() { new Shared.Library().Run(); }\n}\n");
        await Run(app, "dotnet", "restore", "App.csproj");
        await Index(app, "Application");
        var snapshot = _handler.Snapshots.Single();
        snapshot.Exports.Should().NotContain(export => export.SymbolKey.Contains("Shared.Library", StringComparison.Ordinal));
        snapshot.References.Should().Contain(reference => reference.DependencyKind == "source_project" && reference.SymbolKey.Contains("Shared.Library.Run", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Index_MultiTargetConditionalSymbols_PublishesEvaluatedFrameworkDeclarations()
    {
        var library = Path.Combine(_root, "multi");
        Write(library, "Multi.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks></PropertyGroup></Project>");
        Write(library, "Multi.cs", """
            namespace Multi;
            public class Feature
            {
            #if WINDOWS
                public void WindowsOnly() { }
            #endif
                public void Common() { }
            }
            """);
        await Run(library, "dotnet", "restore", "Multi.csproj");
        await Index(library, "Multi");
        var snapshot = _handler.Snapshots.Single();
        snapshot.Scopes.Should().HaveCount(2);
        var windows = snapshot.Scopes.Single(scope => scope.TargetFramework == "net10.0-windows");
        snapshot.Exports.Should().Contain(export => export.ScopeId == windows.Id && export.SymbolKey.Contains("WindowsOnly", StringComparison.Ordinal));
        snapshot.Exports.Should().NotContain(export => export.ScopeId != windows.Id && export.SymbolKey.Contains("WindowsOnly", StringComparison.Ordinal));
    }

    private static void Write(string directory, string file, string content)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, file), content);
    }

    private static async Task<string> Run(string directory, string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        process.ExitCode.Should().Be(0, await stderr + output);
        return output;
    }

    public void Dispose()
    {
        _handler.Dispose();
        if (Directory.Exists(_root))
        {
            foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<PackageReferenceSnapshot> Snapshots { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/package-references", StringComparison.Ordinal))
                Snapshots.Add((await request.Content!.ReadFromJsonAsync<PackageReferenceSnapshot>(cancellationToken: cancellationToken))!);
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new { }) };
        }
    }
}
