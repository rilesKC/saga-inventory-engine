# Choreography vs. Orchestration — A Comparison From Having Built Both

Both implementations of the Order → Inventory → Payment → Shipping saga are complete and tested:
`src/OrderSaga.Choreography` (participants react to each other's published events, no central
coordinator — [spec](specs/order-saga-choreography.md) / [plan](plans/order-saga-choreography-plan.md))
and `src/OrderSaga.Orchestration` (a central `SagaCoordinator` issues commands and reacts to
replies — [spec](specs/order-saga-orchestration.md) / [plan](plans/order-saga-orchestration-plan.md)).
Both share the same `EventBus` and `OrderPlaced` trigger (`src/OrderSaga.Shared`), the same
business rules (payment threshold, single line item, Shipping always succeeds), and the same
underlying `Inventory.Domain` aggregate — so what follows is a comparison of coordination style,
not of two different business problems.

## The short version

| | Choreography | Orchestration |
|---|---|---|
| Correlating `Amount` for Payment | Side-channel: `PaymentStub` remembers it from an earlier `OrderPlaced` subscription | Free: `SagaCoordinator` already holds it, passes it straight into the command |
| Where does "saga progress" live? | Nowhere centrally — no component knows the whole picture | `SagaCoordinator`'s `SagaState`/`SagaStep`, queryable per order |
| Exposure to the `EventBus` ordering bug | Real — two participants (`InventoryParticipant`, `PaymentStub`) both reacted to `OrderPlaced` | None — linear command→reply chain, no two participants ever race for the same trigger |
| Individual component size | `PaymentStub`: 38 lines | `PaymentResponder`: 28 lines |
| Total implementation size | 132 src / 221 test lines | 216 src / 349 test lines |
| Type count | 4 shared event types (dual-purpose: notification out *and* trigger in) | 12 command/reply types (one direction each) + `SagaState`/`SagaStep` |
| Adding a new participant/step | Subscribe a new participant to an existing event — no existing code changes | Edit `SagaCoordinator` directly — new command/reply pair, new step, new subscription |
| Where do you look to understand the whole flow? | Nowhere — read (or run) all four participants and trace the pub/sub graph yourself | `SagaCoordinator.cs` — the whole flow, sequential, top to bottom |

## The details, grounded in what actually got built

### 1. Correlating cross-step data

`PaymentStub` needs `Amount` to evaluate its threshold, but `StockReserved` (the event that
triggers it) doesn't carry `Amount` — correctly, since dollar amount isn't Inventory's concern. So
`PaymentStub` also subscribes to `OrderPlaced`, purely to remember `Amount` per `OrderId` in a
dictionary, so it's available later when `StockReserved` arrives.

`PaymentResponder` needs the exact same value, but never builds a side-channel at all —
`ChargePaymentCommand` carries `Amount` directly, because `SagaCoordinator` already had it in
`SagaState` from the moment `OrderPlaced` arrived and put it straight into the command it issued.

This isn't a minor implementation detail. It's the direct, structural consequence of *where state
lives* in each pattern: choreography has no shared context object, so any data a later step needs
that an earlier step produced has to travel through a participant's own memory, correlated by
whatever ID is available. Orchestration's central coordinator *is* that shared context, by
construction.

### 2. The ordering bug — and why it was structural, not incidental

