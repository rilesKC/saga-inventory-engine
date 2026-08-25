# Orchestration — Real AWS Infrastructure — Plan

Spec: docs/specs/orchestration-aws-infra.md

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):

- **New shared project, `src/OrderSaga.Orchestration.Messaging`**, holding everything genuinely
  identical across all three Hosts: the message envelope + type registry, the idempotency store
  abstraction (+ in-memory fake and real DynamoDB implementation), an SQS-backed message publisher,
  an SQS client factory helper, and — because none of these actually contain choreography-specific
  or orchestration-specific logic, just "deserialize, claim-check, dispatch" / "receive, process,
  batch-delete" — a **generic `SqsMessageProcessor` and `SqsPollingBackgroundService` reused as-is
  by all three Hosts**, parameterized by which `EventBus` and which queue URL. Choreography only
  ever had one Host, so this kind of consolidation wasn't a live question there; three Hosts sharing
  near-identical AWS glue is exactly the "duplicated 3x" shape the choreography PR's code review
  flagged (client construction, envelope handling) — building it shared from the start here instead
  of tripling it and fixing that later.
- **Everything the choreography Host's code review found and had to retrofit is built in from day
  one here, not repeated:** the idempotency store is `TryClaimAsync`/`ReleaseAsync` (async,
  cancellable, claim released on dispatch failure) from task 3 onward, not a later fix; the SQS
  poller batches its deletes and awaits `CancellationToken`-aware calls from task 9 onward; the
  DynamoDB table name and every queue URL are configuration-driven, never a hardcoded constant that
  could drift from what Terraform actually creates.
