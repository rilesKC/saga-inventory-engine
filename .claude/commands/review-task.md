---
model: opus
---

# Review Task

Two-stage review of the most recently completed task from an active `/plan` plan file, run before
moving to the next task (or before `/pre-flight`, if the plan is finished). Catches problems while
context is still fresh, at the task level rather than waiting for one large end-of-branch review.

Run with no arguments to auto-detect the active plan:
```
/review-task
```

Or pass a specific plan file if more than one exists with unchecked boxes remaining:
```
/review-task docs/plans/payer-override-rules-plan.md
```

---

## Step 1 — Find the Active Plan and Task

If no plan file was given as an argument, look in `docs/plans/` for files with at least one
unchecked `- [ ]` box remaining. If exactly one such file exists, use it. If zero or more than one
exist, list the candidates and ask the developer which one to review against — do not guess.

Within that plan file, the task to review is the **last checked `- [x]` box**, read top to bottom
(tasks are worked in the order `/plan` produced them). Read that task's full description: the file
paths, the exact test name(s), and the one-line summary of what it should do.

---

## Step 2 — Get the Diff

Run:
```bash
git diff --staged
```

If nothing is staged, fall back to:
```bash
git diff HEAD
```

If neither produces output, tell the developer there's nothing to review and stop — don't review
against a task description with no corresponding code.

---

## Step 3 — Functional Pass

Check the diff against the task's own description, not against your own idea of how you'd have
built it:

- **Does the implementation match what the task said?** Same file paths, same behavior. Flag scope
  creep (changes beyond what this task described) as well as gaps (parts of the task description
  not actually implemented).
- **Does the new test actually test the right thing?** A test that passes but doesn't exercise the
  behavior the task describes is worse than no test — it creates false confidence. Check the
  assertion actually corresponds to the stated behavior, not just that a test with a plausible name
  exists.
- **Any obvious logic errors?** Off-by-one, wrong operator, inverted condition, wrong variable used
  in a similar-looking line — the kind of thing that compiles and often even passes a shallow test
  but is still wrong.

---

## Step 4 — Quality Pass

- **Readability and naming.** Does this read the way the surrounding code already reads, or does it
  introduce a visibly different style?
- **Convention consistency.** Specifically check it didn't introduce a new testing pattern instead
  of following what's already established in this repo's `CLAUDE.md` — e.g. a newly-introduced
  mocking framework where manual test doubles are the convention, or a hand-rolled fake where a
  mocking framework is the convention, is a finding here even if the test itself is otherwise
  correct.
- **Unnecessary scope.** Per this repo's general scope-discipline convention: no premature
  abstraction, no unrequested refactor of surrounding code, no speculative generality for a
  requirement that doesn't exist yet.

---

## Step 5 — Plan-Decomposition and Trajectory Pass

Check whether this task's own implementation revealed anything about the quality of the plan's
breakdown, or about how cleanly this task's implementation session got there. This is separate
from Steps 3 and 4: those check the diff against the task's stated scope; this checks the task
against the plan itself, and the path taken to implement it.

**Plan-decomposition signals** — did this task's implementation reveal any of the following:

- **Ordering was wrong.** A change made in this task really belonged in an earlier task, or this
  task had to work around something an earlier task should have already handled.
- **Task was materially mis-sized.** This task turned out significantly bigger or smaller than its
  plan entry implied — not a trivial estimation gap, but a sign the plan's decomposition of this
  piece of work was off.
- **Left the build red for a later task to fix.** This task's own diff, taken alone, does not leave
  the build in a passing state — violating the plan's own rule that no task should leave the build
  red for a subsequent task to fix.
- **Plan needed a substantive edit.** Implementing this task required a real edit to the plan
  file beyond a checkbox flip (a task description had to be rewritten, a task had to be inserted,
  scope had to be moved between tasks).

**Trajectory signals** — did this task's own implementation process show:

- **Excessive backtracking.** Multiple false starts or abandoned approaches before landing on the
  one that shipped, beyond what the task's stated difficulty would suggest.
