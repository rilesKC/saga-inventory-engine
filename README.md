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

## Status

Inventory aggregate (event log + projection) complete — see `docs/specs/inventory-aggregate.md`
and `docs/plans/inventory-aggregate-plan.md`. Saga coordination layer (choreography, then
orchestration) is next.

## Development process

This project uses a spec-first pipeline: `/brainstorm` → `/plan` → test-first implementation →
`/review-task`. See `docs/specs/` and `docs/plans/` once they exist.