The choreography build hit a real bug: `EventBus`'s original depth-first `Publish` let
`InventoryParticipant`'s reaction to `OrderPlaced` (which immediately publishes `StockReserved`)
run all the way through `PaymentStub`'s `StockReserved` handler *before* `PaymentStub`'s own
`OrderPlaced` handler — sitting right behind `InventoryParticipant`'s in the same subscriber list —
ever got to run. `PaymentStub` tried to look up an `Amount` that hadn't been recorded yet.
`KeyNotFoundException`. Fixed with breadth-first dispatch (queue nested publishes, drain after the
current event's handlers finish), now shared infrastructure both implementations rely on.

The orchestration build never hit this, and it's not luck: no two orchestration participants ever
subscribe to the same trigger event. Each command has exactly one intended handler. Choreography's
whole model is *multiple independent reactions to the same broadcast event* — which is also
exactly the shape that creates ordering hazards the moment one of those reactions has a side effect
another one depends on. The more participants react to a shared event in a real choreography
system, the more this risk compounds; orchestration doesn't eliminate ordering concerns entirely
(a coordinator can still race against itself across concurrent orders), but it removes this
specific class of same-event-multiple-reactor hazard by design.

### 3. Simpler pieces, more total machinery

`PaymentResponder` (28 lines) is meaningfully smaller than `PaymentStub` (38 lines) — no
correlation dictionary, no second subscription. That pattern holds across the responders generally:
none of them need to reason about "what does a sibling participant expect me to have already
done," because they only ever react to a command addressed directly to them.

But the *system* is bigger: 216 source lines and 349 test lines for orchestration versus 132 and
221 for choreography. The difference is real machinery, not padding — a dedicated `Commands.cs`
and `Replies.cs` (12 single-purpose types, one per direction, versus choreography's 4 dual-purpose
event types that serve as both the "I did this" notification and the "you should react" trigger),
plus `SagaState`/`SagaStep` and the coordinator itself. Choreography pushes complexity out into
implicit coordination between participants; orchestration makes that coordination explicit and
central, which costs lines but buys legibility.

### 4. Extending each one

Adding a new participant to choreography that reacts to an existing event — say, a fraud-check
service that reacts to `OrderPlaced` alongside the existing ones — requires touching nothing that
already exists. Subscribe the new participant, done. (The tradeoff: nobody reading the existing
code would know that participant exists without searching for who else subscribes to `OrderPlaced`.)

Adding an equivalent step to orchestration means editing `SagaCoordinator` directly: a new command,
a new reply, a new `SagaStep` value, a new pair of subscriptions, and a decision about where in the
sequence it fits. More change surface, concentrated in one file — a real coupling cost, but also
the reason `SagaCoordinator.cs` stays a complete, honest description of the saga rather than a
partial one.

## Mapping to a real AWS-native stack

This project's in-process `EventBus` is a stand-in for real infrastructure, but the shapes map
directly: orchestration here is the same idea as an AWS Step Functions state machine (or an
EventBridge-driven DAG with an explicit definition) — a central definition of the flow, calling out
to services and reacting to their results. Choreography here is the same idea as services
independently publishing and subscribing to EventBridge rules with no central definition anywhere
— the flow only exists as the sum of everyone's individual subscriptions.

## What this comparison doesn't cover

This is all in-process, synchronous, single-machine, and in-memory — real infrastructure would
surface questions this project hasn't touched yet: what happens when a service crashes mid-saga
(orchestration's `SagaState` needs to survive that; choreography's implicit state needs a different
recovery story entirely), how idempotency-by-claim and DLQ-backed retry change these designs once
delivery is at-least-once over a real queue instead of guaranteed in-process, and how each pattern
shows up in a real audit trail once events are actually persisted rather than living in a `List`
that vanishes when the process exits. Worth designing next, once real AWS/LocalStack wiring lands —
see below for what actually happened once it did, for both.

## Now that both are actually deployed

Both implementations have since been deployed to real AWS and torn down cleanly (see
`docs/specs/choreography-aws-infra.md` / `docs/plans/choreography-aws-infra-plan.md` and
`docs/specs/orchestration-aws-infra.md` / `docs/plans/orchestration-aws-infra-plan.md` for what that
actually took). A few things the in-process comparison above couldn't have predicted:

### The choreography-vs-orchestration split shows up as an infra decision, not just a code one

Choreography's four participants are independent reactors with no reason to be separate
processes — one shared Fargate service was the natural fit. Orchestration's coordinator-plus-responders
shape is different enough that collapsing it into one shared service would just have been
choreography's topology wearing different code; it deployed instead as three separate services
(Coordinator, Inventory, and a combined stateless Payment+Shipping responder). That split also made
a real scaling difference visible for the first time: the two responders holding no cross-order state
(`PaymentResponder`, `ShippingResponder`) run `desired_count = 2` today, for free — something neither
implementation could demonstrate while everything lived in one process.

### EventBridge earns its place in exactly one of the two

Choreography's pub/sub shape needs EventBridge — participants broadcast, and rule-based routing is
what turns "a `StockReserved` event happened" into "every interested participant gets it."
Orchestration's commands are all point-to-point (`SagaCoordinator` addresses exactly one intended
consumer per command), so EventBridge would have added routing infrastructure with nothing to route.
Orchestration's real deployment uses direct SQS instead, no EventBridge at all — a difference in the
real infrastructure that only became visible once orchestration was made to answer "how does this
actually get from the coordinator to a responder," not just "who calls what in-process."

### The `EventBus` republish-loop bug recurred right on schedule

