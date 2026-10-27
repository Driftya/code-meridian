using System.Text;
using FluentAssertions;

namespace CodeMeridian.PowerShellIndexer.Tests;

public sealed class PowerShellExtractionTests
{
    [Fact]
    public async Task FunctionsFiltersLexicalBodiesAdvancedParametersAndPipelinesHaveExactSourceSpans()
    {
        using var workspace = new PowerShellWorkspace();
        const string source = """
            param([string]$Root = 'default')
            # Get-Comment is not a command.
            function Parent([int]$Number) {
              function Child { param([Parameter(Mandatory)][string[]]$Names) $Names | Write-Output }
              Child -Names 'a'
            }
            filter Pass { $_ | Write-Output }
            Parent 1
            """;
        workspace.Write("script.ps1", source);
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        file.ParseErrorCount.Should().Be(0);
        file.Bodies.Select(body => body.Kind).Should().Equal("script", "function", "function", "filter");
        file.Bodies[0].Parameters.Single().HasDefault.Should().BeTrue();
        file.Bodies[1].Parameters.Single().TypeConstraints.Should().Equal("int");
        file.Bodies[2].ParentId.Should().Be(file.Bodies[1].Id);
        file.Bodies[2].Parameters.Single().TypeConstraints.Should().Equal("string[]");
        file.Bodies[2].Snippet.Should().StartWith("function Child");
        foreach (var body in file.Bodies)
            source.Substring(body.Span.Offset, body.Span.Length).Should().StartWith(body.Snippet);
        file.Commands.Should().NotContain(command => command.Name == "Get-Comment");
        file.Commands.Where(command => command.Name == "Write-Output").Should().HaveCount(2);
        var parentCall = file.Commands.Single(command => command.Name == "Parent");
        source.Substring(parentCall.Span.Offset, parentCall.Span.Length).Should().Be("Parent 1");
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8-bom")]
    [InlineData("utf16-le")]
    [InlineData("utf16-be")]
    [InlineData("utf32-le")]
    [InlineData("utf32-be")]
    public async Task EncodingPolicyPreservesUnicodeAndRawSourceIdentity(string encodingName)
    {
        using var workspace = new PowerShellWorkspace();
        var encoding = encodingName switch
        {
            "utf8" => new UTF8Encoding(false), "utf8-bom" => new UTF8Encoding(true),
            "utf16-le" => (Encoding)new UnicodeEncoding(false, true), "utf16-be" => new UnicodeEncoding(true, true),
            "utf32-le" => new UTF32Encoding(false, true), _ => new UTF32Encoding(true, true)
        };
        var path = workspace.Write("é script.ps1", "function Get-É { 'こんにちは' }; Get-É", encoding);
        var file = (await workspace.Analyze("é script.ps1")).Files.Single();
        file.Status.Should().Be("complete");
        file.Bodies[1].Name.Should().Be("Get-É");
        file.SourceFingerprint.Should().Be("sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))));
        file.Commands.Single().BindingStatus.Should().Be("resolved");
    }

    [Fact]
    public async Task HereStringsCommentsAndQuotedTextDoNotManufactureCommands()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("script.ps1", """
            <# function Fake { Remove-Item '/tmp/side-effect' } #>
            $help = @'
            Invoke-Expression 'side-effect'
            function Fake {}
            '@
            'Write-Host fake'
            Write-Output $help
            """);
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        file.Bodies.Should().ContainSingle();
        file.Commands.Should().ContainSingle().Which.Name.Should().Be("Write-Output");
    }

    [Fact]
    public async Task ExactParseDiagnosticCodesMessagesAndLocationsAreRetainedWithBoundedSamples()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("broken.ps1", "function Broken {\n");
        var file = (await workspace.Analyze("broken.ps1")).Files.Single();
        file.Status.Should().Be("partial");
        file.ParseErrorCount.Should().BeGreaterThan(0);
        file.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "MissingEndCurlyBrace"
            && diagnostic.Message.Contains("Missing closing '}'") && diagnostic.Span!.Line == 1 && diagnostic.Span.Column == 17);
        file.Commands.Should().NotContain(command => command.BindingStatus == "resolved");
        workspace.Write("many.ps1", string.Concat(Enumerable.Repeat("function {\n", 50)));
        var bounded = (await workspace.Analyze("many.ps1")).Files.Single();
        bounded.Diagnostics.Count.Should().BeLessThanOrEqualTo(32);
        bounded.DiagnosticCount.Should().BeGreaterThan(bounded.Diagnostics.Count);
    }

    [Fact]
    public async Task UnsupportedClassesAndDeferredScriptblocksRemainExplicitAndSnippetsAreBounded()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("script.ps1", "class Example { [void] Run() { Write-Output 'x' } }; function Long { #" + new string('x', 3000) + "\n }; & { Long }");
        var file = (await workspace.Analyze("script.ps1")).Files.Single();
        file.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == "unsupported_construct");
        file.Bodies.Should().OnlyContain(body => body.Snippet.Length <= 2000);
        file.Commands.Where(command => command.Deferred).Should().OnlyContain(command => command.BindingStatus == "indeterminate");
        file.Status.Should().Be("partial");
    }
}
