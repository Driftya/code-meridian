### Keyword Graph Enrichment Plan

### Purpose

Add a Neo4j-side keyword enrichment feature that derives `Keyword` nodes and `HAS_KEYWORD` relationships from already indexed CodeMeridian graph nodes.

The goal is to create an explainable lexical bridge between code, documentation, diagnostics, and knowledge documents without requiring the Roslyn indexer, TypeScript indexer, or MCP tools to extract keywords themselves.

CodeMeridian already indexes code structure, documentation, diagnostics, and project knowledge into Neo4j. The keyword enrichment layer should operate on that existing graph as a secondary enrichment step. The tool surface should query the enriched graph, not author keyword data directly.

### Goals

* Extract useful keywords from existing Neo4j nodes.
* Create shared `Keyword` nodes.
* Create weighted relationships from graph nodes to keywords.
* Support related-node discovery through shared keyword overlap.
* Keep results explainable by returning matched keywords and score.
* Keep exact graph relationships separate from lexical/heuristic relationships.
* Avoid changing language indexers for MVP.

### Non-goals

* No keyword extraction inside Roslyn indexer.
* No keyword extraction inside TypeScript indexer.
* No MCP tool that manually creates keyword nodes.
* No embeddings replacement.
* No AI-generated concepts.
* No permanent `RELATED_TO` edges in MVP.
* No full natural-language search replacement.
* No language-specific stemming in MVP.

### Core Idea

Existing graph nodes contain text-like properties such as:

* name
* fullName
* displayName
* kind
* filePath
* namespace
* signature
* summary
* content
* title
* message
* ruleId
* severity
* description

A Neo4j-side enrichment job reads these existing properties, tokenizes them, normalizes terms, removes noise, and stores the useful terms as shared `Keyword` nodes.

Example:

```cypher
(:CodeNode { name: "SeedReplyReadState" })
```

becomes connected to:

```cypher
(:Keyword { value: "seed" })
(:Keyword { value: "reply" })
(:Keyword { value: "read" })
(:Keyword { value: "state" })
```

through:

```cypher
(:CodeNode)-[:HAS_KEYWORD { count: 1, weight: 0.85, source: "name" }]->(:Keyword)
```

### Graph Model

#### Keyword Node

```cypher
(:Keyword {
  projectId: "CodeMeridian",
  value: "authentication",
  normalizedValue: "authentication",
  documentFrequency: 12,
  totalFrequency: 37,
  createdAtUtc: datetime(),
  updatedAtUtc: datetime()
})
```

#### Keyword Relationship

```cypher
(:CodeNode)-[:HAS_KEYWORD {
  count: 4,
  weight: 0.73,
  source: "name|summary|signature|filePath",
  indexedAtUtc: datetime(),
  enrichmentVersion: 1
}]->(:Keyword)
```

Allowed source nodes:

```text
CodeNode
KnowledgeDocument
Diagnostic
ApiEndpoint
ExternalConcept
```

Relationship types:

```text
CodeNode          -[:HAS_KEYWORD]-> Keyword
KnowledgeDocument -[:HAS_KEYWORD]-> Keyword
Diagnostic        -[:HAS_KEYWORD]-> Keyword
ApiEndpoint       -[:HAS_KEYWORD]-> Keyword
ExternalConcept   -[:HAS_KEYWORD]-> Keyword
```

### Neo4j Constraints and Indexes

```cypher
CREATE CONSTRAINT keyword_identity IF NOT EXISTS
FOR (keyword:Keyword)
REQUIRE (keyword.projectId, keyword.normalizedValue) IS UNIQUE;
```

```cypher
CREATE INDEX keyword_value IF NOT EXISTS
FOR (keyword:Keyword)
ON (keyword.normalizedValue);
```

Optional later:

```cypher
CREATE INDEX keyword_frequency IF NOT EXISTS
FOR (keyword:Keyword)
ON (keyword.documentFrequency);
```

### Enrichment Flow

The enrichment job runs after normal graph indexing.

```text
1. Structural indexers write CodeNode, KnowledgeDocument, Diagnostic, ApiEndpoint, and ExternalConcept nodes.
2. Keyword enrichment starts inside the CodeMeridian backend/application flow.
3. Enrichment queries Neo4j for candidate nodes.
4. Neo4j-side query extracts or projects text fields.
5. Application service normalizes/tokenizes text, unless using APOC-only extraction later.
6. Application sends only derived keyword updates back to Neo4j.
7. Neo4j upserts Keyword nodes.
8. Neo4j replaces HAS_KEYWORD relationships for each changed source node.
9. Keyword frequency metadata is recalculated.
10. MCP tools query keyword relationships.
```