Choreography's real AWS work surfaced (and fixed) an ordering bug where two participants reacting to
the same event could interleave in a way that broke correlation (see "The ordering bug" above).
Wiring orchestration's four responders onto real SQS hit the exact same class of bug for a different
reason — sharing one `EventBus` for both inbound (SQS→bus) and outbound (bus→SQS) traffic let a
participant's own outbound publish loop back through its own inbound subscriptions in-process,
completely bypassing SQS. Both implementations needed the same fix: split into an `InboundEventBus`
and `OutboundEventBus` (compile-time-distinct wrapper types around the same underlying `EventBus`),
so the compiler — not just code review — rejects the two ever getting swapped again.

### Two bugs only real AWS, not LocalStack, could surface

- **A missing IAM permission.** The coordinator needed `sqs:SendMessage` on its *own* inbound queue
  (it publishes the initial `OrderPlaced` trigger onto the same queue it polls), not just the two
  command queues it fans out to. LocalStack doesn't enforce IAM at all, so this only showed up as an
  HTTP 500 against real AWS.
- **A `null`-vs-empty-list SDK quirk, twice.** Both `ReceiveMessageResponse.Messages` and
  `DeleteMessageBatchResponse.Failed` come back `null` (not an empty list) on real AWS when there's
  nothing to report; LocalStack's emulation returns `[]` instead. The first occurrence (choreography)
  got fixed and code-reviewed; the second occurrence (orchestration, different file, same defect
  class) shipped anyway, because the two Hosts' AWS plumbing was independently duplicated rather than
  shared. Only real AWS deployment caught it. The plumbing has since been consolidated into one
  shared `OrderSaga.Aws` project specifically so a fix like this only ever has to be made once.

### A live demonstration of the persistence gap this project already named as out of scope

Redeploying orchestration's fixed Docker images produced a stretch where two ECS tasks (old and new)
both polled the same queue for several minutes — ECS's `rolloutState` reports `COMPLETED` before the
old task's background poller actually stops. That's not a new bug; it's the already-documented
`SagaState`-isn't-persisted gap (see "Out of Scope" in `docs/specs/orchestration-aws-infra.md`)
showing up live during a deploy instead of only in a design doc. The DLQ-backed retry pattern
absorbed it exactly as designed — both affected messages landed in the DLQ rather than being
silently lost or double-processed. (This gap has since been closed — see "Now that persistence is
real too" below.)

## Now that persistence is real too

The `SagaState`-isn't-persisted gap named above (and in `docs/specs/orchestration-aws-infra.md`'s
"Out of Scope") has since been closed for both `InventoryItem` and `SagaState` — see
`docs/specs/saga-persistence.md` / `docs/plans/saga-persistence-plan.md`. A few things that real
persistence work surfaced, beyond just "state now survives a restart":

### Persisting state didn't make multi-instance safe by itself

The original assumption was that adding a durable store would be enough to raise both stacks'
`desired_count` above 1. It wasn't: `InventoryParticipant`/`InventoryResponder`/`SagaCoordinator`
were each still mutating one long-lived in-memory object per process, loaded once at startup —
Mongo made that survive a *restart*, but two *simultaneously running* instances never re-synced
with each other. The actual fix was optimistic concurrency (an expected-version check on every
write, retried on conflict), not just "write to Mongo instead of a `List`."

### The same concurrency bug recurred three times in three different classes

Found once in `InventoryParticipant`/`InventoryResponder`, then proactively in `SagaCoordinator`
(caught by code inspection before it failed live), then a third time in choreography's
`PaymentStub`, which cached `Amount` from `OrderPlaced` in a plain dictionary — the same
"per-process cache breaks under `desired_count >= 2`" bug shape, just in a component the
persistence plan hadn't touched at all since it has no store of its own.

### A namespace collision silently defeated the fix for one of the three

`InventoryResponder`'s retry loop lived in the same C# namespace as a second, identically-named
`ConcurrencyConflictException` (added for `SagaCoordinator`'s own store) — its `catch` clause
silently bound to the wrong type, and the regression test written to prove the retry worked had
the identical bug in its own fake, so it passed while testing nothing real. Only caught by a
dedicated code review after the real deployment, not by any of the tests or the live validation
pass itself.

### Real AWS/Atlas deployment found bugs no unit test or LocalStack could

LocalStack doesn't run MongoDB (Atlas isn't an AWS service), so none of this session's
concurrency, index-contamination, or Mongo-serialization bugs was ever visible until the real
deployment — including one genuine live outage (the coordinator crash-looping on every restart
once real `SagaState` data existed, from deserializing Mongo's own auto-generated `_id` field into
a type with no property for it), caught and fixed within the same deployment window.

## Now that a full-solution review closed the remaining gaps

