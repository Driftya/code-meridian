using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindImpactIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindImpactAsync_ForKnownNode_ReturnsAtLeastOneCallerOrGuidance()
    {
        var target = await FindAnyTargetAsync();
        target.Should().NotBeNull("the test seeds an isolated baseline graph");

        var impact = await _repository!.FindImpactAsync(target!.Id, depth: 2);

        impact.Should().NotBeNull();
    }

    [Fact]
    public async Task FindImpactAsync_ForInterfaceTarget_IncludesImplementerConsumers()
    {
        var projectContext = $"Integration.Impact.Interface.{Guid.NewGuid():N}";
        var contract = CreateNode(
            id: $"{projectContext}.IOrderWorkflow",
            name: "IOrderWorkflow",
            type: CodeNodeType.Interface,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderWorkflow.cs");
        var contractMember = CreateNode(
            id: $"{projectContext}.IOrderWorkflow.Run",
            name: "IOrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderWorkflow.cs");
        var implementation = CreateNode(
            id: $"{projectContext}.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs");
        var implementationMember = CreateNode(
            id: $"{projectContext}.OrderWorkflow.Run",
            name: "OrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs");
        var consumer = CreateNode(
            id: $"{projectContext}.CheckoutCoordinator.Run",
            name: "CheckoutCoordinator.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CheckoutCoordinator.cs");

        try
        {
            await _repository!.UpsertNodeAsync(contract);
            await _repository.UpsertNodeAsync(contractMember);
            await _repository.UpsertNodeAsync(implementation);
            await _repository.UpsertNodeAsync(implementationMember);
            await _repository.UpsertNodeAsync(consumer);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = contract.Id,
                TargetId = contractMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = implementation.Id,
                TargetId = implementationMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = implementation.Id,
                TargetId = contract.Id,
                Type = CodeEdgeType.Implements
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = consumer.Id,
                TargetId = implementationMember.Id,
                Type = CodeEdgeType.Calls
            });

            var impact = await _repository.FindImpactAsync(contract.Id, depth: 3);

            impact.Should().Contain(item => item.Node.Id == implementation.Id && item.Distance == 1);
            impact.Should().Contain(item => item.Node.Id == consumer.Id && item.Distance >= 2);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindImpactAsync_ForClassTarget_IncludesAbstractionConsumers()
    {
        var projectContext = $"Integration.Impact.Class.{Guid.NewGuid():N}";
        var contract = CreateNode(
            id: $"{projectContext}.IOrderWorkflow",
            name: "IOrderWorkflow",
            type: CodeNodeType.Interface,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderWorkflow.cs");
        var contractMember = CreateNode(
            id: $"{projectContext}.IOrderWorkflow.Run",
            name: "IOrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/IOrderWorkflow.cs");
        var implementation = CreateNode(
            id: $"{projectContext}.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs");
        var implementationMember = CreateNode(
            id: $"{projectContext}.OrderWorkflow.Run",
            name: "OrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs");
        var consumer = CreateNode(
            id: $"{projectContext}.CheckoutCoordinator.Run",
            name: "CheckoutCoordinator.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CheckoutCoordinator.cs");

        try
        {
            await _repository!.UpsertNodeAsync(contract);
            await _repository.UpsertNodeAsync(contractMember);
            await _repository.UpsertNodeAsync(implementation);
            await _repository.UpsertNodeAsync(implementationMember);
            await _repository.UpsertNodeAsync(consumer);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = contract.Id,
                TargetId = contractMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = implementation.Id,
                TargetId = implementationMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = implementation.Id,
                TargetId = contract.Id,
                Type = CodeEdgeType.Implements
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = consumer.Id,
                TargetId = contractMember.Id,
                Type = CodeEdgeType.Calls
            });

            var impact = await _repository.FindImpactAsync(implementation.Id, depth: 3);

            var consumerImpact = impact.Should().ContainSingle(item =>
                item.Node.Id == consumer.Id
                && item.Distance >= 2).Subject;
            consumerImpact.Node.Properties.TryGetValue("impactEvidenceBucket", out var bucket).Should().BeTrue();
            bucket.Should().Be("dependency");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindImpactAsync_WithFrontendConceptFixture_ReturnsMarkupAndStylesheetDependents()
    {
        var projectContext = $"Integration.Impact.Frontend.{Guid.NewGuid():N}";
        var classConcept = CreateNode(
            id: $"{projectContext}.CssClass.hero",
            name: "hero",
            type: CodeNodeType.ExternalConcept,
            projectContext: projectContext,
            filePath: string.Empty,
            properties: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
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

        try
        {
            await _repository!.UpsertNodeAsync(classConcept);
            await _repository.UpsertNodeAsync(component);
            await _repository.UpsertNodeAsync(selector);
            await _repository.UpsertNodeAsync(stylesheet);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = component.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = selector.Id,
                TargetId = classConcept.Id,
                Type = CodeEdgeType.UsesClass
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = stylesheet.Id,
                TargetId = selector.Id,
                Type = CodeEdgeType.DefinesSelector
            });

            var impact = await _repository.FindImpactAsync(classConcept.Id, depth: 3);

            impact.Should().Contain(item => item.Node.Id == component.Id && item.Distance == 1);
            impact.Should().Contain(item => item.Node.Id == selector.Id && item.Distance == 1);
            impact.Should().Contain(item => item.Node.Id == stylesheet.Id && item.Distance == 2);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
