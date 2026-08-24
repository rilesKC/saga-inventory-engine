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
orchestration) complete — all in-process/domain-layer only so far, no real AWS/LocalStack wiring
yet. See `docs/specs/` and `docs/plans/` for each piece's spec and task breakdown.

## Development process

This project uses a spec-first pipeline: `/brainstorm` → `/plan` → test-first implementation →
`/review-task`. See `docs/specs/` and `docs/plans/` once they exist.