### Important Design Decision

The source of truth is still the existing graph.

The keyword enrichment service is allowed to read existing graph nodes and write derived keyword nodes.

The language indexers should not know that keywords exist.

This keeps the pipeline clean:

```text
Roslyn / TS Indexers
  -> structural graph nodes

Documentation indexer
  -> knowledge/document nodes

Diagnostics ingestion
  -> diagnostic nodes

Keyword enrichment
  -> derived keyword graph
```

### Change Detection

Each enriched source node should store metadata:

```cypher
SET node.keywordTextChecksum = $checksum,
    node.keywordIndexedAtUtc = datetime(),
    node.keywordEnrichmentVersion = 1
```

Before reprocessing a node:

```text
1. Build text projection from selected properties.
2. Calculate checksum.
3. If checksum equals node.keywordTextChecksum, skip.
4. Otherwise replace existing HAS_KEYWORD relationships.
```

This makes the enrichment idempotent.

### Text Projection Rules

Each node type should have a defined projection.

#### CodeNode

Use:

```text
name
fullName
kind
namespace
signature
summary
filePath
```

Weights:

```text
name:       high
fullName:   medium
signature:  medium
summary:    high
filePath:   low
namespace:  low
kind:       low
```

#### KnowledgeDocument

Use:

```text
title
content
path
tags
```

Weights:

```text
title:    high
tags:     high
content:  medium
path:     low
```

#### Diagnostic

Use:

```text
ruleId
message
severity
filePath
symbolName
```

Weights:

```text
ruleId:      medium
message:     high
symbolName:  high
filePath:    low
severity:    very low
```

#### ApiEndpoint

Use:

```text
route
httpMethod
handlerName
summary
```

Weights:

```text
route:       high
handlerName: high
summary:     high
httpMethod:  low
```

#### ExternalConcept

Use:

```text
name
description
source
```

Weights:

```text
name:        high
description: medium
source:      low
```

### Keyword Extraction Rules

Normalize terms by:

```text
- Lowercasing
- Splitting PascalCase
- Splitting camelCase
- Splitting snake_case
- Splitting kebab-case
- Splitting dotted names
- Splitting route segments
- Removing punctuation
- Removing generic code symbols
```

Examples:

```text
SeedReplyReadState -> seed, reply, read, state
BuildMinimalContextAsync -> build, minimal, context
find_stale_knowledge -> find, stale, knowledge
/api/notes/unread-replies/count -> notes, unread, replies, count
```

Reject terms when:

```text
- Length is less than 3
- Term is a stopword
- Term is a common programming filler word
- Term is a GUID-like string
- Term is only numeric
- Term is only punctuation
- Term is too frequent across the project
```

Initial stopword examples:

```text
the
and
for
with
from
into
new
get
set
has
can
use
used
using
class
record
string
bool
void
task
async
public
private
internal
static
sealed
```

### Weighting Rules

Each keyword relationship gets a weight.

Suggested formula:

```text
weight = sourceWeight * localFrequencyWeight * rarityWeight
```

Source weight examples:

```text
Node name:              1.00
Document title:         1.00
Diagnostic message:     0.90
Symbol summary:         0.85
Signature:              0.65
Documentation content:  0.60
File path:              0.35
Node kind:              0.20
```

Rarity should reduce the value of overly common terms.

Example:

```text
documentFrequency too high = lower score
rare but repeated domain term = higher score
```

### Query: Find Related Knowledge

Add a query service that finds related nodes by shared keyword overlap.

MCP tool name:

```text
find_related_knowledge
```

Input:

```json
{
  "sourceNodeId": "node-id",
  "targetKinds": ["KnowledgeDocument", "Diagnostic", "CodeNode"],
  "minimumSharedKeywords": 3,
  "minimumScore": 0.25,
  "limit": 20
}
```

Output:

```json
{
  "sourceNodeId": "node-id",
  "results": [
    {
      "targetNodeId": "target-id",
      "targetKind": "KnowledgeDocument",
      "score": 0.74,
      "sharedKeywordCount": 8,
      "matchedKeywords": [
        "stale",
        "knowledge",
        "document",
        "mention"
      ],
      "confidence": "lexical"
    }
  ]
}
```

### Query Shape

