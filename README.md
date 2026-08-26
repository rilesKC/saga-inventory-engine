# Saga Inventory Engine

A hands-on project for learning event-driven architecture: sagas, event sourcing, and
choreography vs. orchestration — built with an AWS-native stack (EventBridge/SQS, MongoDB Atlas,
S3, DynamoDB, Terraform) on purpose, rather than a generic tutorial stack.

## What this is

An e-commerce order fulfillment saga — **Order → Inventory → Payment → Shipping** — implemented
twice: once as choreography, once as orchestration. The Inventory service is event-sourced
(immutable event log + rebuildable projection).

See [CLAUDE.md](CLAUDE.md) for the full scope, stack rationale, and the patterns this project is
deliberately built to exercise (idempotency-by-claim, DLQ-backed retry, event sourcing,
choreography vs. orchestration).

See [docs/choreography-vs-orchestration.md](docs/choreography-vs-orchestration.md) for what the
tradeoff actually looked like once both were built — concrete differences, a real bug the
choreography build hit that orchestration structurally couldn't, and where the two map onto a
real AWS-native stack (Step Functions/EventBridge DAGs).

## Status

Inventory aggregate (event log + projection) and both saga implementations (choreography,
orchestration) are complete and deployed to real AWS:

- **Choreography** (`src/OrderSaga.Choreography.Host`) — one shared Fargate service, EventBridge
  pub/sub, SQS delivery, DynamoDB idempotency. Infra under `infra/`.
- **Orchestration** (`src/OrderSaga.Orchestration.{CoordinatorHost,InventoryHost,ResponderHost}`) —
  three separate Fargate services (Coordinator, Inventory, and a shared stateless Payment+Shipping
  responder), direct SQS (no EventBridge — every command is point-to-point), its own DynamoDB
  idempotency table. Infra under `infra/orchestration/`.

Both stacks were validated against LocalStack, then deployed for real, exercised end-to-end
(happy path, insufficient stock, payment declined), and torn down — see
[docs/localstack-setup.md](docs/localstack-setup.md) /
[docs/localstack-setup-orchestration.md](docs/localstack-setup-orchestration.md) to reproduce
either. See `docs/specs/` and `docs/plans/` for each piece's spec and task breakdown, and
[docs/choreography-vs-orchestration.md](docs/choreography-vs-orchestration.md) for what deploying
both actually showed, not just building both.

Both stacks now persist real state too: `InventoryItem` (event-sourced) and `SagaState`
(snapshotted) are durably written to a MongoDB Atlas cluster per stack, with a best-effort S3
archive of every event/state write, replacing the earlier in-memory-only implementation. Both
`desired_count`s were raised to 2+ and validated for real — multi-instance concurrency and
crash-recovery mid-saga, not just the single-instance happy path — then torn down. See
[docs/specs/saga-persistence.md](docs/specs/saga-persistence.md) /
[docs/plans/saga-persistence-plan.md](docs/plans/saga-persistence-plan.md) for the design and what
that real deployment found.

## Development process

This project uses a spec-first pipeline: `/brainstorm` → `/plan` → test-first implementation →
`/review-task`. See `docs/specs/` and `docs/plans/` for the full history of specs and task
breakdowns this pipeline has produced so far.
