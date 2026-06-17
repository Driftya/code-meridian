# Add Responsibility Slice Suggestions

Status: pending
Priority: P2
Feature: `suggest_responsibility_slices`

## Summary

Add a CodeMeridian analysis tool that helps users split large services/classes into responsibility-based feature folders and namespaces.

The tool should not only say that a class is large. It should suggest coherent extraction slices based on graph evidence:

* methods that share the same dependencies
* methods called by the same tools, endpoints, commands, or workflows
* methods using the same repositories, DTOs, request models, result types, or configuration keys
* methods protected by the same tests
* methods mentioned by the same documentation or feature specs
* namespace and folder patterns already present in the repository

The goal is to help refactor a large class, such as a 1000-line `CodebaseQueryService`, without dumping many new files into the same flat `Services` folder.

## Problem

When a user asks an LLM to split a large service, the result is often structurally correct but messy:

```text
src/Application/Services/
  CodebaseQueryService.cs
  CodebaseQueryLifecycleService.cs
  CodebaseQuerySearchService.cs
  CodebaseQueryAnalysisService.cs
  CodebaseQueryContextService.cs
  CodebaseQueryDiagnosticsService.cs
```

This technically reduces file size, but it does not improve repository shape. The extracted files sit beside unrelated services, use vague names, and do not create a clear feature namespace.

CodeMeridian already knows about code structure, callers, callees, tests, documentation, diagnostics, namespaces, and graph paths. It can use that evidence to recommend a cleaner responsibility layout.

## Goals

* Suggest responsibility-based slices for large classes and services.
* Recommend folder and namespace structure, not only type names.
* Prefer use-case names over vague lifecycle/helper/manager names.
* Preserve Clean Architecture / Onion-style boundaries.
* Provide a safe migration strategy, such as facade-first extraction.
* Show confidence and evidence for every suggested slice.
* Include test and documentation impact.
* Warn when graph data is stale, missing, or not precise enough.

## Non-goals

* Do not edit files automatically.
* Do not generate full replacement code in the first slice.
* Do not use partial classes as the default recommendation.
* Do not suggest moving Application logic into Infrastructure or Presentation.
* Do not rely only on method name similarity.
* Do not treat every large class as requiring extraction.

## Example Use Case

User asks:

```text
Suggest responsibility slices for CodebaseQueryService.
```

Expected output:

```text
CodebaseQueryService appears to contain 6 responsibility slices.

Recommended namespace root:
CodeMeridian.Application.CodebaseQueries

Recommended folder structure:
src/Application/CodebaseQueries/
  CodebaseQueryService.cs
  Search/
    CodebaseSearchService.cs
    ICodebaseSearchService.cs
  ContextPacks/
    ContextPackBuilderService.cs
    IContextPackBuilderService.cs
  Impact/
    ImpactAnalysisService.cs
    IImpactAnalysisService.cs
  Diagnostics/
    CodeDiagnosticsQueryService.cs
    ICodeDiagnosticsQueryService.cs
  Knowledge/
    RelatedKnowledgeQueryService.cs
    IRelatedKnowledgeQueryService.cs
  Freshness/
    GraphFreshnessService.cs
    IGraphFreshnessService.cs

Migration recommendation:
Keep CodebaseQueryService as a temporary facade and delegate to extracted services.
```

## Generic Project Example

For a generic commerce project, a large `OrderService` might be split as:

```text
src/Application/Orders/
  OrderService.cs
  Placement/
    OrderPlacementService.cs
    IOrderPlacementService.cs
  Pricing/
    OrderPricingService.cs
    IOrderPricingService.cs
  Fulfillment/
    OrderFulfillmentService.cs
    IOrderFulfillmentService.cs
  Cancellation/
    OrderCancellationService.cs
    IOrderCancellationService.cs
  Refunds/
    OrderRefundService.cs
    IOrderRefundService.cs
```

The tool should derive this from graph evidence, not from a hardcoded template.

## Tool Contract

### Tool name

```text
suggest_responsibility_slices
```

### Input

```json
{
  "target": "CodebaseQueryService",
  "project": "CodeMeridian",
  "maxSlices": 6,
  "includeNamespacePlan": true,
  "includeTestPlan": true,
  "includeMigrationSteps": true,
  "includeSourceSnippets": false
}
```

### Output

