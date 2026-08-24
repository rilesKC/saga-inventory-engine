# Order Saga — Orchestration

**Status:** Signed off 2026-08-24

## Problem Statement

The choreography implementation of the Order → Inventory → Payment → Shipping saga
(`src/OrderSaga.Choreography`) is complete: participants react to each other's published events,
with no central coordinator. This spec builds the orchestration counterpart — a central
`SagaCoordinator` that issues commands to Inventory/Payment/Shipping and reacts to their replies —
so the choreography-vs-orchestration tradeoff can be compared from having built both, not just
reasoned about. Same business rules and outcomes as choreography throughout; only the coordination
topology differs.

A genuine, demonstrable contrast worth building rather than just describing: because the
coordinator holds the full order context (`OrderId`, `Sku`, `Quantity`, `Amount`) for the whole
flow, it can put `Amount` directly into the `ChargePayment` command it issues — no
correlation-by-side-channel the way choreography's `PaymentStub` needed (remembering `Amount` from
an earlier `OrderPlaced` event to use later on `StockReserved`). Also, because command/reply
happens over the bus rather than as a blocking call, the coordinator can't just wait inline for a
reply — it needs to remember what order it's mid-processing when a reply arrives, which naturally
implies explicit per-order state. That's a second real, demonstrable contrast: in choreography no
single component ever knows "where this order is" in the overall flow; in orchestration, the
coordinator does, centrally.

## In Scope

- **Extract `EventBus` into a new shared project, `src/OrderSaga.Shared`**, done as its own
  prerequisite task before orchestration's own tasks start. `OrderSaga.Choreography` is updated to
  reference it instead of owning `EventBus` itself, so both implementations depend on shared
  infrastructure without depending on each other.
- **New project `src/OrderSaga.Orchestration`** with fresh Inventory/Payment/Shipping responders —
  independent of `OrderSaga.Choreography`'s participant types, reacting to explicit commands
  rather than to peer domain/notification events.
- **Command/reply types**, all flowing over the shared `EventBus`:
  `ReserveStockCommand`/`StockReservedReply`/`StockReservationFailedReply`,
  `ChargePaymentCommand`/`PaymentChargedReply`/`PaymentDeclinedReply`,
  `ConfirmReservationCommand`/`ReservationConfirmedReply`,
  `ReleaseReservationCommand`/`ReservationReleasedReply`,
  `ScheduleShipmentCommand`/`ShipmentScheduledReply`.
- **`SagaCoordinator`** — subscribes to `OrderPlaced` (same trigger as choreography, for a clean
  comparison) as the saga's entry point. Maintains an explicit per-order `SagaState` (`OrderId`,
  `Sku`, `Quantity`, `Amount`, current step) in memory, keyed by `OrderId`. Drives the full
  sequence by publishing the next command upon each reply:
  1. `OrderPlaced` → create `SagaState` (step `ReservingStock`) → publish `ReserveStockCommand`.
  2. `StockReservedReply` → step `AwaitingPayment` → publish `ChargePaymentCommand` (using the
     saga state's own `Amount`, not a value carried on the reply).
     `StockReservationFailedReply` → step `Failed` → saga ends (nothing to compensate).
  3. `PaymentChargedReply` → step `Confirming` → publish `ConfirmReservationCommand`.
     `PaymentDeclinedReply` → step `Compensating` → publish `ReleaseReservationCommand`.
  4. `ReservationConfirmedReply` → step `SchedulingShipment` → publish `ScheduleShipmentCommand`.
     `ReservationReleasedReply` → step `Compensated` → saga ends compensated.
  5. `ShipmentScheduledReply` → step `Completed` → saga ends successfully.
- **Same business rules as choreography:** Payment threshold `Amount > 500m` declines; single line
  item (one SKU) per order; Shipping always succeeds, no failure/compensation path of its own; no
  separate Order aggregate.
- **In-process, synchronous bus** — same as choreography; no real AWS/LocalStack wiring yet.

## Out of Scope

- Everything choreography already excluded: real AWS/LocalStack event bus wiring, multi-item
  orders, a Shipping failure/compensation path, DLQ-backed retry, Payment/Shipping having their
  own event-sourced aggregates, a separate persisted Order aggregate.
- Modifying `Inventory.Domain` or `OrderSaga.Choreography`'s own participant behavior — the
  `EventBus` extraction touches `OrderSaga.Choreography`'s project references only, not its
  participants' logic.
- `SagaState` persistence — in-memory only, lost on process restart, matching the project's
  existing "in-process infra now, real infra later" scoping.
- A written comparison/analysis of choreography vs. orchestration — that's a follow-up discussion
  once both are built, not a build task itself.

## Codebase

`saga-inventory-engine` repo. New `src/OrderSaga.Shared` (the extracted `EventBus` + its tests,
moved from `OrderSaga.Choreography`). New `src/OrderSaga.Orchestration` (+
`tests/OrderSaga.Orchestration.Tests`), referencing `Inventory.Domain` (unchanged) and
`OrderSaga.Shared`. `OrderSaga.Choreography`'s project reference updated to point at
`OrderSaga.Shared` instead of owning `EventBus.cs` directly; its own participants/events/tests are
otherwise untouched.
