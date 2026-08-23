# Saga Inventory Engine — Project Instructions

## Purpose

A hands-on learning project for event-driven architecture: sagas, event sourcing, and
choreography vs. orchestration, built with the specific patterns and AWS-native stack named in a
target job's technologies list, not a generic tutorial stack. Scope is deliberately small — one
saga, built twice, deployed once for real — over broad coverage.

## Domain

E-commerce order fulfillment saga: **Order → Payment → Inventory → Shipping**. The Inventory
service is event-sourced (immutable event log + rebuildable projection); it's the piece most
directly demonstrating "event-driven systems" experience, so it gets the most design attention.

The saga is implemented **twice**: once as choreography (services react to each other's events,
no central coordinator) and once as orchestration (a saga coordinator issues commands and listens
for replies) — deliberately, to compare the tradeoff from having built both rather than only
reasoning about it.

## Stack

- **Language:** C# / .NET 8 — kept familiar on purpose so the new-territory budget goes to the
  event-driven patterns and AWS surface, not a second unfamiliar language at the same time.
- **Event bus:** AWS EventBridge (or SQS+SNS) — chosen over a general-purpose broker like
  RabbitMQ specifically because it's unfamiliar territory worth closing.
- **Event store:** MongoDB Atlas, free M0 tier. M0 is already a 3-node replica set under shared
  infrastructure; write application/driver code against replica-set semantics (retryable writes,
  majority write concern) from the start so upgrading to a dedicated M10+ tier later is a
  Terraform tier-variable change, not a rewrite.
- **Archive:** S3, as an immutable event archive.
- **Infra as code:** Terraform. AWS compute/networking (ALB, Fargate, subnets) is built multi-AZ
  from day one — it's free with these managed services, so there's no minimal-vs-multi-AZ
  tradeoff to defer there. Parameterize the Mongo Atlas cluster tier and `replication_specs` as
  Terraform variables so M0 → M10 is a variable flip, not a module rewrite.
- **Local dev:** LocalStack for fast iteration against EventBridge/SQS/DynamoDB semantics before
  any real AWS spend; a real minimal AWS deploy happens once the Terraform exists, so there's an
  actually-running system to describe, not just a simulated one.

## Patterns to build deliberately (not incidentally)

- **Idempotency-by-claim** — claim-before-emit, not just retry-and-hope.
- **DLQ-backed retry.**
- **Event sourcing with rebuildable projections** on the Inventory service.
- **Choreography vs. orchestration**, built both ways for the same saga.

## Spec-driven development

This project follows the same spec-first pipeline as the sibling InfusionCalc repos — `/brainstorm`
produces a signed-off spec in `docs/specs/`, `/plan` breaks it into a resumable, checkbox-driven
task list in `docs/plans/`, and `/review-task` reviews each completed task before moving to the
next. This isn't incidental process — practicing it deliberately is part of the point of this
project.

## Test-first discipline when implementing an active plan

When implementing a task from a plan file in `docs/plans/` (produced by `/plan`): write the failing
test first, run it and confirm it fails for the expected reason, then write the minimal
implementation to make it pass, then run it again to confirm it passes. Do not write implementation
code before a failing test exists for it.

This applies per-task, not per-file — small steps, not a single test suite followed by a single
implementation pass. Test convention: xUnit v3 with manual test doubles (no mocking framework),
matching the sibling InfusionCalc repos.

Check off the task's checkbox in the plan file as soon as it passes, before moving to the next
task — the plan file's checked/unchecked state is what a resumed session should trust, not
conversation history. Run `/review-task` after each task completes, before moving to the next one.

## Explicitly out of scope

Multi-region, a UI, Fargate/CDN polish beyond what's needed to demonstrate the pattern. These are
good "here's what I'd add next" talking points, not things this project needs to actually build.