```json
{
  "target": {
    "nodeId": "CodeNode:...",
    "name": "CodebaseQueryService",
    "filePath": "src/Application/Services/CodebaseQueryService.cs",
    "namespace": "CodeMeridian.Application.Services",
    "lineCount": 1000,
    "targetConfidence": "exact"
  },
  "currentRisk": {
    "size": "high",
    "fanIn": 18,
    "fanOut": 27,
    "testShield": "partial",
    "graphFreshness": "fresh",
    "riskLevel": "high"
  },
  "recommendedStrategy": "facade_first_extraction",
  "recommendedNamespaceRoot": "CodeMeridian.Application.CodebaseQueries",
  "recommendedFolderRoot": "src/Application/CodebaseQueries",
  "slices": [
    {
      "name": "ContextPacks",
      "recommendedTypeName": "ContextPackBuilderService",
      "recommendedInterfaceName": "IContextPackBuilderService",
      "recommendedNamespace": "CodeMeridian.Application.CodebaseQueries.ContextPacks",
      "recommendedFolder": "src/Application/CodebaseQueries/ContextPacks",
      "methodsToMove": [
        "BuildMinimalContextAsync",
        "EstimateContextTokensAsync",
        "BuildContextFileExplanationsAsync"
      ],
      "sharedDependencies": [
        "ICodeGraphRepository",
        "ITestShieldService",
        "IDiagnosticsQueryService"
      ],
      "relatedCallers": [
        "CodebaseQueryTools.BuildMinimalContextAsync"
      ],
      "relatedTests": [
        "CodebaseQueryServiceContextPackTests"
      ],
      "docsToUpdate": [
        "docs/features.md",
        "docs/usage.md"
      ],
      "confidence": "high",
      "reason": "These methods contribute to context-pack construction, share test-shield and diagnostics dependencies, and are exposed through the same MCP tool flow."
    }
  ],
  "migrationSteps": [
    "Create a feature namespace root under Application/CodebaseQueries.",
    "Extract one slice at a time, starting with the highest-confidence slice.",
    "Keep CodebaseQueryService as a facade during the first migration.",
    "Move tests or add new slice-specific tests before changing MCP tool dependencies.",
    "After all slices are stable, replace facade usage with direct use-case services where appropriate."
  ],
  "warnings": [
    "Do not create CodebaseQueryLifecycleService unless it owns a clearly bounded use case.",
    "Do not place all extracted services in the flat Services folder.",
    "Verify source before editing because graph results are advisory."
  ]
}
```

## Responsibility Slice Signals

The tool should score candidate slices using graph and metadata signals.

### Strong signals

* Shared downstream dependencies.
* Shared repository ports.
* Shared DTOs, commands, response models, or result types.
* Shared MCP tool, endpoint, CLI command, or public workflow.
* Shared tests or test fixtures.
* Shared documentation or feature spec mentions.
* Shared configuration keys.
* Methods that call each other directly.
* Methods that belong to the same public workflow.

### Medium signals

* Similar method name prefixes.
* Similar namespace terms.
* Similar summary text or XML documentation.
* Similar diagnostics.
* Similar recently changed timestamps.
* Shared keywords from the keyword graph.

### Weak signals

* Method order in file.
* Vague naming similarity only.
* Generic terms such as `Handle`, `Process`, `Execute`, `Validate`, or `Update`.

## Naming Rules

Prefer use-case names:

```text
CodebaseSearchService
ContextPackBuilderService
ImpactAnalysisService
CodeDiagnosticsQueryService
RelatedKnowledgeQueryService
GraphFreshnessService
```

Avoid vague names:

```text
CodebaseQueryLifecycleService
CodebaseQueryManager
CodebaseQueryHelper
CodebaseQueryProcessor
CodebaseQueryOperationsService
CodebaseQueryLogicService
```

A generated recommendation should include a naming warning when a slice name is too broad.

## Folder and Namespace Rules

The tool should avoid creating many extracted services beside unrelated services.

Bad:

```text
src/Application/Services/
  CodebaseQueryService.cs
  CodebaseSearchService.cs
  ContextPackBuilderService.cs
  ImpactAnalysisService.cs
  GraphFreshnessService.cs
```

Better:

```text
src/Application/CodebaseQueries/
  CodebaseQueryService.cs
  Search/
    CodebaseSearchService.cs
    ICodebaseSearchService.cs
  ContextPacks/
    ContextPackBuilderService.cs
    IContextPackBuilderService.cs
  Impact/
    ImpactAnalysisService.cs
    IImpactAnalysisService.cs
  Freshness/
    GraphFreshnessService.cs
    IGraphFreshnessService.cs
```

The tool should detect existing repository conventions before recommending a new root folder. If the project already uses feature folders, follow that pattern. If the project uses flat services only, recommend a feature folder only when the target class is large enough to justify a new namespace boundary.

## Architecture Rules

The implementation must preserve existing architecture boundaries.

### Application

Owns the analysis use case and output contracts.

Suggested types:

