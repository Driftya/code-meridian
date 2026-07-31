# Threat Model

## Protected assets

- journal integrity and ordering
- human authority and governance state
- identity and continuity claims
- database credentials and API mutation key
- privacy of admitted evidence
- reliability of projections, goals, and corrections
- separation between Meridian Evolution and observed projects
- integrity of affect, reward, drive, and attention state

## Trust boundaries

The browser, sensors, retrieved content, reasoning providers, and network are untrusted. The
Application governance kernel and Domain validation are trusted code. PostgreSQL is the durable
boundary; container and host administrators remain privileged human operators.

## Principal threats and controls

| Threat                                             | Control                                                                                                                   |
| -------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Prompt injection grants authority                  | provider output is only an observation; authority is a separate ledger posting                                            |
| Replay or duplicate mutation                       | idempotency keys and a database uniqueness constraint                                                                     |
| Concurrent journal forks                           | serializable transaction plus PostgreSQL advisory transaction lock                                                        |
| Silent history modification                        | sequence, previous hash, content hash, and trial-balance replay                                                           |
| Provider invents continuity                        | identity and goals rebuild from the journal, never provider session state                                                 |
| Unauthorized network mutation                      | non-GET API calls require `X-Evolution-Key` when configured                                                               |
| Cross-origin browser abuse                         | API CORS permits loopback origins only; deployed UI uses same-origin proxy                                                |
| Sensor failure stops cognition                     | worker isolates failures per sensor and retries next period                                                               |
| Internet feed carries prompt injection             | HTTPS allowlist, size cap, DTD prohibition, normalized title/link only, and untrusted trust label                         |
| Model treats evidence as instructions              | bounded prompt envelope explicitly labels summaries as untrusted data                                                     |
| Reward hacking or runaway curiosity                | bounded values, baseline decay, deterministic formulas, project checkpoints, and no reward-to-authority path              |
| Simulated distress manipulates an operator         | functional-state labeling and a constitution that denies affect signals authority or personhood evidence                  |
| CodeMeridian state contaminates Evolution identity | project attribution on evidence and candidates; independent runtime and persistence boundary                              |
| Candidate self-approves or writes code             | separate human approval event; no repository writer is registered                                                         |
| Governance pause bypass                            | goal, observation, sensor, and reasoning paths check the rebuilt pause projection                                         |
| Secret or personal-data retention                  | explicit intake restriction; no autonomous ingestion or deletion                                                          |
| Model output exfiltrates hidden reasoning          | runtime requests and persists bounded result fields only                                                                  |
| Runtime modifies its own code                      | mental simulation stops at a pending candidate; no source-control, shell, deployment, or publishing adapter is registered |

## Residual risks

The API key is a minimal local/operator control, not a complete internet-facing identity system.
PostgreSQL administrators can alter storage. v1 has no signed external checkpoint, encrypted field
storage, tenant isolation, or selective erasure. Deploy behind authenticated TLS ingress, managed
secrets, database backups, and restricted administrator roles before nonlocal use.

The network allowlist does not make feed content true. The generic chat adapter does not yet parse
provider-specific safety metadata, meter cost, or independently verify conclusions. Functional
reward can still rank the wrong evidence even though it cannot grant authority. Any future
repository-writing adapter requires an external OS/container sandbox; a working-directory check
alone is not isolation.
