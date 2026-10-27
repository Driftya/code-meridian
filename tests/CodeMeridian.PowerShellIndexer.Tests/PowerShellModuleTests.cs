using FluentAssertions;

namespace CodeMeridian.PowerShellIndexer.Tests;

public sealed class PowerShellModuleTests
{
    [Fact]
    public async Task LiteralImportExportsRestrictConsumerBindingsWithoutLoadingTheModule()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("tools.psm1", "function Public {}; function Private {}; Export-ModuleMember -Function Public");
        workspace.Write("main.ps1", "Import-Module \"$PSScriptRoot/tools.psm1\"; Public; Private");
        var report = await workspace.Analyze("main.ps1", "tools.psm1");
        var main = report.Files.Single(file => file.Path == "main.ps1");
        main.Modules.Single().BindingStatus.Should().Be("resolved");
        main.Commands.Single(command => command.Name == "Public").BindingStatus.Should().Be("resolved");
        main.Commands.Single(command => command.Name == "Private").BindingStatus.Should().Be("external_unindexed");
        main.Commands.Single(command => command.Name == "Public").TargetId.Should().Be(report.Files.Single(file => file.Path == "tools.psm1").Bodies[1].Id);
    }

    [Fact]
    public async Task LiteralManifestRootMetadataAndExportsFurtherRestrictTheRootModule()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("mods/tools.psm1", "function First {}; function Second {}; Export-ModuleMember -Function First,Second");
        workspace.Write("mods/tools.psd1", """
            @{ RootModule = 'tools.psm1'; ModuleVersion = '1.2.3'; GUID = '2ffb8801-7672-4166-9563-51c80f094f27';
               FunctionsToExport = @('First'); AliasesToExport = @(); CmdletsToExport = @();
               PrivateData = @{ Name = 'literal'; Enabled = $true }; Tags = @('one','two') }
            """);
        workspace.Write("main.ps1", "Import-Module \"$PSScriptRoot/mods/tools.psd1\"; First; Second");
        var report = await workspace.Analyze("main.ps1", "mods/tools.psd1", "mods/tools.psm1");
        var manifest = report.Files.Single(file => file.Manifest is not null).Manifest!;
        manifest.Status.Should().Be("literal");
        manifest.Metadata["ModuleVersion"].Should().Be("1.2.3");
        manifest.FunctionsToExport.Should().Equal("First");
        var main = report.Files[0];
        main.Modules.Single().BindingStatus.Should().Be("resolved");
        main.Commands.Single(command => command.Name == "First").BindingStatus.Should().Be("resolved");
        main.Commands.Single(command => command.Name == "Second").BindingStatus.Should().Be("external_unindexed");
    }

    [Theory]
    [InlineData("Export-ModuleMember -Function '*'", "exports_indeterminate")]
    [InlineData("Export-ModuleMember -Function $exports", "exports_indeterminate")]
    [InlineData("Export-ModuleMember -Function Public -Alias AliasName", "exports_indeterminate")]
    [InlineData("if ($true) { Export-ModuleMember -Function Public }", "exports_indeterminate")]
    public async Task UnsupportedExportVisibilityCannotProduceImportedCallEdges(string export, string code)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("tools.psm1", "function Public {}; " + export);
        workspace.Write("main.ps1", "Import-Module \"$PSScriptRoot/tools.psm1\"; Public");
        var report = await workspace.Analyze("main.ps1", "tools.psm1");
        report.Files[1].Diagnostics.Should().Contain(diagnostic => diagnostic.Code == code);
        report.Files[0].Modules.Single().BindingStatus.Should().Be("indeterminate");
        report.Files[0].Commands.Last().TargetId.Should().BeNull();
    }

    [Theory]
    [InlineData("@{RootModule='tools.psm1'; FunctionsToExport='*'}", "manifest_visibility_indeterminate")]
    [InlineData("@{RootModule='tools.psm1'; FunctionsToExport=$exports}", "manifest_not_literal")]
    [InlineData("@{RootModule='tools.psm1'; NestedModules=@('other')}", "manifest_visibility_indeterminate")]
    [InlineData("@{RootModule='tools.psm1'; Note=(Get-Content 'secret')}", "manifest_not_literal")]
    public async Task ComputedWildcardOrNestedManifestsRetainExplicitLimits(string content, string code)
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("tools.psd1", content);
        var file = (await workspace.Analyze("tools.psd1")).Files.Single();
        file.Manifest!.Status.Should().Be("indeterminate");
        file.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == code);
    }

    [Fact]
    public async Task UsingRequiresAndVersionedImportsRemainSourceFactsWithoutAssumingModuleSearchPaths()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("main.ps1", """
            #requires -Modules @{ModuleName='Required'; ModuleVersion='1.0'}
            using module LocalModule
            Import-Module RemoteModule
            Import-Module 'OtherModule' -RequiredVersion '2.0'
            Run-Unknown
            """);
        var file = (await workspace.Analyze("main.ps1")).Files.Single();
        file.Modules.Select(module => module.Kind).Should().BeEquivalentTo("requires", "using", "import", "import");
        file.Modules.Should().OnlyContain(module => module.BindingStatus == "indeterminate" && module.TargetId == null);
        file.Modules.Single(module => module.Kind == "requires").Target.Should().Be("Required");
        file.Modules.Single(module => module.Kind == "requires").Constraints["minimumVersion"].Should().Be("1.0");
        file.Modules.Single(module => module.Kind == "requires").Span.Line.Should().Be(1);
        file.Commands.Last().Reason.Should().Be("unverified_import_visibility");
    }

    [Fact]
    public async Task DuplicateImportedNamesAreAmbiguousWhileEmptyExportsAreLegitimate()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("one.psm1", "function Public {};");
        workspace.Write("two.psm1", "function Public {};");
        workspace.Write("empty.psm1", "function Hidden {}; Export-ModuleMember -Function @()");
        workspace.Write("main.ps1", "Import-Module \"$PSScriptRoot/one.psm1\"; Import-Module \"$PSScriptRoot/two.psm1\"; Public; Import-Module \"$PSScriptRoot/empty.psm1\"; Hidden");
        var file = (await workspace.Analyze("one.psm1", "two.psm1", "empty.psm1", "main.ps1")).Files.Single(file => file.Path == "main.ps1");
        file.Commands.Single(command => command.Name == "Public").Reason.Should().Be("multiple_imported_definitions");
        file.Commands.Single(command => command.Name == "Hidden").BindingStatus.Should().Be("external_unindexed");
        file.Modules.Should().OnlyContain(module => module.BindingStatus == "resolved");
    }

    [Fact]
    public async Task ShadowedImportCommandsDoNotManufactureModuleVisibility()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("tools.psm1", "function Public {};");
        workspace.Write("main.ps1", "function Import-Module {}; Import-Module \"$PSScriptRoot/tools.psm1\"; Public");
        var file = (await workspace.Analyze("main.ps1", "tools.psm1")).Files[0];
        file.Modules.Single().BindingStatus.Should().Be("indeterminate");
        file.Commands.Last().TargetId.Should().BeNull();
    }

    [Fact]
    public async Task ExecutableModuleInitializersCannotEstablishStaticExportVisibility()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("tools.psm1", "[System.GC]::Collect(); function Public {};");
        workspace.Write("main.ps1", "Import-Module \"$PSScriptRoot/tools.psm1\"; Public");
        var file = (await workspace.Analyze("main.ps1", "tools.psm1")).Files[0];
        file.Modules.Single().BindingStatus.Should().Be("indeterminate");
        file.Commands.Last().TargetId.Should().BeNull();
    }
}
