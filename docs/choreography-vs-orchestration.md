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
that vanishes when the process exits. Worth designing next, once real AWS/LocalStack wiring lands.