```cypher
MATCH (source {id: $sourceNodeId})-[sk:HAS_KEYWORD]->(keyword:Keyword)<-[tk:HAS_KEYWORD]-(target)
WHERE target.id <> source.id
  AND target.kind IN $targetKinds
  AND keyword.documentFrequency <= $maximumDocumentFrequency
WITH
  target,
  count(keyword) AS sharedKeywordCount,
  sum(sk.weight * tk.weight) AS score,
  collect(keyword.normalizedValue)[0..20] AS matchedKeywords
WHERE sharedKeywordCount >= $minimumSharedKeywords
  AND score >= $minimumScore
RETURN
  target.id AS targetNodeId,
  target.kind AS targetKind,
  sharedKeywordCount,
  score,
  matchedKeywords
ORDER BY score DESC, sharedKeywordCount DESC
LIMIT $limit
```

### Neo4j-Side Batch Candidate Query

Candidate nodes can be found with a query like:

```cypher
MATCH (node)
WHERE node.projectId = $projectId
  AND (
    node:CodeNode
    OR node:KnowledgeDocument
    OR node:Diagnostic
    OR node:ApiEndpoint
    OR node:ExternalConcept
  )
RETURN
  labels(node) AS labels,
  node.id AS id,
  node.name AS name,
  node.fullName AS fullName,
  node.kind AS kind,
  node.namespace AS namespace,
  node.signature AS signature,
  node.summary AS summary,
  node.title AS title,
  node.content AS content,
  node.message AS message,
  node.ruleId AS ruleId,
  node.severity AS severity,
  node.filePath AS filePath,
  node.route AS route,
  node.httpMethod AS httpMethod,
  node.description AS description,
  node.keywordTextChecksum AS keywordTextChecksum
LIMIT $batchSize
```

The Application layer can build the text projection from this result.

### Optional APOC-Only Variant

If APOC is available, some tokenization can happen inside Neo4j.

However, MVP should not require APOC.

Reason:

```text
- Keeps local setup simpler.
- Avoids hidden dependency on Neo4j plugin availability.
- Keeps extraction rules testable in C#.
```

Recommended MVP:

```text
Neo4j stores and queries.
Application extracts and scores.
Indexers remain unaware.
```

### Application Layer Design

#### Interfaces

```csharp
public interface IKeywordEnrichmentService
{
    Task<KeywordEnrichmentResultDto> EnrichProjectAsync(
        KeywordEnrichmentRequestDto request,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IKeywordGraphRepository
{
    Task<IReadOnlyList<KeywordSourceNodeDto>> GetKeywordSourceNodesAsync(
        KeywordSourceNodeQueryDto query,
        CancellationToken cancellationToken);

    Task ReplaceKeywordsAsync(
        ReplaceKeywordRelationshipsCommand command,
        CancellationToken cancellationToken);

    Task RecalculateKeywordStatisticsAsync(
        string projectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<KeywordRelatedNodeDto>> FindRelatedByKeywordsAsync(
        KeywordRelatedNodeQueryDto query,
        CancellationToken cancellationToken);
}
```

```csharp
public interface IKeywordExtractionService
{
    KeywordExtractionResult Extract(KeywordExtractionInput input);
}
```

### Infrastructure Design

Infrastructure implements:

```text
Neo4jKeywordGraphRepository
DefaultKeywordExtractionService
```

Neo4j repository responsibilities:

```text
- Load candidate nodes
- Upsert Keyword nodes
- Replace HAS_KEYWORD relationships
- Update checksum metadata
- Recalculate documentFrequency and totalFrequency
- Query related nodes by overlap
```

### MCP Tool Design

MCP exposes only query/admin entry points.

#### Tool: find_related_knowledge

Purpose:

```text
Find code, docs, diagnostics, and knowledge nodes that are lexically related to a source node through shared keywords.
```

#### Tool: rebuild_keyword_graph

Optional developer/admin tool.

Purpose:

```text
Rebuild derived Keyword nodes and HAS_KEYWORD relationships for a project.
```

This should be marked as a maintenance operation.

### CLI Integration

The normal index command can trigger enrichment after indexing.

Suggested flags:

```text
codemeridian index . --keywords
codemeridian index . --skip-keywords
codemeridian keywords rebuild
```

Default:

```text
Keyword enrichment enabled if configured.
```

Configuration:

```json
{
  "KeywordEnrichment": {
    "Enabled": true,
    "MinimumKeywordLength": 3,
    "MaximumKeywordsPerNode": 40,
    "MinimumSharedKeywords": 3,
    "MinimumScore": 0.25,
    "MaximumDocumentFrequencyRatio": 0.35,
    "BatchSize": 500
  }
}
```

### Logging

Log one summary per batch or run.

```text
Information:
Keyword enrichment completed for project {ProjectId}. Processed {ProcessedCount} nodes, skipped {SkippedCount}, created {KeywordCount} keywords, updated {RelationshipCount} relationships.
```

Trace only inside loops.