A `/code-review` run against the whole solution (not just a diff) found 10 findings spanning
security (Terraform) and application logic. Two of the fixes are genuinely about the
choreography-vs-orchestration split itself, not just generic hardening:

### The event-loss-on-publish-failure bug is real in exactly one of the two stacks

Both `InventoryParticipant.ApplyWithRetry` (choreography) and `InventoryResponder.ApplyWithRetry`
(orchestration) looked identical at a glance: append an event to the durable store, then publish
it. The initial assumption was that both needed the same fix. Tracing each one carefully during
planning showed that's wrong: choreography's version really did lose events (append succeeds,
publish fails or the process crashes in between, and a redelivery's `Handle()` call sees the
reservation already in its target state and skips the retry entirely) — but orchestration's
`ApplyWithRetry` has no publish step at all. It only appends; each command handler publishes its
*reply* separately, after `ApplyWithRetry` returns, and an earlier fix in the same session
(catching `InvalidReservationStateException` and still replying) already made that redelivery-safe.

This is the same asymmetry the "EventBridge earns its place in exactly one of the two" section
above already named, showing up again for a different reason: choreography's participants depend
on receiving the actual domain event (nobody else is going to tell them), while orchestration's
`SagaCoordinator` only ever needs a yes/no reply, and a reply is trivially safe to resend on
redelivery in a way a domain event silently isn't. The fix — a transactional outbox folded into the
event store itself (a `Published` field on the same atomically-written document, no second
collection or Mongo transaction needed) plus a new `OutboxDrainerBackgroundService` — only exists
in choreography's Host. Orchestration's `InventoryResponder` is untouched.

### A stale-reply regression risk in orchestration's coordinator, found by design reasoning alone

Designing the redelivery-safe reply for `InventoryResponder` raised a question with no
choreography equivalent: what happens if a stale or duplicate *reply* arrives at `SagaCoordinator`
after the saga has already moved past the step that reply expects? Its handlers applied `Step`
transitions unconditionally, with no check against the saga's current step — a redelivered
`StockReservedReply` arriving after the saga had already advanced to `Confirming` could regress
`Step` back to `AwaitingPayment` and re-issue `ChargePaymentCommand`. This class of bug has no
analogue in choreography: there's no central `Step` for a stale event to regress, just independent
participants each reacting to whatever they're subscribed to. Centralizing saga progress buys
legibility (see "Where does 'saga progress' live?" above) at the cost of a new, coordinator-specific
failure mode that choreography's design doesn't have room for in the first place. Fixed by
generalizing the coordinator's own retry-transition delegate to allow a no-op (the same idiom the
Inventory-side `ApplyWithRetry`s already used) plus a shared step-precondition guard.

## Now that observability is real too

Both stacks got instrumented with New Relic APM, including manual distributed tracing across the
message-queue boundaries the agent can't auto-instrument (see `docs/specs/` isn't the right place
for this one — it wasn't spec-driven; it was a direct response to a target role's stated stack).
The interesting part isn't the APM setup itself (identical NuGet package, identical Dockerfile env
vars, identical Terraform wiring, on all four services) — it's what tracing revealed about the
outbox pattern the last section just finished describing.

### The outbox pattern has a tracing cost neither section above priced in

Choreography's `InventoryParticipant` durably appends an event, then a separate background
service (`OutboxDrainerBackgroundService`) publishes it later, on its own timer — that's the whole
point of the outbox fix two sections up, decoupling the publish from the original request so a
crash between the two can't lose the event. But it means the transaction that originally caused
the event (an HTTP request, or an earlier message's processing) has already *ended* by the time
the drainer gets around to publishing. A distributed trace literally cannot span that gap without
lying about timing, so the drainer gets its own transaction instead of faking a continuation.

Orchestration has no outbox — every one of its `InventoryResponder` handlers publishes a reply
synchronously, in the same transaction that received the command, because that reply's whole job
is to unblock `SagaCoordinator`, which is still waiting. So orchestration's traces stay unbroken
end to end, choreography's don't past the first drain cycle. The same reliability fix that makes
choreography's event delivery durable is exactly what makes its own tracing discontinuous — a
tradeoff neither the original comparison nor the outbox-pattern section predicted, because neither
was asking "what does this do to observability" at the time. Verified live: the specific number
that proved trace continuity was working (message consumptions that started their own independent
trace, rather than joining their publisher's) dropped from 3 to 0 as the deployment's startup
window aged out of the lookback — every steady-state message after that successfully joined an
existing trace, confirming the boundary is exactly where the outbox sits and nowhere else.
