using System.Management.Automation.Language;
using System.Reflection;
using CodeMeridian.PowerShellIndexer;
using FluentAssertions;

namespace CodeMeridian.PowerShellIndexer.Tests;

public sealed class PowerShellSafetyTests
{
    [Fact]
    public void SyntaxOnlyParsingDoesNotCreateAUsingModuleRunspaceOnTheParsingThread()
    {
        var resolver = typeof(Parser).Assembly.GetType("System.Management.Automation.Language.SymbolResolver")!;
        var field = resolver.GetField("t_usingStatementResolvePowerShell", BindingFlags.Static | BindingFlags.NonPublic)!;
        field.GetValue(null).Should().BeNull();
        var file = PowerShellParser.Parse("Fixture", "script.ps1", "using module DefinitelyNotInstalled\nWrite-Output 1", "sha256:" + new string('a', 64));
        file.ParseErrorCount.Should().Be(0);
        file.Modules.Should().ContainSingle();
        field.GetValue(null).Should().BeNull();
    }

    [Fact]
    public async Task SourceModulesUsingDirectivesManifestsAndParameterDefaultsNeverExecute()
    {
        using var workspace = new PowerShellWorkspace();
        var marker = Path.Combine(workspace.Root.FullName, "executed.txt").Replace("'", "''", StringComparison.Ordinal);
        var effect = $"[System.IO.File]::WriteAllText('{marker}', 'executed')";
        workspace.Write("evil.psm1", effect + "; function Evil {}");
        workspace.Write("evil.psd1", "@{ RootModule = 'evil.psm1'; PrivateData = (" + effect + ") }");
        workspace.Write("assembly.ps1", "using assembly './missing.dll'\n" + effect);
        workspace.Write("main.ps1", "using module './evil.psm1'\nparam($Value = $(" + effect + "))\n"
            + effect + "; Import-Module \"$PSScriptRoot/evil.psm1\"; . \"$PSScriptRoot/evil.psm1\"; Invoke-Expression '" + effect.Replace("'", "''") + "'");
        var report = await workspace.Analyze("main.ps1", "evil.psm1", "evil.psd1", "assembly.ps1");
        File.Exists(marker).Should().BeFalse();
        report.Files.Single(file => file.Path == "evil.psd1").Manifest!.Status.Should().Be("indeterminate");
        report.Worker.ParseMode.Should().Be("module_analysis_syntax_only");
        report.Files.Single(file => file.Path == "main.ps1").Modules.Should().Contain(module => module.Kind == "using");
        var resolver = typeof(Parser).Assembly.GetType("System.Management.Automation.Language.SymbolResolver")!;
        resolver.GetField("t_usingStatementResolvePowerShell", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)
            .Should().BeNull("syntax-only parsing must not create the public parser's using-module runspace");
    }

    [Theory]
    [InlineData("configuration Example { Node localhost {} }")]
    [InlineData("\"$(configuration Example { Node localhost {} })\"")]
    [InlineData("\"$(`u{63}onfiguration Example { Node localhost {} })\"")]
    [InlineData("# configuration words in comments also conservatively stop this pass")]
    public async Task DscIsRejectedBeforeAnyNativeResourceOrRunspaceProcessing(string source)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("script.ps1", source);
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        file.Status.Should().Be("partial");
        file.Bodies.Should().BeEmpty();
        file.Commands.Should().BeEmpty();
        file.Diagnostics.Should().ContainSingle().Which.Code.Should().Be("configuration_unsupported");
    }

    [Theory]
    [InlineData("../outside.ps1")]
    [InlineData("/absolute.ps1")]
    [InlineData("folder/../script.ps1")]
    [InlineData("script.txt")]
    public async Task InvalidSourceBoundariesAreRejectedBeforeReading(string path)
    {
        using var workspace = new PowerShellWorkspace();
        await FluentActions.Awaiting(() => workspace.Analyze(path)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task MissingFilesInvalidEncodingAndCancelledRunsCannotRetainFacts()
    {
        using var workspace = new PowerShellWorkspace();
        File.WriteAllBytes(Path.Combine(workspace.Root.FullName, "binary.ps1"), [0xff, 0xff]);
        var report = await workspace.Analyze("binary.ps1", "missing.ps1");
        report.Status.Should().Be("partial");
        report.Files.Should().OnlyContain(file => file.Status == "failed" && file.Bodies.Count == 0 && file.Commands.Count == 0);
        report.Files.Single(file => file.Path == "missing.ps1").SourceFingerprint.Should().BeNull();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await FluentActions.Awaiting(() => new PowerShellAnalyzer().AnalyzeAsync(new("Fixture", workspace.Root.FullName, true, []), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SourceByteNestingFactAndRequestBudgetsFailWithoutTruncatedSuccessfulFacts()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("deep.ps1", new string('(', 129) + "1" + new string(')', 129));
        workspace.Write("large.ps1", new string('x', 1048577));
        workspace.Write("many.ps1", string.Concat(Enumerable.Repeat("Write-Output 1;", 1001)));
        var report = await workspace.Analyze("deep.ps1", "large.ps1", "many.ps1");
        report.Files.Should().OnlyContain(file => file.Status != "complete" && file.Bodies.Count == 0 && file.Commands.Count == 0);
        await FluentActions.Awaiting(() => new PowerShellAnalyzer().AnalyzeAsync(new("Fixture", workspace.Root.FullName, null, [])))
            .Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => new PowerShellAnalyzer().AnalyzeAsync(new("Fixture", workspace.Root.FullName, true, Enumerable.Repeat("a.ps1", 1001).ToArray())))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("parameters", "parameter_budget_exceeded")]
    [InlineData("characters", "parse_budget_exceeded")]
    public async Task ParameterAndDecodedCharacterBudgetsAreExplicitRatherThanSilentTruncation(string budget, string code)
    {
        using var workspace = new PowerShellWorkspace();
        var source = budget == "parameters"
            ? "function Many { param(" + string.Join(',', Enumerable.Range(0, 129).Select(index => "$p" + index)) + ") }"
            : "#" + new string('x', PowerShellParser.MaximumCharacters);
        workspace.Write("script.ps1", source);
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        file.Status.Should().Be("partial");
        file.Bodies.Should().BeEmpty();
        file.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(code);
    }

    [Theory]
    [InlineData(true, "unresolved_local")]
    [InlineData(false, "resolved")]
    public async Task BindingUsesExplicitPathCaseRatherThanCommandNameCase(bool caseSensitive, string expected)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("helper.ps1", "function Help {}");
        workspace.Write("main.ps1", "& \"$PSScriptRoot/HELPER.PS1\"");
        var report = await new PowerShellAnalyzer().AnalyzeAsync(new("Fixture", workspace.Root.FullName, caseSensitive, ["helper.ps1", "main.ps1"]));
        report.Files.Single(file => file.Path == "main.ps1").Commands.Single().BindingStatus.Should().Be(expected);
    }

    [LinuxSymlinkFact]
    public async Task SymlinkSourcesCannotExpandTheExplicitAnalysisBoundary()
    {
        using var workspace = new PowerShellWorkspace();
        var target = workspace.Write("target.ps1", "function Hidden {}");
        var link = Path.Combine(workspace.Root.FullName, "linked.ps1");
        // Windows developer mode (or link privilege) and normal Linux symlink support are required.
        File.CreateSymbolicLink(link, target);
        var file = (await workspace.Analyze("linked.ps1")).Files.Single();
        file.Status.Should().Be("failed");
        file.Bodies.Should().BeEmpty();
    }
}
