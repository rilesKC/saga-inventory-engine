# Inventory Aggregate

**Status:** Signed off 2026-08-23

## Problem Statement

The saga (Order → Payment → Inventory → Shipping) needs an Inventory service that can hold stock
against an order without permanently committing it, since a later saga step might still fail and
require compensation. This spec covers the Inventory aggregate itself — an event-sourced, per-SKU
stock ledger — independent of whichever saga coordination style (choreography or orchestration)
ends up driving it. This is the piece most directly demonstrating hands-on event-sourcing and
saga-compensation design, which is the core purpose of this project.

## In Scope

- A per-SKU, event-sourced Inventory aggregate tracking a single global stock pool (fungible
  quantity, not distinct/serialized units).
- Three-state reservation lifecycle: `Reserved` (soft hold) → `Confirmed` (permanent deduction) or
  `Released` (compensating action, returns quantity to the available pool).
- Commands: `ReserveStock`, `ConfirmReservation`, `ReleaseReservation` — each producing a
  corresponding domain event on the aggregate's stream.
- Optimistic concurrency on the aggregate's event stream: two concurrent reservation attempts
  against the same low-stock SKU — the losing attempt is rejected as insufficient stock, not a
  system error.
- Idempotency inside the aggregate: a duplicate `ReserveStock` for an order ID that already holds
  a reservation for that SKU is a no-op that returns the existing reservation, not a double
  reservation. (This is the second line of defense — a claim-before-emit check at the
  saga-coordinator level is a separate, out-of-scope concern for this spec.)
- Rejecting invalid state transitions as errors: e.g. `ConfirmReservation` against a reservation
  that's already `Released`, or `ReleaseReservation` against one already `Confirmed`.
- A rebuildable projection giving current available quantity per SKU, derived by replaying the
  aggregate's event log.

## Out of Scope

- Distinct/serialized unit tracking (individually identified units). If ever needed, it's a new,
  separate aggregate type added alongside this one — not a migration of it.
- Reservation expiry/TTL (auto-releasing a stale, never-confirmed-or-released hold).
- Replenishment/restock (receiving new stock). Initial inventory is assumed seeded once, outside
  this spec.
- Per-warehouse/multi-location stock (this spec tracks a single global pool per SKU).
- The saga coordination logic that calls this service — both the choreography and orchestration
  implementations are separate, later specs. This spec only covers the Inventory aggregate's own
  command/event contract.
- Claim-before-emit idempotency at the saga-coordinator level (referenced above as a related but
  separate concern).

## Codebase

`saga-inventory-engine` repo, single codebase. No .NET solution exists yet — this is the first
project. Project structure is intentionally loose for now, not locked to a fixed layout; `/plan`
should propose a minimal, reasonable structure as part of task breakdown rather than assuming one
already exists.
