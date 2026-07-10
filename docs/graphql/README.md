# GraphQL Nitro Queries

Example GraphQL documents for the `/graphql` endpoint live in this folder.

Use them in Nitro at `http://localhost:5100/graphql`:

1. Open one of the `.graphql` files.
2. Paste it into Nitro.
3. If the query uses variables, copy the JSON block from the file comments into Nitro's variables pane.
4. Send the API key as either:
   - `Authorization: Bearer <your-api-key>`
   - `X-CodeMeridian-ApiKey: <your-api-key>`

Notes:

- Query execution requires auth even though the Nitro UI itself can load anonymously.
- Node and relationship page size is clamped server-side to `100`.
- Neighbor traversal depth is clamped server-side to `3`.
- Supported node sort fields are `id`, `name`, `projectContext`, `primaryLabel`, `type`, and `filePath`.
- Supported relationship sort fields are `id`, `type`, `fromNodeId`, and `toNodeId`.