- **A missed existing convention.** The implementation ignored a convention already established
  elsewhere in the codebase that it should have found (e.g. by reading a neighboring file) before
  writing new code.
- **A shortcut that happens to pass tests without satisfying intent.** The test is green, but the
  path taken to get there sidesteps what the task actually asked for rather than fulfilling it.

If any signal fired, append a single-line flag directly under the task's `- [x]` checkbox line in
the plan file, indented as a sub-bullet:

```
- [x] 5. Add the X component
  - ⚠ Retro: <one-sentence explanation of what fired and why it matters>
```

If nothing fired, do not add a note — flagged tasks only, so the plan file stays readable. This is
the one exception to this command's report-only rule; see the Guardrails section for its exact
boundary.

**After writing a flag, confirm it actually landed before Step 6 reports anything.** Re-read the
plan file (or grep for the exact flag text just written) and check the line is really there. Do
not rely on having intended to write it — a write that silently didn't happen is exactly the
failure this confirmation step exists to catch.

---

## Step 6 — Deliver the Review

### Task Reviewed

**Plan:** [plan file path] — **Task [N]:** [one-line task description]

### Findings (if any)

For each finding, in severity order:

**[CRITICAL / WARNING / NOTE]** — [one-line title]

- **Pass:** Functional or Quality
- **Location:** [file:line]
- **What's wrong:** [one sentence]
- **Fix:** [specific, actionable]

**Severity guide:**
- `CRITICAL` — the implementation doesn't actually do what the task says, or the test doesn't
  actually verify the claimed behavior (false confidence).
- `WARNING` — works, but drifts from an established convention (wrong test-double style, scope
  creep beyond the task) in a way worth fixing before it compounds across later tasks.
- `NOTE` — minor readability/naming nit, not blocking.

### Plan-Decomposition and Trajectory Signal

State the outcome of Step 5 plainly, covering whichever of the two applies:

- **If a signal fired and the write was confirmed** (per Step 5's re-read check): state that a
  `⚠ Retro:` flag was written to the plan file, and quote its exact text.
- **If a signal fired but the write could not be confirmed:** say so plainly — e.g. "a signal
  fired but the flag failed to write; the plan file does not contain it." Never report a flag as
  written without having actually confirmed it in the file.
- **If nothing fired:** state that explicitly — no plan-decomposition or trajectory signal fired,
  no flag was written. This is a result worth stating, not something to leave implicit.

### Passed Checks

State plainly which of the Functional and Quality passes found nothing — "nothing found" is a
result worth stating, not something to leave implicit.

### Verdict

One line: **Clear to continue** (no CRITICAL findings) or **Fix before continuing** (at least one
CRITICAL finding) — plus a reminder of what's next: move to the next unchecked task in the plan, or
run `/pre-flight` if this was the last one.

---

## Guardrails

- **This is not `/pre-flight`.** Do not run builds, run the full test suite, or report on coverage
  percentages — that's `/pre-flight`'s job, and duplicating it here just adds latency without new
  signal. This command's value is reasoning about correctness and convention-fit, not re-running
  mechanical checks.
- **Do not fix anything, with one sole exception.** Report findings; the developer decides what to
  act on and does the editing. The single exception is Step 5's `⚠ Retro:` flag, written directly
  to the plan file when a plan-decomposition or trajectory signal fires. That exception is narrow:
  it never edits source code, never rewrites a task's description, and never unchecks or re-opens a
  task — it only adds a one-line annotation under a task already checked off.
- **Do not invent findings.** If you're not confident something is wrong, say so and name the
  specific evidence that would confirm or rule it out, rather than flagging speculatively.
- **Review against the task's own stated scope**, not a broader or narrower scope you'd have
  chosen. If the task description itself seems wrong in hindsight, say so explicitly rather than
  silently reviewing against a different, better version of it.
- **Never report a write as done without confirming it landed.** A `⚠ Retro:` flag that's claimed
  but missing from the plan file is worse than no flag at all — it creates false confidence in the
  plan's own record, and `/plan-retro` treats existing flags as established evidence without
  re-deriving them, so a phantom one is invisible to every downstream retrospective.
