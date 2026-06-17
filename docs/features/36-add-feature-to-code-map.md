# Add Feature To Code Map

- Status: pending
- Priority: P2
- Note: Make features first-class graph nodes linked to code.

**Feature:** codemeridian feature add "Support PayPal subscription"

**Why Neo4j helps:** Feature nodes can connect code, docs, tests, issues, endpoints, tables, and messages.

**Expected output:**

- Questions like what code belongs to this feature, what tests protect it, and what changed recently.

**Related first slice:** `analyze_feature_implementation_path` now maps a feature request or `docs/features/*.md` path to documented status, closest implementation surfaces, likely touched areas, related tests, docs, missing graph evidence, confidence, and risk. It does not yet create first-class `Feature` nodes; that remains the stronger graph-model follow-up for durable feature-to-code ownership.
