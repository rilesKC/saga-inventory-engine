# Choreography — Real AWS Infrastructure

**Status:** Signed off 2026-08-24

## Problem Statement

Both saga implementations exist as pure in-process domain code with an in-memory event bus — real
enough to build and test the coordination logic, but nothing has actually run distributed, on real
(or LocalStack-simulated) AWS infrastructure. This spec moves the choreography implementation onto
real AWS: EventBridge for pub/sub routing, SQS as the actual delivery mechanism into a long-running
service, Fargate for compute, and Terraform for all of it — validated against LocalStack first,
then deployed for real once, per the project's stated "domain logic first, infra later, one real
deployment" scope. This is also where two patterns named in `CLAUDE.md` since the project's
inception, but correctly deferred until real infrastructure existed to build them against, finally
get built: DLQ-backed retry and claim-before-emit idempotency.

Choreography was chosen over orchestration for this deployment because it maps most naturally onto
EventBridge's actual model (independent participants, no central flow definition) — and deploying
it for real risks surfacing a genuinely different class of bug than the in-process ordering hazard
already found: EventBridge does not guarantee delivery ordering the way the in-process bus's
breadth-first fix does.

## In Scope

- **Deployment topology:** one shared Fargate service hosting all three choreography participants
  (`InventoryParticipant`, `PaymentStub`, `ShippingStub`) as they already exist, largely unchanged
  — not three separate services. Multi-AZ from day one (VPC/subnets across ≥2 AZs, ALB, Fargate
  desired count ≥2), per the project's standing multi-AZ decision — free with these managed
  services, no reason to defer it.
- **Every event round-trips through EventBridge**, not just the external trigger. A participant
  publishing an event (via the existing, unchanged `EventBus` abstraction) also results in that
  event being sent to EventBridge; an EventBridge rule (one per event type the service's
  participants subscribe to) routes it to a single shared SQS queue; the service polls that queue,
  deserializes each message, and dispatches it through the existing local `EventBus.Publish` to
  whichever participant(s) are subscribed. This applies uniformly to `OrderPlaced` and to every
  event any participant produces — no special-cased "internal" dispatch path that skips AWS.
  Chosen deliberately over a trigger-only design so the new infrastructure (SQS, DLQ, idempotency
  store) is genuinely exercised for the whole saga, and so a future spec that splits participants
  into separate services wouldn't require redesigning the event flow.
- **HTTP intake endpoint** (ALB → the Fargate service, e.g. `POST /orders`) whose only job is to
  construct an `OrderPlaced` event and publish it to EventBridge — it does not call `EventBus`
  directly, keeping the entry point consistent with every other event's path.
- **One shared SQS queue** (not one per event type or participant) for the whole service, fed by
  multiple EventBridge rules all targeting it.
- **DLQ-backed retry:** a redrive policy on the shared queue (a small `maxReceiveCount` — exact
  value an implementation detail) moving a message to a dead-letter queue after repeated processing
  failures, rather than retrying forever or silently dropping it.
- **Claim-before-emit idempotency:** a small DynamoDB table using conditional writes
  (`PutItem`/`attribute_not_exists`) keyed by a per-event message ID, checked before a polled
  message is dispatched to `EventBus.Publish`. This is a distinct layer from the Inventory
  aggregate's own existing idempotency (task 6 of the Inventory Aggregate plan — deduplicating
  `ReserveStock` by order+SKU at the business level); this one deduplicates SQS's at-least-once
  redelivery of the same message at the transport level, and matters for *every* event, not just
  reservation requests. Necessary specifically because multi-AZ means 2+ running instances share
  no in-memory state — an in-memory claim set wouldn't catch a duplicate delivered to a different
  instance.
- **Message envelope** carrying enough of a type discriminator for the SQS poller to deserialize
  into the correct CLR event type before calling `EventBus.Publish` — exact shape is an
  implementation detail.
- **Terraform** for all of the above: VPC/subnets (multi-AZ), ALB, ECS cluster + Fargate service,
  EventBridge custom bus + rules, the SQS queue + DLQ + redrive policy, the DynamoDB idempotency
  table, IAM roles/policies, ECR repository, CloudWatch log group. NAT Gateway cost minimized via
  VPC endpoints where practical (exact endpoint list an implementation detail).
- **Validation:** against LocalStack first (EventBridge/SQS/DynamoDB/ECR emulation), then one real
  minimal AWS deployment, per the project's established pattern.

## Out of Scope

- The orchestration implementation's AWS deployment — this spec is choreography only.
- Real persistence of the `InventoryItem` event log (MongoDB Atlas) or the S3 immutable archive —
  the aggregate's event log stays in-memory inside the Fargate service, same as today. A distinct
  concern from the DynamoDB idempotency table introduced here, which is transport plumbing, not
  domain event storage.
- Splitting the three participants into separate deployable services — explicitly designed *for*
  later via the "every event round-trips" decision, but not built now.
- DLQ alerting/alarms — messages landing in the DLQ are visible via the AWS console; automated
  alerting (CloudWatch alarms, notifications) is a future concern.
- Multi-region — multi-AZ only, per the project's standing scope.
- Any change to the choreography participants' own business logic, or to `Inventory.Domain` — this
  spec adds a transport/infrastructure layer around already-built, already-tested code, not new
  domain behavior.

## Codebase

`saga-inventory-engine` repo. New `src/OrderSaga.Choreography.Host` (the deployable — HTTP intake
endpoint, SQS poller, EventBridge publisher adapter, DynamoDB idempotency check), referencing
`OrderSaga.Choreography`, `OrderSaga.Shared`, and `Inventory.Domain` unchanged. New `infra/`
directory for Terraform (root + modules). LocalStack config for local validation. Unit-testable
logic (claim-check, message envelope serialization, adapter logic) follows the repo's xUnit v3 +
manual-test-double convention; actual AWS behavior (EventBridge routing, DLQ triggering) is
verified against LocalStack, not unit tests.
