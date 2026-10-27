using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.PowerShellIndexer.Tests;

public sealed class PowerShellIdentityTests
{
    [Fact]
    public async Task DeclarationIdsSurviveLineMovesUseLexicalScopeAndDistinguishDuplicates()
    {
        using var workspace = new PowerShellWorkspace();
        const string source = "function Same {}; function Same {}; function Parent { function Same {} }; filter Same {}";
        workspace.Write("script.ps1", source);
        var first = (await workspace.Analyze("script.ps1")).Files.Single();
        workspace.Write("script.ps1", "# leading comment\n\n" + source);
        var moved = (await workspace.Analyze("script.ps1")).Files.Single();
        moved.Bodies.Select(body => body.Id).Should().Equal(first.Bodies.Select(body => body.Id));
        first.Bodies.Select(body => body.Id).Should().OnlyHaveUniqueItems();
        moved.Bodies[1].Span.Line.Should().NotBe(first.Bodies[1].Span.Line);
        workspace.Write("other.ps1", source);
        var other = (await workspace.Analyze("other.ps1")).Files.Single();
        other.Bodies.Select(body => body.Id).Should().NotIntersectWith(first.Bodies.Select(body => body.Id));
    }

    [Fact]
    public async Task GraphMappingRetainsCallableAndFileSemanticsWithoutCreatingAClassOrUnknownEdges()
    {
        using var workspace = new PowerShellWorkspace();
        workspace.Write("helper.ps1", "function Work {};");
        workspace.Write("main.ps1", ". \"$PSScriptRoot/helper.ps1\"; Work; Write-Output 'x'");
        var report = await workspace.Analyze("helper.ps1", "main.ps1");
        var (nodes, edges) = PowerShellGraphMapper.Map(report);
        nodes.Should().OnlyContain(node => node.ProjectContext == "Fixture" && node.Id.StartsWith("Fixture::") && node.Namespace == null);
        nodes.Where(node => node.Type == CodeNodeType.Method).Should().HaveCount(3);
        nodes.Should().NotContain(node => node.Type == CodeNodeType.Class);
        nodes.Where(node => node.Type == CodeNodeType.Method).Should().OnlyContain(node => node.SourceSnippet != null);
        edges.Where(edge => edge.Type == CodeEdgeType.Calls).Should().ContainSingle();
        edges.Where(edge => edge.Type == CodeEdgeType.DependsOn).Should().ContainSingle().Which.Properties["powershellInvocation"].Should().Be("dot_source");
        edges.Should().OnlyContain(edge => edge.ValidateEvidence() == null && edge.Properties["analyzer"] == "powershell");
        var ids = nodes.Select(node => node.Id).ToArray();
        edges.Should().OnlyContain(edge => ids.Contains(edge.SourceId) && ids.Contains(edge.TargetId));
        report.Coverage.Should().Be("unknown");
    }
}