- **Each Host's composition-root wiring is its own small, directly-testable static class from the
  start** (`CoordinatorWiring`, `InventoryWiring`, `ResponderWiring` — same shape as
  `HostParticipantWiring`, which choreography only got *after* its review flagged zero test coverage
  for `Program.cs`'s bus wiring). Each has its own test proving inbound/outbound separation holds
  for that Host's actual composition, not a retrofit.
- **`InventoryHost` and `ResponderHost` use a plain `Microsoft.NET.Sdk.Worker`-style generic host**
  (`Host.CreateApplicationBuilder`, no Kestrel) — neither has any HTTP surface, and the spec already
  settled that both rely on ECS's own task-health signal, not a target-group health check. Only
  `CoordinatorHost` is `Microsoft.NET.Sdk.Web`.
- **Terraform tasks don't use the xUnit "Test:" convention** — same as choreography's plan; nothing
  to unit test in HCL. Verification is `terraform validate`, then LocalStack, then real AWS.
- **The existing `compute` module needs one small, additive change**: an optional ALB attachment
  (the `load_balancer` block and its `target_group_arn` variable become conditional), since
  `InventoryHost` and `ResponderHost` have no ALB in front of them. This must not change the
  module's behavior for choreography's existing usage (which always supplies a target group) — that
  existing usage gets re-validated as part of the same task, not assumed safe.
- **Coordinator only ever polls one queue** (`coordinator-inbound`) even though it publishes to two
  (`inventory-commands`, `stateless-responder-commands`) — the routing complexity is entirely on the
  publish side (`CommandRouter`), not the receive side.

## Tasks

### Shared transport plumbing (`src/OrderSaga.Orchestration.Messaging`)

- [x] 1. Scaffold the project and test project
      - File(s): `src/OrderSaga.Orchestration.Messaging/OrderSaga.Orchestration.Messaging.csproj`
        (references `OrderSaga.Shared`, `OrderSaga.Orchestration`),
        `tests/OrderSaga.Orchestration.Messaging.Tests/OrderSaga.Orchestration.Messaging.Tests.csproj`,
        added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Orchestration.Messaging.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 2. Message envelope and type registry (all 13 known types: `OrderPlaced`, the 5 commands, the
      7 replies)
      - File(s): `src/OrderSaga.Orchestration.Messaging/MessageEnvelope.cs`,
        `src/OrderSaga.Orchestration.Messaging/OrchestrationMessageTypeRegistry.cs` — `Payload` is a
        `JsonElement` from the start (not a pre-serialized string), avoiding choreography's
        double-encoding bug outright
      - Test: `tests/OrderSaga.Orchestration.Messaging.Tests/MessageEnvelopeTests.cs` —
        `Serialize_ReserveStockCommand_ProducesEnvelopeWithTypeMessageIdAndPayload`,
        `Deserialize_EnvelopeWithReserveStockCommandType_ReconstructsOriginalCommand`,
        `Serialize_Payload_IsARealJsonObjectNotAnEscapedString`

- [x] 3. Idempotency store abstraction + in-memory fake
      - File(s): `src/OrderSaga.Orchestration.Messaging/IIdempotencyStore.cs`,
        `src/OrderSaga.Orchestration.Messaging/InMemoryIdempotencyStore.cs`
      - Test: `tests/OrderSaga.Orchestration.Messaging.Tests/InMemoryIdempotencyStoreTests.cs` —
        `TryClaimAsync_FirstTime_ReturnsTrue`, `TryClaimAsync_DuplicateMessageId_ReturnsFalse`,
        `ReleaseAsync_ClaimedMessageId_AllowsReClaim`

- [x] 4. AWS client factory helper (LocalStack-endpoint-override-aware, shared by SQS and DynamoDB
      client construction across all three Hosts)
      - File(s): `src/OrderSaga.Orchestration.Messaging/AwsClientFactory.cs`
      - Verification: compiles against the AWS SDK; no independent branching logic worth a unit
        test (mirrors choreography's inline pattern, just extracted and shared instead of
        triplicated) — exercised via every Host's own LocalStack run (task 27).

- [x] 5. Real DynamoDB-backed idempotency store (config-driven table name)
      - File(s): `src/OrderSaga.Orchestration.Messaging/DynamoDbIdempotencyStore.cs`
      - Verification: compiles against `IAmazonDynamoDB`; real conditional-write behavior verified
        via LocalStack (task 27), not a unit test — same precedent as choreography's.

- [x] 6. Message publisher abstraction + real SQS-backed implementation (queue URL supplied at
      construction, `SendMessageAsync` awaited with the caller's `CancellationToken`)
      - File(s): `src/OrderSaga.Orchestration.Messaging/IMessagePublisher.cs`,
        `src/OrderSaga.Orchestration.Messaging/SqsMessagePublisher.cs`
      - Verification: compiles against `IAmazonSQS`; real send behavior verified via LocalStack
        (task 27), not a unit test — thin wrapper, same precedent as choreography's
        `EventBridgeEventPublisher`.

- [x] 7. Generic SQS message processor (claim-check, deserialize via the registry, dispatch to a
      given `EventBus`; releases the claim and rethrows on dispatch failure)
      - File(s): `src/OrderSaga.Orchestration.Messaging/SqsMessageProcessor.cs`
      - Test: `tests/OrderSaga.Orchestration.Messaging.Tests/SqsMessageProcessorTests.cs` —
        `ProcessMessageAsync_NewEnvelope_ClaimsAndDispatchesToEventBus`,
        `ProcessMessageAsync_DuplicateMessageId_SkipsDispatch`,
        `ProcessMessageAsync_DispatchThrows_ReleasesClaimSoRedeliveryCanRetry`

- [x] 8. Generic outbound message forwarder (subscribes to every known type on a given
      `OutboundEventBus`; publishes each via a caller-supplied `Func<Type, IMessagePublisher>`
      selector, so a single-destination Host can always return the same publisher and Coordinator
      can route by command type)
      - File(s): `src/OrderSaga.Orchestration.Messaging/OutboundMessageForwarder.cs`
      - Test: `tests/OrderSaga.Orchestration.Messaging.Tests/OutboundMessageForwarderTests.cs` —
        `Publish_KnownType_ForwardsToTheSelectedPublisher`,
        `Publish_TwoTypesRoutedToDifferentPublishers_EachGoesToItsOwnPublisher` (using recording
        fake `IMessagePublisher`s)

- [x] 9. Generic SQS polling background service (batches deletes for successfully-processed
      messages into one `DeleteMessageBatchAsync` call per poll; per-message and per-poll exception
      handling matching choreography's already-hardened version)
      - File(s): `src/OrderSaga.Orchestration.Messaging/SqsPollingBackgroundService.cs`
      - Verification: compiles against `IAmazonSQS`; wraps task 7's already-unit-tested processor
        with the actual receive/delete loop. Real behavior verified via LocalStack (task 27).

### CoordinatorHost (`src/OrderSaga.Orchestration.CoordinatorHost`)

- [x] 10. Scaffold the project and test project
      - ⚠ Retro: task's file list didn't include a placeholder `Program.cs`, but `Microsoft.NET.Sdk.Web` fails to compile at all without an entry point (`CS5001`) -- task 14 was assigned all of `Program.cs`, leaving this task unbuildable on its own as originally scoped. Added a minimal `WebApplication.CreateBuilder(args); ...; app.Run();` placeholder here; task 14 fleshes it out rather than creating it from scratch.
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/OrderSaga.Orchestration.CoordinatorHost.csproj`
        (ASP.NET Core minimal API — this Host has the only HTTP surface — references
        `OrderSaga.Orchestration`, `OrderSaga.Orchestration.Messaging`, `OrderSaga.Shared`),
        `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/OrderSaga.Orchestration.CoordinatorHost.Tests.csproj`,
        added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 11. Command router (pure function: a command's type → which of the two command queues it
      belongs to)
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/CommandRouter.cs`
      - Test: `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/CommandRouterTests.cs` —
        `PublisherFor_ReserveStockOrConfirmOrReleaseCommand_ReturnsInventoryPublisher`,
        `PublisherFor_ChargePaymentOrScheduleShipmentCommand_ReturnsStatelessResponderPublisher`

- [x] 12. HTTP intake handler (publishes `OrderPlaced` onto `coordinator-inbound` via
      `IMessagePublisher` directly, not `EventBus.Publish`)
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/OrderIntakeHandler.cs`
      - Test: `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/OrderIntakeHandlerTests.cs` —
        `Handle_ValidRequest_PublishesOrderPlacedViaMessagePublisher` (using a recording fake
        `IMessagePublisher`)

- [x] 13. Coordinator wiring (testable composition-root extraction)
      - ⚠ Retro: `SagaCoordinator`'s constructor took one plain `EventBus` (fine for the in-process orchestration design, built before this Host layer existed) -- wrapping it as-is with `InboundEventBus`/`OutboundEventBus` without changing it would have let the coordinator's own issued commands loop back through its own reply subscriptions in-process, the exact bug class choreography's participants were fixed for. Not anticipated by the plan. Fixed by giving `SagaCoordinator` the same two-parameter constructor `InventoryParticipant`/`PaymentStub`/`ShippingStub` already have (`src/OrderSaga.Orchestration/SagaCoordinator.cs`), updating its own existing tests (`SagaCoordinatorTests.cs`, `OrderSagaOrchestrationIntegrationTests.cs`) to match -- same treatment, same precedent as the earlier choreography plan's task 10. `InventoryResponder`/`PaymentResponder`/`ShippingResponder` still have the old single-bus constructor; expect the identical finding on tasks 16 and 19.
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/CoordinatorWiring.cs`
      - Test: `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/CoordinatorWiringTests.cs` —
        `Wire_OrderPlacedOnInbound_PublishesReserveStockCommandOnOutboundOnly`

- [x] 14. Wire the CoordinatorHost application
      - ⚠ Retro: two gaps in this task's own description, caught while implementing it. (1) The "unused for publishing... none needed for its own inbound" clause about the third `IMessagePublisher` was simply wrong -- it's the one `OrderIntakeHandler` uses to publish `OrderPlaced` onto `coordinator-inbound`, very much used. (2) "DI for... the three `IMessagePublisher`s" doesn't work as literally stated: `AddSingleton<IMessagePublisher, X>()` can't cleanly express three distinct instances of the same interface (last-registered-wins). Resolved by constructing all three (and everything built from them -- `CommandRouter`, `OutboundMessageForwarder`, `OrderIntakeHandler`) directly after `builder.Build()`, resolving `IAmazonSQS`/`IHostApplicationLifetime` from `app.Services` once, rather than through further DI registration -- consistent with how choreography's Host already constructs `EventBus`/participants directly rather than forcing everything through the container.
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/Program.cs` — DI for the AWS clients
        (via task 4's factory), the three `IMessagePublisher`s (inventory queue, stateless-responder
        queue, and — unused for publishing but registered for symmetry — none needed for its own
        inbound), `CommandRouter`, `CoordinatorWiring.Wire(...)`, `OutboundMessageForwarder`, the
        `POST /orders` and `GET /health` endpoints, `SqsPollingBackgroundService` against
        `coordinator-inbound`
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

### InventoryHost (`src/OrderSaga.Orchestration.InventoryHost`)

- [x] 15. Scaffold the project and test project
      - ⚠ Retro: same gap as task 10 -- `Microsoft.NET.Sdk.Worker` also requires an entry point to compile at all, and also needed an explicit `Microsoft.Extensions.Hosting` package reference (only `Microsoft.Extensions.Hosting.Abstractions` came in transitively via the `Messaging` project reference; `Host.CreateApplicationBuilder` itself lives in the non-Abstractions package). Added a minimal placeholder `Program.cs` (`Host.CreateApplicationBuilder(args); ...; host.Run();`); task 17 fleshes it out.
      - File(s): `src/OrderSaga.Orchestration.InventoryHost/OrderSaga.Orchestration.InventoryHost.csproj`
        (plain `Microsoft.NET.Sdk.Worker` — no HTTP surface — references `OrderSaga.Orchestration`,
        `OrderSaga.Orchestration.Messaging`, `OrderSaga.Shared`, `Inventory.Domain`),
        `tests/OrderSaga.Orchestration.InventoryHost.Tests/OrderSaga.Orchestration.InventoryHost.Tests.csproj`,
        added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Orchestration.InventoryHost.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 16. Inventory wiring (testable composition-root extraction)
      - ⚠ Retro: as anticipated in task 13's flag, `InventoryResponder` had the same single-`EventBus`-constructor gap. Fixed with the same two-parameter constructor, updated `InventoryResponderTests.cs` and `OrderSagaOrchestrationIntegrationTests.cs` to match.
      - File(s): `src/OrderSaga.Orchestration.InventoryHost/InventoryWiring.cs`
      - Test: `tests/OrderSaga.Orchestration.InventoryHost.Tests/InventoryWiringTests.cs` —
        `Wire_ReserveStockCommandOnInbound_PublishesStockReservedReplyOnOutboundOnly`

- [x] 17. Wire the InventoryHost application
      - File(s): `src/OrderSaga.Orchestration.InventoryHost/Program.cs` — DI for the AWS clients,
        the single `IMessagePublisher` (always `coordinator-inbound`), seeded in-memory
        `InventoryItem` state (same fixed demo SKU as choreography), `InventoryWiring.Wire(...)`,
        `OutboundMessageForwarder`, `SqsPollingBackgroundService` against `inventory-commands`
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

### ResponderHost (`src/OrderSaga.Orchestration.ResponderHost`)

- [x] 18. Scaffold the project and test project
      - File(s): `src/OrderSaga.Orchestration.ResponderHost/OrderSaga.Orchestration.ResponderHost.csproj`
        (plain `Microsoft.NET.Sdk.Worker` — references `OrderSaga.Orchestration`,
        `OrderSaga.Orchestration.Messaging`, `OrderSaga.Shared`),
        `tests/OrderSaga.Orchestration.ResponderHost.Tests/OrderSaga.Orchestration.ResponderHost.Tests.csproj`,
        added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Orchestration.ResponderHost.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 19. Responder wiring (testable composition-root extraction, both responders)
      - ⚠ Retro: as predicted, `PaymentResponder` and `ShippingResponder` had the same single-`EventBus`-constructor gap as `SagaCoordinator`/`InventoryResponder`. Fixed both with the same two-parameter constructor; updated `PaymentResponderTests.cs`, `ShippingResponderTests.cs`, `OrderSagaOrchestrationIntegrationTests.cs` to match. This closes out the pattern across all four `OrderSaga.Orchestration` classes.
      - File(s): `src/OrderSaga.Orchestration.ResponderHost/ResponderWiring.cs`
      - Test: `tests/OrderSaga.Orchestration.ResponderHost.Tests/ResponderWiringTests.cs` —
        `Wire_ChargePaymentCommandOnInbound_PublishesPaymentReplyOnOutboundOnly`,
        `Wire_ScheduleShipmentCommandOnInbound_PublishesShipmentScheduledReplyOnOutboundOnly`

- [x] 20. Wire the ResponderHost application
      - File(s): `src/OrderSaga.Orchestration.ResponderHost/Program.cs` — DI for the AWS clients,
        the single `IMessagePublisher` (always `coordinator-inbound`), `ResponderWiring.Wire(...)`,
        `OutboundMessageForwarder`, `SqsPollingBackgroundService` against
        `stateless-responder-commands`
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

### Terraform (`infra/modules/`, `infra/orchestration/`)

- [x] 21. SQS-only messaging module (three queues, three DLQs, three redrive policies — no
      EventBridge bus or rules)
      - File(s): `infra/modules/orchestration-messaging/*.tf` —
        `inventory-commands`/`stateless-responder-commands`/`coordinator-inbound` queues, each with
        its own DLQ
      - Verification: `terraform validate` (from `infra/orchestration/` once task 23 wires it in)

- [x] 22. Make the `compute` module's ALB attachment optional
      - ⚠ Retro: the task's own description only anticipated the ALB block itself needing to become conditional, but implementing it surfaced a second, related gap: the module's `environment` block hardcoded choreography-specific variable names (`Sqs__QueueUrl`, `EventBridge__BusName`) directly in `ecs.tf`, which don't generalize to orchestration's three services' completely different (and non-overlapping) configuration needs -- none of them use EventBridge at all. Replaced with a generic `environment_variables` list-of-`{name,value}` supplied by the caller, merged with a fixed `AWS_REGION` baseline via `concat()`. This also required updating choreography's own `infra/main.tf` root wiring (`queue_url`/`event_bus_name` args → an `environment_variables` list) -- re-validated with `terraform validate` against the existing choreography root to confirm its behavior is unchanged, per this task's own stated verification.
      - File(s): `infra/modules/compute/*.tf` — `target_group_arn`/`app_port` (for the
        load-balancer attachment specifically) become optional variables; the `load_balancer` block
        in the ECS service resource is conditional on a target group being supplied
      - Verification: `terraform validate` passes for a new orchestration usage with no target
        group supplied; **re-run `terraform validate` against the existing choreography `infra/`
        root too**, confirming its always-supplied-target-group usage is unaffected by this change.

- [x] 23. Root module wiring for orchestration
      - ⚠ Retro: implementing this surfaced a fourth instance of the same class of gap tasks 22
        found (module reuse assumptions baked in for choreography's single-service shape):
        `iam-and-observability`'s task-role IAM policy was hardcoded to EventBridge PutEvents + one
        queue + one table, which doesn't fit orchestration's three services' different,
        non-overlapping permission needs (none use EventBridge; each needs a different SQS
        send/receive combination). Generalized the same way as `compute`'s environment variables --
        a caller-supplied `task_policy_statements` list of `{actions, resources}`, built into the
        policy document via a `dynamic "statement"` block. Also required updating choreography's
        `infra/main.tf` to match; re-validated with `terraform validate` there too, confirming its
        behavior is unchanged. Also added one small root-level resource
        (`aws_security_group.background_worker`) outside any module, for the two non-ALB services'
        outbound-only security needs -- proportionate to its size rather than a new module for one
        resource.
      - File(s): `infra/orchestration/{main,variables,outputs,versions}.tf` — one `networking`
        instance; one `load-balancer` instance (Coordinator only); three `iam-and-observability`
        instances (distinct ECR repo/log group/IAM role per service); three `compute` instances
        (Coordinator's supplies a target group, Inventory's and Responder's don't, per task 22);
        one `orchestration-messaging` instance; one `idempotency` instance with a distinct table
        name (not choreography's)
      - Verification: `terraform validate` passes on the complete configuration

### Bridging app and infra

- [x] 24. Dockerfile for CoordinatorHost
      - ⚠ Retro: first build attempt failed -- `OrderSaga.Orchestration.csproj` references `Inventory.Domain` transitively (for `InventoryResponder`), which CoordinatorHost itself never uses directly, but `dotnet restore`/`publish` still needs that project's source copied into the build context to resolve the reference chain. Added the `Inventory.Domain.csproj`/source `COPY` lines even though this Host has no direct dependency on it.
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/Dockerfile`
      - Verification: `docker build` succeeds locally

- [x] 25. Dockerfile for InventoryHost
      - File(s): `src/OrderSaga.Orchestration.InventoryHost/Dockerfile` — no `EXPOSE`/port binding,
        matching its worker-style, no-HTTP shape
      - Verification: `docker build` succeeds locally

- [x] 26. Dockerfile for ResponderHost
      - File(s): `src/OrderSaga.Orchestration.ResponderHost/Dockerfile` — same shape as task 25
      - Verification: `docker build` succeeds locally

### Validation

- [x] 27. LocalStack validation
      - No bugs found -- unlike choreography's task 21, which found and fixed 3 real defects
        (SQS message shape, the republish loop, an unhandled-exception host crash). This time the
        design notes at the top of this plan closed each of those defect classes off before they
        could recur: the message envelope was never EventBridge-wrapped to begin with (direct SQS,
        no "detail" nesting to get wrong); the inbound/outbound split was enforced by the type
        system from task 13 onward, not retrofitted after a bug; `SqsPollingBackgroundService`
        already had the null-`Messages`/outer-try-catch hardening built in from task 9. Happy path
        (9 claims), insufficient stock (3 claims), and payment declined (7 claims) all ran cleanly
        against real SQS/DynamoDB via LocalStack -- 19 total claims, all three queues empty
        afterward, zero errors/warnings across all three Hosts' logs. Confirmed via direct SQS/
        DynamoDB query API calls, not log inspection alone.
      - File(s): `docs/localstack-setup.md` (extended with this deployment's setup, or a new
        `docs/localstack-setup-orchestration.md` if the two flows diverge enough to warrant it —
        implementation detail)
      - Verification: `terraform apply` against LocalStack succeeds for the `orchestration-messaging`
        and `idempotency` modules; all three Host apps run locally via `dotnet run` (not
        containerized), pointed at LocalStack; a full saga run through the Coordinator's HTTP
        endpoint produces the same observable outcome as the existing in-process orchestration
        integration tests, confirmed via each queue's `ApproximateNumberOfMessages` (all three back
        to 0) and the shared idempotency table's claim count, for happy path, insufficient stock,
        and payment declined — not just log inspection.

- [x] 28. Real AWS deployment
      - File(s): `infra/orchestration/main.tf` (Coordinator's `task_policy_statements` fixed),
        `src/OrderSaga.Orchestration.Messaging/SqsPollingBackgroundService.cs` (null-safety fix)
      - Verification: `terraform apply` against real AWS succeeded (57 resources, applied in two
        passes so ECS didn't launch against images that didn't exist yet); after both real findings
        below were fixed and the images rebuilt/redeployed, all three saga paths ran cleanly for
        real from a clean baseline — happy path (9 claims), insufficient stock (3 claims), payment
        declined (7 claims), exactly matching task 27's LocalStack results, all three queues empty
        afterward, zero errors. Torn down immediately after; confirmed via direct AWS queries
        (VPC, NAT, ECS, ALB, ECR, SQS, DynamoDB all absent) that nothing is left running or
        accruing cost.
      - ⚠ Retro: two real findings from this task, both LocalStack didn't and couldn't have caught.
        1. **IAM policy gap.** The Coordinator's task role granted `sqs:SendMessage` on the two
           command queues but not on `coordinator-inbound` itself — missed that
           `OrderIntakeHandler` also needs to publish the initial `OrderPlaced` trigger onto that
           same queue it polls, not just the two command queues it routes to. First happy-path
           attempt failed outright (`AmazonSQSException`, HTTP 500) before any message flowed.
           Fixed by adding `coordinator_inbound_queue_arn` to that statement's resources.
        2. **`deleteResponse.Failed` null on real AWS, not LocalStack.** Same defect class
           choreography found once already (`response.Messages` null on an empty poll) recurring in
           a different, unguarded call: `SqsPollingBackgroundService`'s `DeleteMessageBatchAsync`
           result's `Failed` collection is `null` (not an empty list) on real AWS when every entry
           in the batch succeeds — LocalStack's emulation returned an empty list instead, so
           `foreach (var failed in deleteResponse.Failed)` never threw during task 27's validation.
           Recurred identically on all three Hosts (shared code, correctly reused). Fixed with the
           same `?? []` pattern already used for `response.Messages`; rebuilt and redeployed all
           three images before re-verifying (not repeating choreography's stale-image mistake).
        3. **Not a code defect, but a real operational finding worth recording:** after
           `force-new-deployment`, ECS's `rolloutState` reports `COMPLETED` (the new task is healthy
           and serving) well before the *old* task actually stops — its background SQS poller kept
           running and racing the new task for messages on the same queue for several minutes
           afterward (ALB deregistration delay). Triggered exactly the already-documented, already-
           deferred `SagaState`-persistence gap (an order's in-memory saga state existing in the old
           task's soon-to-be-killed process, not the new one) during the fix-and-redeploy cycle
           itself. Both affected messages correctly landed in the DLQ after exhausting retries —
           the DLQ-backed retry pattern working exactly as designed for an unrecoverable case, not a
           new gap. Resolved by re-verifying only after confirming (via `list-tasks --desired-status
           RUNNING` and `STOPPED`) that just one task per service was actually running, not by
           trusting `rolloutState` alone.
