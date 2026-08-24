# Order Saga — Choreography

**Status:** Signed off 2026-08-24

## Problem Statement

The Inventory aggregate (event log + projection) exists but nothing drives it yet. This spec
builds the choreography-style implementation of the Order → Inventory → Payment → Shipping saga:
participants that react to each other's published events, with no central coordinator. It's the
first of two saga implementations (orchestration comes later, as a separate spec) built
specifically so the choreography-vs-orchestration tradeoff can be discussed from having built
both. This spec is also where the Inventory aggregate's full three-state lifecycle
(Reserved/Confirmed/Released) actually gets exercised end-to-end for the first time, including its
compensating-action path.

## In Scope

- **An in-process, synchronous publish/subscribe event bus** — no real AWS/LocalStack wiring yet
  (that's a separate future spec). Publishing an event synchronously invokes every subscribed
  handler in-process.
- **`OrderPlaced`** (`OrderId`, `Sku`, `Quantity`, `Amount`) is the triggering event — no separate
  Order aggregate; nothing tracks order state beyond what each participant already tracks.
- **Happy path event chain:**
  1. `OrderPlaced` → Inventory participant calls `Handle(ReserveStock)` → `StockReserved`
     published.
  2. `StockReserved` → Payment stub evaluates a threshold rule (e.g. decline if `Amount > 500`) →
     `PaymentCharged` published.
  3. `PaymentCharged` → Inventory participant calls `Handle(ConfirmReservation)` →
     `ReservationConfirmed` published.
  4. `ReservationConfirmed` → Shipping stub always succeeds → `ShipmentScheduled` published
     (terminal event).
- **Compensation paths:**
  - Insufficient stock: the Inventory participant catches `InsufficientStockException` and
    publishes a new saga-level `StockReservationFailed` event (a notification, not a sourced
    domain event on the aggregate itself — rejected commands aren't sourced). Saga ends here;
    nothing upstream needs compensating since Inventory is the first real step after `OrderPlaced`.
  - Payment declined: Payment stub publishes `PaymentDeclined` → Inventory participant reacts by
    calling `Handle(ReleaseReservation)` → `ReservationReleased` published. Saga ends compensated.
- **Payment stub:** no event log, no projection — a pure function of `Amount` against a fixed
  threshold.
- **Shipping stub:** no event log, no projection, no failure path — always succeeds once it
  receives `ReservationConfirmed`.
- Single line item (one SKU) per order.

## Out of Scope

- Real AWS/LocalStack event bus wiring (EventBridge/SQS) — separate future spec.
- The orchestration implementation of this same saga — separate future spec.
- A Shipping failure/compensation path (e.g. "no carrier available"). Would require a third
  compensation chain (un-confirm the reservation, refund payment) without teaching a new pattern.
- Multi-item orders (multiple SKUs per order, each needing its own Inventory aggregate instance
  and independent reserve/compensate outcome).
- A separate Order aggregate or persisted order state.
- DLQ-backed retry — inherently a queue/infra concept; belongs with the real AWS event bus spec,
  not the in-process synchronous bus.
- Payment and Shipping having their own event-sourced aggregates (see the earlier "thin stubs vs.
  full services" decision — stubs only, no independent event logs).

## Codebase

`saga-inventory-engine` repo. Proposed new project `src/OrderSaga.Choreography` (exact structure
is `/plan`'s call, same as the Inventory spec's own codebase note): the event bus abstraction, the
`OrderPlaced` trigger type, the Inventory/Payment/Shipping participants, and the saga-level
notification events (`StockReservationFailed`, `PaymentCharged`, `PaymentDeclined`,
`ShipmentScheduled`) that don't belong on the Inventory aggregate itself. Depends on
`src/Inventory.Domain` (already built) as a project reference — does not modify it, aside from
possibly needing to reference its existing events (`StockReserved`, `ReservationConfirmed`,
`ReservationReleased`) directly on the same bus rather than wrapping them.
