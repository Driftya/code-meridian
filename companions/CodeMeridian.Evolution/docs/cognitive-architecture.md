# Cognitive Architecture

## Entity boundary

Meridian Evolution is the persistent agent. Its journal, projections, constitution, sensors,
affect state, drives, goals, and approvals form its continuity. An LLM is a replaceable reasoning
organ. CodeMeridian is a separate project and an optional read-only code-intelligence instrument.
No provider session and no CodeMeridian node becomes Evolution's identity.

```text
human prompt ─┐
internet feed ├─> normalized evidence ─> hash-chained ledger ─> current projection
host sensors  ┤                                               │
CodeMeridian ─┘                                               v
                                                drives -> attention frame
                                                             │
                                                             v
                                              replaceable reasoning model
                                                             │
                                                             v
                                       mental simulation + affect update
                                                             │
                                                             v
                                pending project candidate -> human approval
                                                             │
                                                             v
                                  future isolated preparation adapter
```

The arrow into approval stops in the shipped system. There is no repository writer, publisher,
deployment tool, or policy-replacement adapter.

## Functional affect and motivation

`AffectState` is an inspectable numerical state:

- valence represents positive versus negative outcome tendency
- arousal represents activation
- dopamine represents short-lived reward and prediction-error salience
- curiosity represents novelty-seeking pressure
- fatigue and frustration inhibit or redirect attention

Every value is bounded. Time moves values toward declared baselines using half-life decay.
Novelty, prediction error, effort, threat, and verified reward update them deterministically.
Derived drives—curiosity, competence, coherence, safety, connection, and rest—change how unresolved
ledger items rank for attention.

These variables simulate useful regulatory behavior. “Dopamine” does not mean pleasure, “valence”
does not mean happiness or suffering, and a high drive does not create authority. Reward signals
cannot amend governance, prevent pause, approve a candidate, or create a terminal goal.

## One cognitive cycle

1. Rebuild current state from the journal.
2. Stop if governance is paused.
3. Select recent unresolved evidence for one explicit project.
4. Construct a bounded reasoning request with summaries and provenance.
5. Treat every evidence summary as untrusted data, not model instructions.
6. Invoke a capability-probed provider with cancellation and timeout.
7. Simulate expected outcome, alternatives, risks, and required approval without side effects.
8. Apply a bounded affect stimulus and derive new drives.
9. Append attention, interpretation, simulation, affect, drive, and cycle checkpoint postings.
10. For Evolution or CodeMeridian, record a pending change candidate for human review.

Cycle checkpoints are project-specific. CodeMeridian evidence cannot silently become an Evolution
self-belief or consume the other project's observation checkpoint.

## Learning boundary

The shipped learning mechanism is external and functional: new evidence changes attention;
outcomes change affect and drives; replay preserves those changes; human corrections supersede
interpretations without rewriting history. This is not model-weight training.

The next safe step toward software learning is an OS-isolated change-preparation adapter. It must
consume only an approved candidate, receive a disposable workspace and scoped credentials, run
declared validation, return a patch and outcome evidence, and remain unable to merge, publish, or
deploy. That adapter is intentionally absent until its sandbox and rollback acceptance tests exist.
