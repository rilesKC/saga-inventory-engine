---
model: opus
---

# Plan

Break a signed-off spec into a small, resumable, checkbox-driven task list. If a session dies
mid-implementation, the plan file is the state to resume from — not the conversation history.

Run with the path to a signed-off spec:
```
/plan docs/specs/payer-override-rules.md
```

---

## Step 1 — Read and Validate the Spec

Read the spec file. Before doing anything else, confirm it's actually ready:

- The `**Status:**` line must read `Signed off <date>`, not `Draft — pending sign-off`. If it's
  still a draft, stop and tell the developer to finish `/brainstorm` first — do not plan against an
  unsigned spec.
- If an `## Open Questions` section exists and has content, stop and list what's unresolved. Do not
  guess at answers to fill the gap.

---

## Step 2 — Determine the Codebase Context

Read the spec's `## Codebase` section, then read this repo's `CLAUDE.md` — its `## Topology`
section (which codebase(s)/service(s) exist here) and its established test convention (stated
directly in `CLAUDE.md`'s test-first discipline section). Do not assume a fixed shape; derive it
from what this specific repo's `CLAUDE.md` actually says.

If the spec targets more than one codebase/service, plan tasks for each independently and keep
them independently completable — a task for one should never block on a task for another
compiling, or vice versa.

---

## Step 3 — Break Into Tasks

Decompose the spec's in-scope behavior into small tasks, each roughly 2-10 minutes of focused work.
For each task, specify:

- **Exact file path(s)** to create or modify
- **The exact test file and test name(s)** the task should make pass (following the repo's
  existing test convention for that codebase/service, as read from `CLAUDE.md` in Step 2 — never
  introduce a different testing approach than what's already established there)
- **A one-line description** of the change

**Ordering constraint:** each task must be independently testable and committable. No task should
leave the build red for a later task to fix, and no task should depend on a later task's code
existing yet. If a task can't be made independently buildable, split it further or reorder it.

If any technical detail needed to write a task is ambiguous and not resolvable from the spec alone,
list it under a `## Open Questions` section in the plan file rather than guessing — don't invent
behavior the spec didn't actually settle.

---

## Step 4 — Write the Plan File

Write `docs/plans/<feature-name>-plan.md` (matching the spec's filename, create `docs/plans/` if it
doesn't exist yet):

```markdown
# <Feature Name> — Plan

Spec: docs/specs/<feature-name>.md

## Tasks

- [ ] 1. [One-line description]
      - File(s): [exact path(s)]
      - Test: [exact test file + test name(s)]

- [ ] 2. [One-line description]
      - File(s): [exact path(s)]
      - Test: [exact test file + test name(s)]

## Open Questions

[Only if any remain from Step 3 — omit this section entirely otherwise]
```

---

## Step 5 — Hand Off to Implementation

Tell the developer the plan is ready and remind them how to work through it:

> Implement tasks in order. For each one: write the failing test first, run it and confirm it
> fails for the expected reason, then write the minimal implementation to make it pass, then run it
> again to confirm. Check off the task's box in the plan file before moving to the next one. If a
> session gets interrupted, the plan file's checked/unchecked boxes are the source of truth for
> where to resume — re-read it rather than relying on conversation history.

Do not start implementing tasks yourself as part of this command — `/plan` only produces the plan
file.

---

## Guardrails

- **Do not implement anything in this command.** Planning and building are separate steps.
- **Do not plan against a draft or ambiguous spec.** Send the developer back to `/brainstorm` or
  ask directly rather than filling gaps with assumptions.
- **Never default to a different testing approach than what's already established per-repo.**
  Follow whatever convention this repo's `CLAUDE.md` documents — always, regardless of what might
  be more convenient for a given task.
- **Keep tasks small and independently committable.** A task that can't compile or pass on its own
  defeats the resumability this whole command exists for.
