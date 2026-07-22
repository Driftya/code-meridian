using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryGetContextForEditingIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task GetContextForEditingAsync_ForKnownNode_ReturnsTheNode()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the test seeds an isolated baseline graph");

        var context = await _repository!.GetContextForEditingAsync(target!.Id);

        context.Node.Should().NotBeNull();
        context.Node!.Id.Should().Be(target.Id);
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithFrontendFixture_ReturnsImportingFilesAndDefinedSelectors()
    {
        var projectContext = $"Integration.Context.Frontend.{Guid.NewGuid():N}";
        var stylesheet = CreateNode(
            id: $"{projectContext}.Stylesheet",
            name: "HeroCard.scss",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.scss",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "StyleSheetFile"
            });
        var component = CreateNode(
            id: $"{projectContext}.Component",
            name: "HeroCard.tsx",
            type: CodeNodeType.File,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/HeroCard.tsx",
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["frontendRole"] = "ComponentFile"
            });
        var selector = CreateNode(
            id: $"{projectContext}.CssSelector.hero",
            name: ".hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            });

        try
        {
            await _repository!.UpsertNodeAsync(stylesheet);
            await _repository.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(selector);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = stylesheet.Id,
                Type = CodeEdgeType.ImportsStyle
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var context = await _repository.GetContextForEditingAsync(stylesheet.Id);

            context.Node.Should().NotBeNull();
            context.Callers.Should().Contain(node => node.Id == component.Id);
            context.Callees.Should().Contain(node => node.Id == selector.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
