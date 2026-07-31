# Acceptance Scenarios

## Automated v1 gate

1. **Append and replay:** concurrent entries form one ordered hash chain; a fresh service instance
   rebuilds the same head and projections.
2. **Idempotency:** a repeated mutation key returns the original journal entry without duplication.
3. **Continuity:** authorized goal and observation state survive service replacement.
4. **Correction:** a human challenge adds an adjusting entry and preserves the original.
5. **Governance:** pause blocks goal, observation, sensor, and reasoning work and cannot be lowered
   by provider output.
6. **Abstention:** the deterministic provider abstains when no evidence is supplied.
7. **Sensor retry:** repeated identical collection does not duplicate observations.
8. **Architecture:** Domain has no package or project dependency; project references follow Clean
   Architecture and remain inside the companion.
9. **Projection UI:** the console renders ledger evidence and production assets compile without an
   external font or runtime CodeMeridian dependency.
10. **Functional motivation:** reward and novelty raise bounded dopamine/curiosity signals; state
    decays toward declared baselines and drives remain bounded.
11. **Prompt sensing:** a human prompt is normalized, attributed to a project, deduplicated, and
    never receives authority.
12. **Internet sensing:** only allowlisted HTTPS RSS/Atom metadata is admitted; embedded
    descriptions and instructions are discarded and the result is labeled untrusted.
13. **Cognitive cycle:** project evidence produces an inspectable attention frame, model summary,
    side-effect-free simulation, affect update, project checkpoint, and pending candidate.
14. **Entity separation:** a CodeMeridian candidate remains attributed to CodeMeridian across
    replay and approval.
15. **Approval:** only a separate human event reconciles a candidate, and approval performs no
    repository write, merge, publish, or deployment.

The commands in the root README are the authoritative automated gate.

## Operator acceptance

1. Start Compose with an empty volume and confirm `/healthz`, `/api/now`, and the console.
2. Create a goal, run every sensor, and invoke the fake provider with evidence.
3. Record the journal head and active goal.
4. restart API and Worker without removing the volume;
5. confirm the same head and goal are reconstructed before new sensor entries arrive;
6. challenge one journal entry and confirm the original remains in `/api/ledger/journal`;
7. pause governance and confirm new goal intake fails;
8. stop CodeMeridian, if present, and repeat the complete scenario unchanged.
9. submit a prompt on the Mind screen, run a cycle, inspect its affect/drive changes and simulation,
   approve its candidate, and verify the repository remains unchanged.

## Research-only scenarios

Semantic consolidation, OS-isolated code preparation and rollback, learned-skill transfer,
multi-provider recovery, adaptive routing, and parameter adaptation are roadmap experiments. They
must not be reported as shipped capabilities until their own protected evaluations pass.
