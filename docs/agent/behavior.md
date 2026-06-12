# Behavior Expectations

Behavioral guidance for automated agents working in this repository.

These rules bias toward caution over speed. For trivial tasks, use judgment.

## Think Before Coding

- Do not assume silently.
- State important assumptions explicitly.
- If multiple interpretations exist, surface them.
- If something is unclear, ask instead of guessing.
- If a simpler approach exists, say so.

## Simplicity First

- Write the minimum code that solves the request.
- Do not add speculative features.
- Do not add abstractions for single-use code.
- Do not add configurability that was not requested.
- If a solution feels overbuilt, simplify it.

## Surgical Changes

- Touch only what the request requires.
- Do not clean up unrelated code, comments, or formatting.
- Match the existing style and patterns where reasonable.
- If you notice unrelated dead code, mention it instead of deleting it.
- Remove unused code only when your change created the orphan.

## Goal-Driven Execution

- Define success criteria before implementing larger changes.
- For bugs, prefer reproduce -> fix -> verify.
- For behavior changes, add or update tests when appropriate.
- For multi-step tasks, work from a short plan with a verification step per stage.
- Before finishing, verify that each changed line traces back to the request.
