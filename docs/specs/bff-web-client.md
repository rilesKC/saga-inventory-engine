# BFF & Web Client

**Status:** Signed off 2026-09-09

## Problem Statement

This project already covers most of the AWS-native stack named in the target job's technologies
list — EventBridge/SQS, DynamoDB, S3, Fargate, Terraform, MongoDB Atlas — but has zero coverage of
that stack's TypeScript/React/Node layer. There's also currently no way to place or check on an
order except by calling one of the saga hosts' HTTP endpoints directly.

This adds a local-only Node/TypeScript BFF (Backend-for-Frontend) and a React + TypeScript
frontend, so an order can be placed and tracked through a real BFF pattern — one client contract
hiding two differently-shaped backend services — closing the last named gap in the target stack.

## In Scope

- New `GET /orders/{id}` endpoint on `OrderSaga.Choreography.Host`, returning 404 if the order
  doesn't exist.
- New `GET /orders/{id}` endpoint on `OrderSaga.Orchestration.CoordinatorHost`, returning 404 if
  the order doesn't exist.
- Response shape for both new endpoints: the order's "full history" is the filtered Inventory
  event log for that order (`StockReserved`/`ReservationConfirmed`/`ReservationReleased` events
  matching the order's `OrderId`, read from the existing SKU-indexed event store) plus the current
  `SagaState` snapshot for where the order ended up.
- New Node/TypeScript BFF service (local-only):
  - `POST /orders` — takes a per-request choice of target stack (choreography or orchestration),
    proxies to that stack's `POST /orders`.
  - `GET /orders/{id}` — takes a per-request choice of target stack, proxies to that stack's new
    `GET /orders/{id}`, and normalizes both stacks' responses into one unified shape for the
    client.
- New React + TypeScript frontend (local-only):
  - A form to place an order (`OrderId`, `Sku`, `Quantity`, `Amount`, and a choice of which stack
    to route through).
  - A view to look up an order by ID and see its full history.
  - Talks only to the BFF — never calls either .NET host directly.
- Everything in this feature runs locally: `dotnet run` (or the existing LocalStack setup) for the
  two hosts, a local Node dev server for the BFF, a local Vite dev server for the React app.

## Out of Scope

- Authentication/authorization — the BFF and both hosts remain unauthenticated, same as today.
- Real-time push (SSE/WebSocket) — status is fetched on demand, not streamed.
- Deploying the BFF or React app to AWS — no new Terraform module, no third Fargate service.
- Changing `SagaState` persistence from snapshot to event-sourced/append-only. "Full history" is
  served from the existing Inventory event log plus the current `SagaState` snapshot, not a true
  step-by-step saga timeline — reopening the snapshot-vs-event-sourced decision for `SagaState` is
  explicitly not part of this feature.
- Production-grade defensive coding (retries, circuit breakers, exhaustive input validation). This
  is a learning project — basic, correct behavior for the paths described above is the goal, not
  hardening.

## Codebase

Single repo, no multi-repo topology (`saga-inventory-engine` is standalone). Touches:

- `src/OrderSaga.Choreography.Host` — new `GET /orders/{id}` endpoint.
- `src/OrderSaga.Orchestration.CoordinatorHost` — new `GET /orders/{id}` endpoint.
- New top-level `bff/` directory — the Node/TypeScript BFF service.
- New top-level `web/` directory — the React + TypeScript frontend.