```text
Trace:
Extracted {KeywordCount} keywords for node {NodeId}.
```

Never log full source text, full document content, diagnostic payloads, secrets, tokens, or raw user content.

### Error Handling

Application:

```text
- Validate request.
- Return safe failure DTO for invalid input.
- Let infrastructure errors bubble to boundary.
```

Infrastructure:

```text
- Catch only specific Neo4j exceptions if translating to repository exceptions.
- Preserve stack traces.
```

MCP/CLI boundary:

```text
- Catch expected exceptions.
- Log once.
- Return safe tool error.
```

### Testing Plan

#### Keyword Extraction Tests

```text
- Splits PascalCase.
- Splits camelCase.
- Splits snake_case.
- Splits kebab-case.
- Splits routes.
- Rejects short words.
- Rejects stopwords.
- Rejects GUID-like values.
- Keeps domain terms.
```

#### Application Tests

```text
- Skips nodes with unchanged checksum.
- Processes changed nodes.
- Enforces maximum keyword count per node.
- Produces stable output for same input.
- Returns lexical confidence.
```

#### Infrastructure Tests

```text
- Creates Keyword node once per project/value.
- Replaces old HAS_KEYWORD relationships.
- Updates source node keyword checksum.
- Recalculates documentFrequency.
- Finds related nodes by shared keyword overlap.
- Applies minimumSharedKeywords.
- Applies minimumScore.
- Excludes overly common keywords.
```

#### MCP Tests

```text
- find_related_knowledge rejects missing sourceNodeId.
- find_related_knowledge returns empty result when no keywords match.
- find_related_knowledge returns matched keywords.
- find_related_knowledge labels confidence as lexical.
```

### Rollout Plan

#### Phase 1: Extraction and DTOs

```text
- Add keyword DTOs and options.
- Add extraction service.
- Add extraction tests.
- No graph writes yet.
```

#### Phase 2: Neo4j Persistence

```text
- Add Keyword constraints.
- Add repository methods.
- Add Keyword node upsert.
- Add HAS_KEYWORD relationship replacement.
- Add checksum metadata update.
```

#### Phase 3: Enrichment Job

```text
- Add project enrichment service.
- Process source nodes in batches.
- Skip unchanged nodes.
- Recalculate keyword statistics.
- Add structured summary logging.
```

#### Phase 4: Query Tool

```text
- Add related-by-keywords query.
- Add find_related_knowledge MCP tool.
- Return score, sharedKeywordCount, matchedKeywords, and confidence.
```

#### Phase 5: Documentation

```text
- Update docs/features.md.
- Add usage example.
- Explain lexical confidence.
- Document that keyword graph is derived and heuristic.
```

### Acceptance Criteria

```text
- Keyword enrichment runs from existing Neo4j graph nodes.
- Roslyn and TypeScript indexers do not create keyword nodes.
- Keyword nodes are unique per project and normalized value.
- Source nodes connect to keywords through HAS_KEYWORD.
- Re-running enrichment is idempotent.
- Unchanged nodes are skipped by checksum.
- find_related_knowledge returns ranked related graph nodes.
- Results include score, shared keyword count, matched keywords, and confidence = lexical.
- Common/noisy keywords are filtered or down-weighted.
- No Neo4j types leak into Domain or Application contracts.
- All I/O operations are async and accept CancellationToken.
- Logs are structured and loop-safe.
```

### Architecture Review

This design keeps Onion boundaries intact.

```text
Domain/Core:
Pure keyword/value models only.

Application:
Enrichment orchestration, scoring rules, ports, DTOs.

Infrastructure:
Neo4j queries, constraints, persistence, graph updates.

MCP/CLI:
Boundary tools that trigger or query Application services.
```

The keyword graph is derived infrastructure data, but the rules for how it is produced and queried belong in Application.

### Operational Review

```text
- Async-first repository and service methods.
- CancellationToken required for enrichment and query operations.
- Structured logging only.
- Trace logging inside loops only.
- No sensitive content in logs.
- Idempotent writes through checksum comparison.
- Batch processing to avoid large graph transactions.
- Tool results clearly marked as lexical/heuristic.
```

### Final Recommendation

Build this as a Neo4j graph enrichment pass over existing indexed nodes.

Do not make language indexers keyword-aware.

Do not make MCP tools create keywords directly.

The clean shape is:

```text
Existing graph nodes
  -> enrichment service reads graph
  -> Keyword nodes and HAS_KEYWORD edges are derived
  -> MCP tools query overlap
  -> AI receives explainable lexical connections
```

This gives CodeMeridian a useful soft-relation layer while keeping exact code structure, diagnostics, and knowledge links trustworthy.
