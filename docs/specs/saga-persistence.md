# Saga Persistence — MongoDB Atlas + S3

**Status:** Signed off 2026-08-26

## Problem Statement

Both real AWS infra specs deferred the same underlying problem: `InventoryItem` and `SagaState`
both live only in-memory, so every service that holds one is pinned to `desired_count = 1` (a second
instance would start from a disagreeing blank slate) and a process crash mid-saga silently loses
whatever state it held — no compensating action fires, nothing surfaces as an error. This spec
builds the real, durable, shared storage layer both specs pointed at, and finally exercises the
MongoDB Atlas + S3 pieces of this project's originally-chosen stack that no code has touched yet.

There are three separate persistence targets, not one — each stack's `InventoryItem` is an
independent in-memory copy today (choreography's `InventoryParticipant`/Host and orchestration's
`InventoryResponder`/InventoryHost never shared state), and orchestration's `SagaState` has no
choreography equivalent at all:

1. Choreography's `InventoryItem` (`src/OrderSaga.Choreography.Host`)
2. Orchestration's `InventoryItem` (`src/OrderSaga.Orchestration.InventoryHost`)
3. Orchestration's `SagaState` (`src/OrderSaga.Orchestration.CoordinatorHost`)

## In Scope

- **MongoDB Atlas is the live store; S3 is a secondary archive, not something the live path reads
  from.** Every write goes to Mongo first (authoritative); S3 gets a best-effort dual-write of the
  same data at write time — no batch export job, no compaction, no scheduler. Mongo failing a write
  fails the operation; S3 failing a write logs and continues (matching this project's existing
  "the message was already processed, a delete failure just means redelivery" tolerance for
  secondary-path failures).
- **`InventoryItem` stays fully event-sourced, now against real storage.** Each Host appends the
  same domain events it already produces (`StockReserved`/`ReservationConfirmed`/etc. equivalents)
  to its stack's Mongo collection instead of just an in-memory `List`, and rebuilds via
  `LoadFromHistory` by reading that collection back — the projection this project already built
  finally has something durable to project from.
- **`SagaState` is persisted as a snapshot, not event-sourced.** It's `SagaCoordinator`'s current
  position in a flow, not an independent log of facts (unlike `InventoryItem`, where the events
  themselves are the meaningful record) — write the current `SagaState` document keyed by `OrderId`
  on every transition, overwriting the previous version. Keeping this asymmetric (one aggregate
  event-sourced, one snapshotted) is deliberate: it reflects where each actually earns the pattern,
  not uniform treatment for its own sake.
- **Startup behavior changes from "always reseed" to "load if present, else seed once."** Every Host
  currently hardcodes a fresh `InventoryItem.Seed("SKU-1", 100)` on every process start — with no
  persistence, there's nothing to lose, but that directly defeats the point once there is. On
  startup, a Host now checks its Mongo collection for an existing event stream for `SKU-1`; if one
  exists, it rebuilds via `LoadFromHistory`; if none exists (first run ever, fresh collection), it
  seeds once, same as today. Still single-SKU (`SKU-1`) — this spec doesn't expand SKU scope.
- **A new shared `Inventory` persistence project** (exact name TBD in `/plan`) holds the Mongo
  append/replay/dual-write logic reused by both choreography's and orchestration's Inventory
  Hosts — the two Hosts' persistence needs are identical in shape (same `Inventory.Domain` events,
  same domain model, just different connection strings/collection names via config), and duplicating
  it across both Hosts is the exact mistake this project's own PR #3 code review already caught and
  fixed once (`OrderSaga.Aws`) for the SQS/idempotency plumbing. `SagaState`'s snapshot store stays
  orchestration-only, in `OrderSaga.Orchestration.CoordinatorHost` (or `.Messaging`, decided in
  `/plan`) — no duplication risk there, since nothing else needs it.
- **Two separate MongoDB Atlas clusters, one per saga stack** — not one shared cluster. Matches how
  DynamoDB idempotency already works (each stack gets its own table, never a shared one) and keeps
  `infra/` and `infra/orchestration/` fully independent, the same independence orchestration's own
  spec explicitly chose over reusing choreography's stack. Orchestration's single cluster holds two
  collections (`InventoryItem` events, `SagaState` snapshots); choreography's cluster holds one.
