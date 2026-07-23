using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServicePlanEditRouteTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task PlanEditRouteAsync_WithGraphMatches_ReturnsOrderedItinerary()
    {
        var (sut, graph) = Build();
        var port = Node("i1", "IPaymentRepository", CodeNodeType.Interface, "src/Application/Ports/IPaymentRepository.cs", 1, "Shop");
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop");
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop");
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop");
        var program = Node("p1", "Program", CodeNodeType.File, "src/McpServer/Program.cs", 1, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop");

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, port, implementation, endpoint, program]);
        graph
            .GetContextForEditingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], [port]));
        graph
            .FindImpactAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1), (program, 2)]);
        graph
            .FindRelatedTestsAsync(Arg.Any<string>(), "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        result.Should().Contain("## Change Route");
        result.Should().Contain("Contract / port");
        result.Should().Contain("Application / domain behavior");
        result.Should().Contain("Infrastructure implementation");
        result.Should().Contain("Composition and API entry points");
        result.Should().Contain("Tests and verification");
        result.Should().Contain("IPaymentRepository");
        result.Should().Contain("PaymentService");
        result.Should().Contain("SqlPaymentRepository");
        result.Should().Contain("PaymentEndpoint");
        result.Should().Contain("PaymentServiceTests");
        result.Should().Contain("Graph signals");
    }

    [Fact]
    public async Task PlanEditRouteAsync_BehaviorGoalFollowsContractToConcreteMethodAndDirectTest()
    {
        var (sut, graph) = Build();
        var contract = Node(
            "contract-delete",
            "DeleteDiagnosticsAsync(string,CancellationToken)",
            CodeNodeType.Method,
            "src/Core/CodeGraph/ICodeGraphRepository.cs",
            20,
            "CodeMeridian");
        var implementation = Node(
            "implementation-delete",
            "DeleteDiagnosticsAsync(string,CancellationToken)",
            CodeNodeType.Method,
            "src/Infrastructure/Graph/Neo4jCodeGraphRepository.Diagnostics.cs",
            40,
            "CodeMeridian",
            sourceSnippet: "public async Task DeleteDiagnosticsAsync(string projectContext, CancellationToken cancellationToken)");
        var unrelated = Node(
            "unrelated-impact",
            "FindImpactPathsAsync(string,int,CancellationToken)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Analytics.Risk.cs",
            70,
            "CodeMeridian");
        var regression = Node(
            "test-delete",
            "DeleteDiagnosticsAsync_RemovesOnlyDiagnostics()",
            CodeNodeType.Method,
            "tests/CodeMeridian.Infrastructure.Integration.Tests/Neo4jCodeGraphRepositoryDiagnosticsTests.cs",
            15,
            "CodeMeridian",
            fileRole: IndexedFileRole.Source);

        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([contract, unrelated]);
        graph.FindImpactPathsAsync(contract.Id, 1, Arg.Any<CancellationToken>())
            .Returns([
                new ImpactPath(
                    implementation,
                    1,
                    [new GraphPathStep(contract, null, null), new GraphPathStep(implementation, "Implements", 1d)])
            ]);
        graph.GetContextForEditingAsync(implementation.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(implementation, [], [], []));
        graph.FindImpactAsync(implementation.Id, 2, Arg.Any<CancellationToken>()).Returns([]);
        graph.FindDownstreamAsync(implementation.Id, 2, Arg.Any<CancellationToken>()).Returns([]);
        graph.FindRelatedTestsAsync(implementation.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(regression, "direct")]);

        var result = await sut.PlanEditRouteAsync("delete diagnostics cleanup", projectContext: "CodeMeridian");

        result.Should().Contain("**Anchor:** `DeleteDiagnosticsAsync(string,CancellationToken)` (Method) - `src/Infrastructure/Graph/Neo4jCodeGraphRepository.Diagnostics.cs`");
        result.Should().Contain("DeleteDiagnosticsAsync_RemovesOnlyDiagnostics");
        result.Should().NotContain("Contract / port");
        result.Should().NotContain("FindImpactPathsAsync");
    }

    [Fact]
    public async Task PlanEditRouteAsync_PrefersProductionAnchorOverDocsAndTests()
    {
        var (sut, graph) = Build();
        var doc = Node("d1", "Add Payments Feature", CodeNodeType.File, "docs/features/add-payments.md", 1, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop", fileRole: IndexedFileRole.Test);
        var port = Node("i1", "IPaymentRepository", CodeNodeType.Interface, "src/Application/Ports/IPaymentRepository.cs", 1, "Shop");
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", fileRole: IndexedFileRole.Source);
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop", fileRole: IndexedFileRole.Source);
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop", fileRole: IndexedFileRole.Source);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([doc, test, service, port, implementation, endpoint]);
        graph
            .GetContextForEditingAsync(service.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], [port]));
        graph
            .FindImpactAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1)]);
        graph
            .FindRelatedTestsAsync(service.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        result.Should().Contain("**Anchor:** `PaymentService` (Class) - `src/Application/Payments/PaymentService.cs`");
        result.Should().Contain("Implementation candidates: 4");
        result.Should().Contain("PaymentServiceTests");
        result.Should().NotContain("Add Payments Feature");
        result.Should().NotContain("docs/features/add-payments.md");
    }

    [Fact]
    public async Task PlanEditRouteAsync_PrefersExactGoalTargetOutsideApplicationAndDomain()
    {
        var (sut, graph) = Build();
        var target = Node("cfg1", "CodeMeridianConfigFileStore", CodeNodeType.Class, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 7, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var helper = Node("core1", "FindRelatedTestsAsync(string,string?,CancellationToken)", CodeNodeType.Method, "src/Core/CodeGraph/ICodeGraphRepository.cs", 49, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var writeMethod = Node("cfg-write", "Write(DirectoryInfo,string?,string,bool,bool)", CodeNodeType.Method, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 118, "CodeMeridian", fileRole: IndexedFileRole.Source);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([helper, target, writeMethod]);
        graph
            .GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [writeMethod], []));
        graph
            .FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(writeMethod, 1)]);
        graph
            .FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "refactor CodeMeridianConfigFileStore into smaller collaborators for template IO and write behavior",
            "configuration,templates,file-io",
            "CodeMeridian");

        result.Should().Contain("**Anchor:** `CodeMeridianConfigFileStore` (Class) - `src/Tooling/Configuration/CodeMeridianConfigFileStore.cs`");
        result.Should().NotContain("**Anchor:** `FindRelatedTestsAsync");
    }

    [Fact]
    public async Task PlanEditRouteAsync_PrunesUnrelatedSemanticMatchesFromStructuredRouteStages()
    {
        var (sut, graph) = Build();
        var target = Node("cfg1", "CodeMeridianConfigFileStore", CodeNodeType.Class, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 7, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var unrelated = Node("dup1", "DuplicateCandidate", CodeNodeType.Class, "src/Core/CodeGraph/DuplicateCandidate.cs", 6, "CodeMeridian", fileRole: IndexedFileRole.Source, @namespace: "CodeMeridian.Core.CodeGraph");
        var writeMethod = Node("cfg-write", "Write(DirectoryInfo,string?,string,bool,bool)", CodeNodeType.Method, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 118, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var configTests = Node("t1", "IndexerConfigTests", CodeNodeType.Class, "tests/CodeMeridian.Indexer.Tests/Cli/IndexerConfigTests.cs", 6, "CodeMeridian", fileRole: IndexedFileRole.Test);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([target, unrelated, writeMethod]);
        graph
            .GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [writeMethod], []));
        graph
            .FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(writeMethod, 1)]);
        graph
            .FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(configTests, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "refactor CodeMeridianConfigFileStore safely",
            "configuration,templates,file-io",
            "CodeMeridian");

        result.Should().Contain("**Anchor:** `CodeMeridianConfigFileStore` (Class) - `src/Tooling/Configuration/CodeMeridianConfigFileStore.cs`");
        result.Should().Contain("IndexerConfigTests");
        result.Should().NotContain("DuplicateCandidate");
        result.Should().NotContain("src/Core/CodeGraph/DuplicateCandidate.cs");
    }

    [Fact]
    public async Task PlanEditRouteAsync_CanPreferInfrastructureAnchorsFromConfiguration()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            RoutePlanning = new RoutePlanningOptions
            {
                PreferContractAnchors = false,
                PreferApplicationOrDomainAnchors = false,
                PreferInfrastructureAnchors = true,
                PreferredAnchorBoost = 6
            }
        });
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", fileRole: IndexedFileRole.Source);
        var repository = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop", fileRole: IndexedFileRole.Source);
        var retryPolicy = Node("r2", "RetryPolicy", CodeNodeType.Class, "src/Infrastructure/Payments/RetryPolicy.cs", 30, "Shop", fileRole: IndexedFileRole.Source);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, repository, retryPolicy]);
        graph
            .GetContextForEditingAsync(repository.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(repository, [], [retryPolicy], []));
        graph
            .FindImpactAsync(repository.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(repository.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(retryPolicy, 1)]);
        graph
            .FindRelatedTestsAsync(repository.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "stabilize payment persistence retries",
            "payments,persistence,retries",
            "Shop");

        result.Should().Contain("**Anchor:** `SqlPaymentRepository` (Class) - `src/Infrastructure/Payments/SqlPaymentRepository.cs`");
        result.Should().NotContain("**Anchor:** `PaymentService`");
    }

    [Fact]
    public async Task PlanEditRouteAsync_NonConfigurationGoal_ExcludesConfigurationCandidatesWhenSourceTargetsExist()
    {
        var (sut, graph) = Build();
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", fileRole: IndexedFileRole.Source);
        var configKey = Node("cfg1", "analysis:routePlanning:preferContractAnchors", CodeNodeType.ConfigurationKey, "meridian.json", 18, "Shop", fileRole: IndexedFileRole.Configuration);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([configKey, service]);
        graph
            .GetContextForEditingAsync(service.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [], [], []));
        graph
            .FindImpactAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindRelatedTestsAsync(service.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "stabilize payment service orchestration",
            "payments,service",
            "Shop");

        result.Should().Contain("**Anchor:** `PaymentService` (Class) - `src/Application/Payments/PaymentService.cs`");
        result.Should().Contain("Implementation candidates: 1");
        result.Should().NotContain("analysis:routePlanning:preferContractAnchors");
        result.Should().NotContain("`meridian.json`");
    }

    [Fact]
    public async Task PlanEditRouteAsync_ConfigurationGoal_AllowsConfigurationCandidates()
    {
        var (sut, graph) = Build();
        var configKey = Node("cfg1", "analysis:routePlanning:preferContractAnchors", CodeNodeType.ConfigurationKey, "meridian.json", 18, "Shop", fileRole: IndexedFileRole.Configuration);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([configKey]);
        graph
            .GetContextForEditingAsync(configKey.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(configKey, [], [], []));
        graph
            .FindImpactAsync(configKey.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(configKey.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindRelatedTestsAsync(configKey.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "update meridian.json configuration for preferred route anchors",
            "configuration,meridian.json,route",
            "Shop");

        result.Should().Contain("**Anchor:** `analysis:routePlanning:preferContractAnchors` (ConfigurationKey) - `meridian.json`");
        result.Should().Contain("Implementation candidates: 1");
        result.Should().NotContain("No edit route found");
    }

    [Fact]
    public async Task PlanEditRouteAsync_WhenFilteringRemovesAllCandidates_FallsBackToBroaderMatches()
    {
        var (sut, graph) = Build();
        var configKey = Node("cfg1", "analysis:routePlanning:preferContractAnchors", CodeNodeType.ConfigurationKey, "meridian.json", 18, "Shop", fileRole: IndexedFileRole.Configuration);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([configKey]);
        graph
            .GetContextForEditingAsync(configKey.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(configKey, [], [], []));
        graph
            .FindImpactAsync(configKey.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(configKey.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindRelatedTestsAsync(configKey.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "stabilize payment route ranking",
            "payments,ranking",
            "Shop");

        result.Should().Contain("**Anchor:** `analysis:routePlanning:preferContractAnchors` (ConfigurationKey) - `meridian.json`");
        result.Should().Contain("Implementation candidates: 1");
        result.Should().NotContain("No edit route found");
    }

    [Fact]
    public async Task PlanEditRouteAsync_WhenNoMatches_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync("replace repository pattern in payments");

        result.Should().Contain("No edit route found");
        result.Should().Contain("find_implementation_surface");
    }


}
