using FluentAssertions;

namespace CodeMeridian.PowerShellIndexer.Tests;

public sealed class PowerShellBindingTests
{
    [Theory]
    [InlineData("function Foo {}; fOO", "fOO", "resolved", "lexical_definition")]
    [InlineData("Foo; function Foo {}", "Foo", "unresolved_local", "definition_not_yet_established")]
    [InlineData("function Foo {}; function Foo {}; Foo", "Foo", "indeterminate", "conditional_or_redefined_name")]
    [InlineData("if ($true) { function Foo {} }; Foo", "Foo", "indeterminate", "conditional_or_redefined_name")]
    [InlineData("function Foo { Foo }; Foo", "Foo", "resolved", "lexical_definition")]
    [InlineData("Write-Output 'x'", "Write-Output", "external_unindexed", "no_supported_repository_definition")]
    [InlineData("function Foo {}; Set-Alias Foo Write-Output; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function Foo {}; $function:Foo = {}; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function Foo {}; & $command; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function script:Foo {}; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function Foo {}; Remove-Module LocalModule; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function Foo {}; ipmo LocalModule; Foo", "Foo", "indeterminate", "dynamic_resolution")]
    [InlineData("function Foo {}; Get-*", "Get-*", "indeterminate", "dynamic_resolution")]
    public async Task StaticNameBindingDistinguishesPositiveUnresolvedDynamicAndExternalEvidence(string source, string name, string status, string reason)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("script.ps1", source);
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        var commands = file.Commands.Where(command => command.Name == name).ToArray();
        commands.Should().NotBeEmpty();
        commands.Should().OnlyContain(command => command.BindingStatus == status && command.Reason == reason);
        if (status != "resolved") commands.Should().OnlyContain(command => command.TargetId == null);
    }

    [Fact]
    public async Task NearestLexicalDefinitionsShadowParentsAndNestedDefinitionsAreNotGloballyVisible()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("script.ps1", "function Helper {}; function Parent { function Helper {}; Helper }; Helper; function Hidden { function Secret {} }; Secret");
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        var helpers = file.Bodies.Where(body => body.Name == "Helper").ToArray();
        file.Commands.Where(command => command.Name == "Helper").Select(command => command.TargetId).Should().Equal(helpers[1].Id, helpers[0].Id);
        file.Commands.Single(command => command.Name == "Secret").Reason.Should().Be("definition_outside_visible_scope");
    }

    [Fact]
    public async Task SameNamedDefinitionsInUnrelatedFilesDoNotBecomeCandidates()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("one.ps1", "function Foo {};");
        workspace.Write("two.ps1", "Foo");
        var file = (await workspace.Analyze("one.ps1", "two.ps1")).Files.Single(file => file.Path == "two.ps1");
        file.Commands.Single().BindingStatus.Should().Be("external_unindexed");
    }

    [Fact]
    public async Task SupportedRootPathsAndDotSourcesUseSelectedFilesAndExposeOnlyProvenDefinitions()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("scripts/helpers.ps1", "function Helper {};");
        workspace.Write("scripts/main.ps1", ". \"$PSScriptRoot/helpers.ps1\"; Helper; & \"$PSScriptRoot/helpers.ps1\"");
        var report = await workspace.Analyze("scripts/helpers.ps1", "scripts/main.ps1");
        var helper = report.Files[0];
        var main = report.Files[1];
        main.Commands.Select(command => command.BindingStatus).Should().OnlyContain(status => status == "resolved");
        main.Commands[0].TargetId.Should().Be(helper.FileId);
        main.Commands[1].TargetId.Should().Be(helper.Bodies[1].Id);
        main.Commands[2].TargetId.Should().Be(helper.Bodies[0].Id);
    }

    [Theory]
    [InlineData(". './helpers.ps1'", "runtime_working_directory_or_qualified_command")]
    [InlineData("& './helpers.ps1'", "runtime_working_directory_or_qualified_command")]
    [InlineData("& \"$PSScriptRoot/../../outside.ps1\"", "unsupported_or_outside_path")]
    [InlineData("& \"$PSScriptRoot/missing.ps1\"", "target_not_in_successful_selection")]
    public async Task RuntimeRelativeOutsideAndMissingPathsAreNotInventedTargets(string source, string reason)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("main.ps1", source);
        var command = (await workspace.Analyze("main.ps1")).Files.Single().Commands.Single();
        command.BindingStatus.Should().NotBe("resolved");
        command.TargetId.Should().BeNull();
        command.Reason.Should().Be(reason);
    }

    [Fact]
    public async Task UnverifiedDotSourceAndModulesPreventFalseLocalResolution()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("main.ps1", "function Foo {}; . './unknown.ps1'; Foo");
        var file = (await workspace.Analyze("main.ps1")).Files.Single();
        file.Commands.Last().BindingStatus.Should().Be("indeterminate");
        file.Commands.Last().Reason.Should().Be("unverified_import_visibility");
    }

    [Fact]
    public async Task ReassignedScriptRootCannotAuthorizeAStaticSourcePath()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("helper.ps1", "function Help {}");
        workspace.Write("main.ps1", "$PSScriptRoot = '/other'; & \"$PSScriptRoot/helper.ps1\"");
        var file = (await workspace.Analyze("helper.ps1", "main.ps1")).Files[1];
        file.Commands.Single().BindingStatus.Should().Be("indeterminate");
        file.Commands.Single().TargetId.Should().BeNull();
    }
}
