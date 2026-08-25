# Orchestration — Real AWS Infrastructure

**Status:** Signed off 2026-08-25

## Problem Statement

Choreography's AWS deployment (`docs/specs/choreography-aws-infra.md`) validated one half of this
project's core comparison for real — EventBridge pub/sub, SQS delivery, DynamoDB idempotency, and
Fargate compute, all actually running, not just reasoned about in-process. The orchestration
implementation (`src/OrderSaga.Orchestration`) still only exists as in-process domain code with an
in-memory bus. This spec moves it onto real AWS too, so the choreography-vs-orchestration comparison
can finally be made from having deployed both, not just built both.

The deployment shape is deliberately different from choreography's, not a copy of it. Choreography's
participants are independent reactors with no reason to be separate processes, so one shared Fargate
service made sense. Orchestration's actual shape is a central coordinator plus responders reacting to
targeted commands — collapsing that into one shared service again would just be choreography's
topology wearing different code. Splitting it into real separate services is what makes this
deployment worth building at all: it's the first place in the whole project where the
choreography-vs-orchestration tradeoff shows up as an infrastructure difference, not just a code
difference. Two concrete, demonstrable contrasts fall out of actually deploying it this way:

- **EventBridge doesn't earn its place here.** Every orchestration command has exactly one possible
  consumer (the coordinator addresses `ReserveStockCommand` at the Inventory responder and nothing
  else could ever want it) — there's no fan-out for EventBridge's rule-based routing to do anything
  useful with. Choreography needs EventBridge because it's broadcast; orchestration doesn't, because
  every message is already point-to-point. This spec uses direct SQS queues, no EventBridge, at all.
- **Statefulness, not "is this a participant," is what determines whether a service can scale.**
  `PaymentResponder` and `ShippingResponder` hold no cross-order state today — the coordinator now
  carries `Amount` directly on `ChargePaymentCommand`, so neither responder needs the
  correlate-by-side-channel trick choreography's `PaymentStub` needed. They can run
  `desired_count >= 2` today, for free, with zero persistence work. `InventoryResponder` (same
  `InventoryItem` in-memory state as choreography) and the `SagaCoordinator` itself (a new kind of
  state that didn't exist anywhere in choreography — explicit per-order `SagaState`, tracking where
  each order is in the flow) both cannot, and both stay pinned to `desired_count = 1` for the same
  reason choreography's single service was.

## In Scope

- **Deployment topology: three separate ECS Fargate services**, not one shared service:
  - **Coordinator** — hosts `SagaCoordinator` plus the HTTP intake endpoint (`POST /orders`,
    `GET /health`). The only one of the three behind an ALB, since it's the only one with any HTTP
    surface. `desired_count = 1` — `SagaState` is in-memory (see Out of Scope).
  - **Inventory responder** — hosts `InventoryResponder` against the same `InventoryItem` in-memory
    model choreography used. `desired_count = 1`, same reasoning, same limitation, named just as
    honestly here as it was there.
  - **Stateless responders** — hosts `PaymentResponder` and `ShippingResponder` together in one
    service (both stateless, no differentiating reason to separate them further).
    `desired_count >= 2` — the one genuinely different, better outcome this topology makes possible
    compared to choreography, where nothing currently runs above `desired_count = 1`.
