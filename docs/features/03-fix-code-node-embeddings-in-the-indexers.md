# Fix Code Node Embeddings in the Indexers

- Status: done
- Priority: P0
- Note: `find_similar_nodes` already exists and is positioned as duplicate-code discovery, but it does not work unless nodes have embeddings


**Why:** `find_similar_nodes` already exists and is positioned as duplicate-code discovery, but it does not work unless nodes have embeddings. The current indexers ingest nodes without embeddings, so a high-value feature appears broken during real use.

**Tasks:**

- Add optional embedding generation to C# indexing.
- Add optional embedding generation to TypeScript indexing.
- Make embedding generation opt-in by env/config so local indexing remains cheap.
- Document required model, dimensions, and cost behavior.
- Add a clear indexer warning when `find_similar_nodes` cannot work because embeddings are absent.

**Effort:** Medium to high  
**Value:** High  
**Risk:** Medium, mostly around model/provider configuration and indexing speed.