- **Two separate S3 buckets, one per saga stack**, same independence reasoning.
- **The Atlas cluster (M0, free tier) is Terraform-managed**, via the `mongodbatlas` provider — a
  new Atlas API key added to the repo's gitignored `.env` (same pattern as the LocalStack token and
  AWS keys), an Atlas project reference, a cluster resource, a database user, and network access
  configured for Fargate to reach it. M0 doesn't support VPC peering or PrivateLink (a
  paid-dedicated-tier-only feature), so network access is an IP allowlist scoped to each stack's
  existing NAT gateway's static Elastic IP — not `0.0.0.0/0` — since both stacks' Fargate tasks
  already egress through one, per choreography's real-deployment cost lesson.
- **Once persistence exists, `desired_count` is raised from `1` to `>= 2`** on every service that
  was pinned by this exact gap: the Coordinator, choreography's Inventory-holding service, and
  orchestration's Inventory responder. Validated for real (not just "the config now allows it"): a
  reservation made against one task instance must be visible when a different task instance handles
  the next request for the same SKU, and a task restart mid-saga must not lose state.
- **Validation**: LocalStack for S3 and the write/read logic first (MongoDB Atlas isn't an AWS
  service — LocalStack can't emulate it, so the Mongo layer is exercised against the real, free M0
  cluster from the start), then one real, temporary AWS deployment (all affected Hosts, both stacks,
  bumped `desired_count`), the multi-instance/crash-recovery proof above, verified and torn down —
  same established pattern as both prior AWS infra specs.

## Out of Scope

- **Upgrading either Atlas cluster beyond M0.** The tier stays a Terraform variable (per this
  project's original stack notes) specifically so this is a future variable flip, not something
  this spec needs to exercise now.
- **Event-sourcing `SagaState`.** Decided against deliberately — see In Scope's reasoning. A future
  spec could revisit this if a real need for saga-transition history (not just current position)
  emerges, but none has.
- **Batch/scheduled S3 archival or compaction.** Dual-write at write time only; no export job, no
  lifecycle policy beyond whatever S3 defaults apply.
- **Expanding beyond `SKU-1`** — this spec persists the existing single-SKU simplification, not a
  multi-SKU catalog.
- **Any change to `Inventory.Domain`'s or `SagaCoordinator`'s existing business logic** — this spec
  adds a storage layer around already-built, already-tested domain code, not new domain behavior.
- **A shared Terraform root for the two Atlas clusters/S3 buckets.** Explicitly rejected in favor of
  keeping `infra/` and `infra/orchestration/` fully independent — see In Scope.
- **Multi-region** — multi-AZ only, per the project's standing scope.
- **DLQ alerting/alarms, CDC/streaming replication from Mongo to S3** — not introduced by this spec.

## Codebase

`saga-inventory-engine` repo, single codebase (no sibling-repo topology). Touches:

- A new shared persistence project (name decided in `/plan`) for `InventoryItem`'s Mongo
  append/replay/dual-write logic, referenced by both `src/OrderSaga.Choreography.Host` and
  `src/OrderSaga.Orchestration.InventoryHost`.
- `src/OrderSaga.Orchestration.CoordinatorHost` (or `.Messaging`) — new `SagaState` snapshot store.
- `src/OrderSaga.Choreography.Host`, `src/OrderSaga.Orchestration.InventoryHost`,
  `src/OrderSaga.Orchestration.CoordinatorHost` — startup load-or-seed changes; DI wiring for the
  new persistence dependencies.
- New Terraform: an Atlas cluster + database user + network access resource and an S3 bucket +
  IAM policy statements, added to both `infra/` and `infra/orchestration/` independently (not a
  shared module instance). `desired_count` raised on the previously-pinned services in both roots.
- Test convention: xUnit v3, manual test doubles — an in-memory fake for the Mongo-backed store
  interface, matching this repo's established pattern (`InMemoryIdempotencyStore` for
  `IIdempotencyStore`); real Mongo/S3 behavior verified against LocalStack (S3) and the real Atlas
  cluster (Mongo), not unit tests.
