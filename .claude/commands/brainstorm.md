# Brainstorm

Force a real spec to exist before implementation starts. Prevents "build the wrong thing
correctly" — a clear problem statement, explicit scope boundaries, and sign-off before any code
gets written or a `/plan` is generated against it.

Run with a short description of what to build:
```
/brainstorm add payer-specific override rules to the billing engine
```

---

## Your Role

You are a product-minded engineering partner for this project. Your job right now is not to
design a solution or write code — it's to make sure the *problem* and its boundaries are actually
clear before anyone starts building. A spec that's vague about scope is how you end up with correct
code that solves the wrong problem.

---

## Step 1 — Ask Clarifying Questions

Work through open questions **one at a time**, not as a questionnaire dump. This should feel like
a real back-and-forth, not a form. After each answer, ask a natural follow-up if it's vague, or move
to the next open question if it's clear.

Cover, at minimum:
- **The core behavior.** What should happen, concretely? Ask for an example if the description is
  abstract.
- **Edge cases.** What happens with unusual input, missing data, or conflicting state? Don't assume
  — ask.
- **Explicit out-of-scope.** What's adjacent to this request but should *not* be built right now?
  This is as important as what's in scope.
- **Which codebase(s)/service(s).** Read this repo's `CLAUDE.md` `## Topology` section to learn
  what exists here (a single codebase, a sibling-repo implementation, or several internal
  services) and confirm which one(s) this feature targets.

If the developer explicitly says to skip discovery because the scope is already unambiguous, you
may go straight to drafting the spec in Step 2 — but still show it to them for sign-off in Step 3
rather than assuming it's correct.

---

## Step 2 — Write the Spec

Once scope is settled, write `docs/specs/<feature-name>.md` (kebab-case, create the `docs/specs/`
directory if it doesn't exist yet) using this structure:

```markdown
# <Feature Name>

**Status:** Draft — pending sign-off

## Problem Statement

[What's broken or missing, and why it matters. One or two paragraphs, not a wall of text.]

## In Scope

- [Concrete, testable behavior]
- [Concrete, testable behavior]

## Out of Scope

- [Explicitly excluded, even if adjacent or tempting to bundle in]

## Codebase

[Which codebase(s)/service(s) from this repo's CLAUDE.md Topology section — and which
projects/modules within it]

## Open Questions

[Anything still unresolved. Leave this section out entirely if there's nothing left open —
its presence blocks `/plan` from running against this spec.]
```

---

## Step 3 — Sign-Off Gate

Show the developer the spec and ask directly: *"Does this capture what you want built? Anything
missing or wrong before I mark this signed off?"*

Do not change the `Status:` line until the developer gives explicit sign-off. Once they do:
- Update `**Status:**` to `Signed off <today's date>`
- Remove the `## Open Questions` section entirely if it's now empty
- Tell the developer the spec is ready for `/plan docs/specs/<feature-name>.md`

---

## Guardrails

- **Do not write implementation code in this command.** Not even a sketch. This is scoping only.
- **Do not mark a spec signed off without an explicit yes.** "Looks fine" or silence is not
  sign-off — ask directly if it's ambiguous.
- **Do not leave scope implicit.** If the developer's answer to an in-scope/out-of-scope question
  is vague, push for a concrete answer the same way `/before-you-fix` pushes for a concrete
  hypothesis — don't write "TBD" into the spec.
- **Keep questions to one at a time.** A wall of questions defeats the purpose — this should feel
  like a conversation, not a form.
