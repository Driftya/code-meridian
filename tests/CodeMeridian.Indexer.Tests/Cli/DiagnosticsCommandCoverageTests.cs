using System.Reflection;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class DiagnosticsCommandCoverageTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"codemeridian-diagnostics-{Guid.NewGuid():N}"));

    [Fact]
    public void PrivateParsers_ParseDotnetTypeScriptAndLintDiagnostics()
    {
        var dotnet = InvokePrivate<IReadOnlyList<object>>(
            "ParseDotnetDiagnostics",
            """
            src/Application/OrderService.cs(12,7): warning CS8602: Dereference of a possibly null reference. [J:\repo\CodeMeridian.Application.csproj]
            """,
            _root,
            "CodeMeridian");

        var typeScriptRoot = Directory.CreateDirectory(Path.Combine(_root.FullName, "web"));
        var typeScript = InvokePrivate<IReadOnlyList<object>>(
            "ParseTypeScriptDiagnostics",
            "src/app.ts(4,11): error TS2322: Type 'number' is not assignable to type 'string'.",
            _root,
            typeScriptRoot,
            "CodeMeridian");

        var lint = InvokePrivate<IReadOnlyList<object>>(
            "ParseLintDiagnostics",
            """
            src/app.ts
              4:11  warning  Unexpected console statement  no-console
            """,
            _root,
            "CodeMeridian");

        ReadFinding(dotnet.Single()).Should().BeEquivalentTo(("warning", "CS8602", "src/Application/OrderService.cs", 12, 7, "dotnet"));
        ReadFinding(typeScript.Single()).Should().BeEquivalentTo(("error", "TS2322", "web/src/app.ts", 4, 11, "tsc"));
        ReadFinding(lint.Single()).Should().BeEquivalentTo(("warning", "no-console", "src/app.ts", 4, 11, "eslint"));
    }

    [Fact]
    public void ResolveLintCommand_PrefersPackageScriptAndFallsBackToLocalBinary()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "package.json"), """{ "scripts": { "lint": "eslint ." } }""");

        var packageCommand = InvokePrivate<(string FileName, string[] Arguments)?>("ResolveLintCommand", _root);

        packageCommand.Should().NotBeNull();
        packageCommand!.Value.FileName.Should().NotBeNullOrWhiteSpace();
        packageCommand.Value.Arguments.Should().Equal("run", "lint");

        File.Delete(Path.Combine(_root.FullName, "package.json"));
        var binDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, "node_modules", ".bin"));
        File.WriteAllText(Path.Combine(binDirectory.FullName, OperatingSystem.IsWindows() ? "eslint.cmd" : "eslint"), "echo eslint");

        var eslintCommand = InvokePrivate<(string FileName, string[] Arguments)?>("ResolveLintCommand", _root);

        eslintCommand.Should().NotBeNull();
        eslintCommand!.Value.Arguments.Should().Equal(".");
        eslintCommand.Value.FileName.ToLowerInvariant().Should().Contain("eslint");
    }

    [Fact]
    public void ResolveLocalNodeBinary_FindsNearestInstalledExecutable()
    {
        var nestedRoot = Directory.CreateDirectory(Path.Combine(_root.FullName, "src", "client"));
        var binDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, "node_modules", ".bin"));
        var expectedFile = Path.Combine(binDirectory.FullName, OperatingSystem.IsWindows() ? "tsc.cmd" : "tsc");
        File.WriteAllText(expectedFile, "echo tsc");

        var resolved = InvokePrivate<string?>("ResolveLocalNodeBinary", nestedRoot, "tsc");

        resolved.Should().Be(expectedFile);
    }

    [Fact]
    public void NormalizePath_HandlesQuotedRelativeAndAbsolutePaths()
    {
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, "src"));
        var relative = InvokePrivate<string>("NormalizePath", "\"app.ts\"", _root, workingDirectory);
        var absolute = InvokePrivate<string>("NormalizePath", Path.Combine(_root.FullName, "src", "nested", "file.ts"), _root, workingDirectory);

        relative.Should().Be("src/app.ts");
        absolute.Should().Be("src/nested/file.ts");
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }

    private static (string Severity, string Code, string FilePath, int? Line, int? Column, string Source) ReadFinding(object finding)
    {
        var type = finding.GetType();
        return (
            (string)type.GetProperty("Severity")!.GetValue(finding)!,
            (string)type.GetProperty("Code")!.GetValue(finding)!,
            (string)type.GetProperty("FilePath")!.GetValue(finding)!,
            (int?)type.GetProperty("Line")!.GetValue(finding),
            (int?)type.GetProperty("Column")!.GetValue(finding),
            (string)type.GetProperty("Source")!.GetValue(finding)!);
    }

    private static T InvokePrivate<T>(string methodName, params object?[] arguments)
    {
        var method = typeof(DiagnosticsCommand).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"private method {methodName} should exist");
        return (T)method!.Invoke(null, arguments)!;
    }
}