- **Transport: direct SQS, no EventBridge.** Three queues, each with its own DLQ + redrive policy
  (small `maxReceiveCount`, exact value an implementation detail, matching choreography's pattern):
  - `inventory-commands` (`ReserveStockCommand`, `ConfirmReservationCommand`,
    `ReleaseReservationCommand`) — polled by the Inventory responder service.
  - `stateless-responder-commands` (`ChargePaymentCommand`, `ScheduleShipmentCommand`) — polled by
    the Stateless responders service.
  - `coordinator-inbound` (`OrderPlaced` plus all seven reply types) — polled by the Coordinator
    service. Carries `OrderPlaced` as well as replies, not a separate intake path.
  The Coordinator publishes each command directly to whichever queue owns that command type; both
  responder services publish every reply back onto the single `coordinator-inbound` queue.
- **HTTP intake still round-trips through the real transport.** The Coordinator's HTTP handler
  publishes `OrderPlaced` onto `coordinator-inbound` via the SQS client directly (no EventBridge, no
  calling `EventBus.Publish` straight from the HTTP handler) — the same "every event takes the real
  transport path, no special-cased internal dispatch" principle choreography's Host used, adapted to
  a transport that's SQS-only.
- **One shared, new DynamoDB idempotency table** (claim-before-emit, conditional `PutItem`, same
  pattern as choreography's), keyed by the same per-message GUID scheme, referenced by all three
  services' IAM roles. Not a reuse of choreography's table — a distinct system, its own table.
- **Message envelope**: same shape/pattern as choreography's `EventEnvelope` (message ID, a type
  discriminator, and a payload that stays a real nested JSON value rather than a
  pre-serialized/re-escaped string) — reused directly where the type fits, adapted only as needed for
  commands vs. replies vs. the `OrderPlaced` trigger.
- **The two non-HTTP services (Inventory responder, Stateless responders) rely on ECS's own
  task-health signal**, not a target-group health check — there's no ALB in front of either one to
  check against.
- **Terraform**: reuses the existing `networking`, `load-balancer`, `iam-and-observability`, and
  `compute` modules from choreography's infra (generic enough to instantiate once per service where
  applicable — `load-balancer` only once, for the Coordinator). A new SQS-only messaging module
  (three queues + three DLQs + redrive policies, no EventBridge bus or rules) replaces
  choreography's EventBridge-based one for this deployment. New root Terraform config under
  `infra/orchestration/` alongside (not replacing) the existing `infra/` choreography config.
- **Validation**: LocalStack first (SQS, DynamoDB), then one real minimal AWS deployment, verified
  and torn down — same established project pattern.

## Out of Scope

- **`SagaState` persistence.** Stays in-memory, `desired_count = 1` on the Coordinator, same as
  choreography's `InventoryItem` gap. If the Coordinator process restarts mid-saga, any in-flight
  order's state is lost silently — no compensating action fires, nothing surfaces as an error. Named
  honestly here rather than solved, to be addressed together with `InventoryItem` persistence in a
  future spec (both are the same underlying problem: real, shared, crash-safe state for the pieces
  of this system that hold it).
- **`InventoryItem` persistence** — already out of scope per choreography-aws-infra, still deferred
  here, same future spec.
- **Splitting Payment and Shipping into their own separate services** — deliberately kept together;
  both are equally stateless, so there's no scaling-story difference to demonstrate by separating
  them further.
- **EventBridge** — not used anywhere in this deployment. If a future change introduces genuine
  fan-out (a command or reply more than one service needs to react to), that would be the trigger to
  revisit this decision, not something to build preemptively now.
- **DLQ alerting/alarms** — same as choreography, messages landing in a DLQ are visible via the AWS
  console; automated alerting is a future concern.
- **Multi-region** — multi-AZ only, per the project's standing scope.
- **Any change to `OrderSaga.Orchestration`'s own business logic** (`SagaCoordinator`,
  `InventoryResponder`, `PaymentResponder`, `ShippingResponder`) — this spec adds a transport/infra
  layer around already-built, already-tested code, not new domain behavior.
- **Reusing choreography's existing DynamoDB idempotency table or Terraform state** — orchestration
  gets its own fresh stack under `infra/orchestration/`, even though choreography's is currently torn
  down and there'd be no live collision either way.

## Codebase

`saga-inventory-engine` repo. Three new Host projects, each referencing `OrderSaga.Orchestration`,
`OrderSaga.Shared`, and `Inventory.Domain` as needed:

- `src/OrderSaga.Orchestration.CoordinatorHost` — `SagaCoordinator`, HTTP intake endpoint, SQS
  publisher(s) for the two command queues, SQS poller for `coordinator-inbound`.
- `src/OrderSaga.Orchestration.InventoryHost` — `InventoryResponder`, SQS poller for
  `inventory-commands`, SQS publisher back to `coordinator-inbound`.
- `src/OrderSaga.Orchestration.ResponderHost` — `PaymentResponder` + `ShippingResponder`, SQS poller
  for `stateless-responder-commands`, SQS publisher back to `coordinator-inbound`.

New `infra/orchestration/` directory (root Terraform config: `main.tf`/`variables.tf`/`outputs.tf`/
`versions.tf`) alongside the existing `infra/` (choreography's, untouched), referencing
`infra/modules/{networking,load-balancer,iam-and-observability,compute}` and a new
`infra/modules/orchestration-messaging`. Unit-testable logic (message envelope
serialization/deserialization, command-to-queue routing, claim-check) follows the repo's xUnit v3 +
manual-test-double convention; actual AWS behavior is verified against LocalStack, not unit tests.
