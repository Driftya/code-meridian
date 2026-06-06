# Add Blast Radius With Confidence

- Status: pending
- Priority: P2
- Note: Make impact analysis explicit about what is proven versus inferred.

**Feature:** codemeridian impact PaymentGateway.ChargeAsync --confidence

**Why Neo4j helps:** The graph can separate exact relationships, interface paths, implementation paths, stale nodes, and documentation mentions.

**Expected output:**

- Impact confidence plus proven callers, heuristic callers, and unknown risk.
