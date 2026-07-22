using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jCodeGraphRepositoryFindGodClassesIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task FindGodClassesAsync_WithLargeClassAndHighFanIn_ReturnsThatClass()
    {
        var projectContext = $"Integration.GodClasses.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Production")
        with
        {
            LineCount = 360
        };
        var targetMember = CreateNode(
            id: $"{projectContext}.OrderWorkflow.Run",
            name: "OrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs",
            namespaceName: $"{projectContext}.Production")
        with
        {
            LineCount = 40
        };
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs",
            namespaceName: $"{projectContext}.Production");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs",
            namespaceName: $"{projectContext}.Production");
        var callerThree = CreateNode(
            id: $"{projectContext}.CallerThree",
            name: "CallerThree",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerThree.cs",
            namespaceName: $"{projectContext}.Production");
        var callerFour = CreateNode(
            id: $"{projectContext}.CallerFour",
            name: "CallerFour",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerFour.cs",
            namespaceName: $"{projectContext}.Production");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(targetMember);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);
            await _repository.UpsertNodeAsync(callerThree);
            await _repository.UpsertNodeAsync(callerFour);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = target.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerThree.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerFour.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3);

            results.Should().ContainSingle(item =>
                item.Node.Id == target.Id &&
                item.LineCount == 360 &&
                item.FanIn >= 4);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_WithStoredConfigurationRole_ExcludesThatClass()
    {
        var projectContext = $"Integration.GodClasses.Config.{Guid.NewGuid():N}";
        var target = CreateNode(
            id: $"{projectContext}.AppConfig",
            name: "AppConfig",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 360,
            FileRole = IndexedFileRole.Configuration
        };
        var targetMember = CreateNode(
            id: $"{projectContext}.AppConfig.Load",
            name: "AppConfig.Load",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/AppConfig.ts")
        with
        {
            LineCount = 40,
            FileRole = IndexedFileRole.Configuration
        };
        var callerOne = CreateNode(
            id: $"{projectContext}.CallerOne",
            name: "CallerOne",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerOne.cs");
        var callerTwo = CreateNode(
            id: $"{projectContext}.CallerTwo",
            name: "CallerTwo",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerTwo.cs");
        var callerThree = CreateNode(
            id: $"{projectContext}.CallerThree",
            name: "CallerThree",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerThree.cs");
        var callerFour = CreateNode(
            id: $"{projectContext}.CallerFour",
            name: "CallerFour",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/CallerFour.cs");

        try
        {
            await _repository!.UpsertNodeAsync(target);
            await _repository.UpsertNodeAsync(targetMember);
            await _repository.UpsertNodeAsync(callerOne);
            await _repository.UpsertNodeAsync(callerTwo);
            await _repository.UpsertNodeAsync(callerThree);
            await _repository.UpsertNodeAsync(callerFour);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = target.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerOne.Id,
                TargetId = target.Id,
                Type = CodeEdgeType.DependsOn
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerTwo.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Calls
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerThree.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.Uses
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = callerFour.Id,
                TargetId = targetMember.Id,
                Type = CodeEdgeType.DependsOn
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 3);

            results.Should().NotContain(item => item.Node.Id == target.Id);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_DistinguishesFanInPerCandidateClass()
    {
        var projectContext = $"Integration.GodClasses.FanIn.{Guid.NewGuid():N}";
        var primary = CreateNode(
            id: $"{projectContext}.Primary",
            name: "PrimaryService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/PrimaryService.cs")
        with
        {
            LineCount = 360
        };
        var primaryMember = CreateNode(
            id: $"{projectContext}.Primary.Run",
            name: "PrimaryService.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/PrimaryService.cs");
        var secondary = CreateNode(
            id: $"{projectContext}.Secondary",
            name: "SecondaryService",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryService.cs")
        with
        {
            LineCount = 420
        };
        var secondaryMember = CreateNode(
            id: $"{projectContext}.Secondary.Run",
            name: "SecondaryService.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryService.cs");
        var primaryCallers = Enumerable.Range(1, 4)
            .Select(index => CreateNode(
                id: $"{projectContext}.PrimaryCaller{index}",
                name: $"PrimaryCaller{index}",
                type: CodeNodeType.Method,
                projectContext: projectContext,
                filePath: $"src/{projectContext}/PrimaryCaller{index}.cs"))
            .ToArray();
        var secondaryCaller = CreateNode(
            id: $"{projectContext}.SecondaryCaller",
            name: "SecondaryCaller",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/SecondaryCaller.cs");

        try
        {
            await _repository!.UpsertNodeAsync(primary);
            await _repository.UpsertNodeAsync(primaryMember);
            await _repository.UpsertNodeAsync(secondary);
            await _repository.UpsertNodeAsync(secondaryMember);
            foreach (var caller in primaryCallers)
                await _repository.UpsertNodeAsync(caller);
            await _repository.UpsertNodeAsync(secondaryCaller);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primary.Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = secondary.Id,
                TargetId = secondaryMember.Id,
                Type = CodeEdgeType.Contains
            });

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[0].Id,
                TargetId = primary.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[1].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Calls
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[2].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = primaryCallers[3].Id,
                TargetId = primaryMember.Id,
                Type = CodeEdgeType.DependsOn
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = secondaryCaller.Id,
                TargetId = secondary.Id,
                Type = CodeEdgeType.Calls
            });

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 0);

            results.Should().Contain(item =>
                item.Node.Id == primary.Id &&
                item.FanIn == 4);
            results.Should().Contain(item =>
                item.Node.Id == secondary.Id &&
                item.FanIn == 1);
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }

    [Fact]
    public async Task FindGodClassesAsync_PrefersDirectAndMemberCallerQualityOverHeuristicVolume()
    {
        var projectContext = $"Integration.GodClasses.Quality.{Guid.NewGuid():N}";
        var directHeavy = CreateNode(
            id: $"{projectContext}.OrderWorkflow",
            name: "OrderWorkflow",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs")
        with
        {
            LineCount = 360
        };
        var directHeavyMember = CreateNode(
            id: $"{projectContext}.OrderWorkflow.Run",
            name: "OrderWorkflow.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflow.cs");
        var weakBroad = CreateNode(
            id: $"{projectContext}.BroadCoordinator",
            name: "BroadCoordinator",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/BroadCoordinator.cs")
        with
        {
            LineCount = 420
        };
        var weakBroadMember = CreateNode(
            id: $"{projectContext}.BroadCoordinator.Run",
            name: "BroadCoordinator.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/BroadCoordinator.cs");
        var directCaller = CreateNode(
            id: $"{projectContext}.OrderController",
            name: "OrderController",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderController.cs");
        var memberCaller = CreateNode(
            id: $"{projectContext}.Checkout.Run",
            name: "Checkout.Run",
            type: CodeNodeType.Method,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/Checkout.cs");
        var dependencyCaller = CreateNode(
            id: $"{projectContext}.OrderWorkflowFactory",
            name: "OrderWorkflowFactory",
            type: CodeNodeType.Class,
            projectContext: projectContext,
            filePath: $"src/{projectContext}/OrderWorkflowFactory.cs");
        var heuristicCallers = Enumerable.Range(1, 4)
            .Select(index => CreateNode(
                id: $"{projectContext}.Endpoint{index}",
                name: $"POST /api/orders/{index}",
                type: CodeNodeType.ApiEndpoint,
                projectContext: projectContext,
                filePath: $"src/{projectContext}/OrdersEndpoint{index}.cs"))
            .ToArray();

        try
        {
            await _repository!.UpsertNodeAsync(directHeavy);
            await _repository.UpsertNodeAsync(directHeavyMember);
            await _repository.UpsertNodeAsync(weakBroad);
            await _repository.UpsertNodeAsync(weakBroadMember);
            await _repository.UpsertNodeAsync(directCaller);
            await _repository.UpsertNodeAsync(memberCaller);
            await _repository.UpsertNodeAsync(dependencyCaller);
            foreach (var heuristicCaller in heuristicCallers)
                await _repository.UpsertNodeAsync(heuristicCaller);

            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = directHeavy.Id,
                TargetId = directHeavyMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = weakBroad.Id,
                TargetId = weakBroadMember.Id,
                Type = CodeEdgeType.Contains
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = directCaller.Id,
                TargetId = directHeavy.Id,
                Type = CodeEdgeType.Uses
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = memberCaller.Id,
                TargetId = directHeavyMember.Id,
                Type = CodeEdgeType.Calls
            });
            await _repository.UpsertEdgeAsync(new CodeEdge
            {
                SourceId = dependencyCaller.Id,
                TargetId = directHeavy.Id,
                Type = CodeEdgeType.DependsOn
            });
            foreach (var heuristicCaller in heuristicCallers)
            {
                await _repository.UpsertEdgeAsync(new CodeEdge
                {
                    SourceId = heuristicCaller.Id,
                    TargetId = weakBroad.Id,
                    Type = CodeEdgeType.Uses
                });
            }

            var results = await _repository.FindGodClassesAsync(projectContext, lineThreshold: 300, fanInThreshold: 0);

            results.Should().NotBeEmpty();
            results[0].Node.Id.Should().Be(directHeavy.Id);
            results[0].Node.Properties.Should().ContainKey("godClassDirectCallerCount");
            results[0].Node.Properties["godClassDirectCallerCount"].Should().Be("1");
            results[0].Node.Properties["godClassMemberCallerCount"].Should().Be("1");
            results[0].Node.Properties["godClassDependencyCallerCount"].Should().Be("1");
            results[0].Node.Properties["godClassHeuristicCallerCount"].Should().Be("0");
        }
        finally
        {
            await _repository!.DeleteProjectAsync(projectContext);
        }
    }


}