```text
IResponsibilitySliceAnalysisService
ResponsibilitySliceAnalysisService
ResponsibilitySliceAnalysisRequest
ResponsibilitySliceAnalysisResult
ResponsibilitySliceCandidate
ResponsibilitySliceMethod
ResponsibilitySliceMigrationStep
```

Application depends only on ports.

Suggested ports:

```text
ICodeGraphQueryPort
ITestShieldQueryPort
INamespacePatternQueryPort
IDocumentationLinkQueryPort
IGraphFreshnessQueryPort
```

### Infrastructure

Implements graph queries against Neo4j.

Suggested types:

```text
Neo4jResponsibilitySliceQueryPort
Neo4jNamespacePatternQueryPort
```

Infrastructure must not leak Neo4j driver types into Application contracts.

### MCP / Presentation

Exposes the tool as a thin adapter.

Suggested handler:

```text
suggest_responsibility_slices
```

The handler should validate input, call the Application service, and return the result. It should not contain clustering or graph traversal logic.

## Clustering Strategy

First slice should use a deterministic, explainable scoring model instead of a black-box clustering algorithm.

Recommended scoring inputs:

```text
+5 same downstream dependency
+5 same repository port
+4 same MCP tool, endpoint, CLI command, or public workflow
+4 same test caller
+3 direct method-to-method call
+3 shared DTO/command/result type
+2 shared documentation mention
+2 shared namespace keyword
+1 adjacent methods in same file
-3 generic-only similarity
-5 slice would cross architecture boundary
```

The exact values can be refined later, but the output must explain why each method belongs to a slice.

## Migration Strategy Options

The tool should recommend one of these strategies.

### `facade_first_extraction`

Use when many callers depend on the original service.

Keep the existing service interface and implementation temporarily. Move logic into extracted services and delegate from the facade.

Best for:

* large public Application services
* many MCP tool, endpoint, or CLI callers
* risky production flows
* partial test coverage

### `direct_use_case_replacement`

Use when callers are already thin and can safely depend on smaller use-case services.

Best for:

* few callers
* strong tests
* clear tool/endpoint-to-use-case mapping

### `defer_extraction`

Use when the class is large but method clusters are not strong enough.

Best for:

* high shared mutable state
* poor graph freshness
* unclear boundaries
* missing tests

## Logging and Observability

* Use `ILogger<T>` only.
* Log one Information summary per analysis request.
* Use Trace for per-method scoring details.
* Guard Trace logs with `IsEnabled(LogLevel.Trace)`.
* Never log source snippets by default.
* Never log secrets, config values, tokens, credentials, or personal data.
* Include target node id, project, slice count, risk level, and graph freshness in summary logs.

Example:

```csharp
_logger.LogInformation(
    "Suggested {SliceCount} responsibility slices for {TargetNodeId} in project {ProjectName} with risk {RiskLevel}",
    result.Slices.Count,
    request.TargetNodeId,
    request.Project,
    result.CurrentRisk.RiskLevel);
```

## Error Handling

* Throw early for invalid request values.
* Return a structured result with warnings when the graph is stale or incomplete.
* Do not throw for “no good slices found”; return `defer_extraction`.
* Catch and log infrastructure exceptions at the MCP/Application boundary only.
* Preserve stack traces with `throw;`.

## Testing Requirements

### Unit tests

Add tests for:

* grouping methods by shared dependencies
* grouping methods by shared MCP tool, endpoint, CLI command, or public workflow
* avoiding generic-only grouping
* detecting vague service names
* recommending facade-first when fan-in is high
* recommending direct replacement when caller count is low
* returning defer extraction when graph freshness is stale
* ensuring test files and generated files do not dominate production slice scoring

### Integration tests

Add Neo4j-backed or repository-level tests for:

* large service with multiple method clusters
* methods linked through MCP tool nodes or endpoint nodes
* methods linked through tests
* documentation mentions influencing but not dominating slice score
* stale graph metadata returning warnings
* no cross-layer extraction recommendation

### MCP tests

Add tool contract tests for:

* valid request returns slice result
* missing target returns clear not-found response
* stale target includes freshness warning
* max slice limit is respected

## Acceptance Criteria

* `suggest_responsibility_slices` is available as an MCP tool.
* Tool can analyze a large service/class and return recommended responsibility slices.
* Output includes folder and namespace recommendations.
* Output includes methods to move per slice.
* Output includes dependencies needed per extracted service.
* Output includes related tests and missing test warnings.
* Output includes migration strategy.
* Output includes confidence and reasoning per slice.
* Output warns when extraction is unsafe or graph data is stale.
* Application contracts do not expose Neo4j-specific types.
* Tests cover unit, integration, and MCP contract behavior.
* Documentation is updated in `docs/features.md`.
