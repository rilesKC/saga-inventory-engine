# Choreography — Real AWS Infrastructure — Plan

Spec: docs/specs/choreography-aws-infra.md

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):
- **Terraform tasks don't use the xUnit "Test:" convention** — there's nothing to unit test in
  HCL. Their verification step is `terraform validate` (from `infra/`), and later, real behavior
  is verified against LocalStack (tasks 20-21) and then real AWS (task 22), not unit tests. Flagged
  explicitly since this repo's established test-first discipline doesn't cover infrastructure code.
- **Message `MessageId` is assigned once, at envelope-creation time** (when an event is first
  published to EventBridge), not re-generated on every read. SQS redelivers the same message body
  on retry, so the envelope — and its `MessageId` — stays stable across redeliveries, which is
  exactly what the idempotency check needs to recognize "have I already processed this."
- **SQS message processing logic is decoupled from the actual SQS receive/delete loop.** A
  `SqsMessageProcessor.ProcessMessage(string rawBody)` does the real work (deserialize, claim
  check, dispatch) and is fully unit-testable; the thin `BackgroundService` wrapping the actual
  AWS SDK receive/delete calls (task 9) is verified only via LocalStack, not unit tests — it's pure
  AWS SDK plumbing with no logic of its own worth unit-testing in isolation.
- **The HTTP intake endpoint's logic lives in a small handler class**, not inline in `Program.cs`
  minimal-API route registration — keeps it unit-testable without pulling in
  `Microsoft.AspNetCore.Mvc.Testing`/`WebApplicationFactory` for a first pass.
- **`OutboundEventForwarder` subscribes to all 8 known event types explicitly** (`OrderPlaced`,
  `StockReserved`, `StockReservationFailed`, `PaymentCharged`, `PaymentDeclined`,
  `ReservationConfirmed`, `ReservationReleased`, `ShipmentScheduled`) rather than adding an
  "any event" hook to the already-tested `EventBus` — keeps that shared, tested code untouched.
  Its own test proves the mechanism with one representative type (`OrderPlaced`), same precedent
  as `SagaEventsTests` only testing one of five event types when they were introduced together.

## Tasks

### App code (`src/OrderSaga.Choreography.Host`)

- [x] 1. Scaffold the Host project and test project
      - File(s): `src/OrderSaga.Choreography.Host/OrderSaga.Choreography.Host.csproj` (ASP.NET
        Core minimal API, references `OrderSaga.Choreography`, `OrderSaga.Shared`,
        `Inventory.Domain`), `tests/OrderSaga.Choreography.Host.Tests/OrderSaga.Choreography.Host.Tests.csproj`,
        added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Choreography.Host.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 2. Message envelope and event-type registry
      - File(s): `src/OrderSaga.Choreography.Host/EventEnvelope.cs`,
        `src/OrderSaga.Choreography.Host/EventTypeRegistry.cs`
      - Test: `tests/OrderSaga.Choreography.Host.Tests/EventEnvelopeTests.cs` —
        `Serialize_OrderPlaced_ProducesEnvelopeWithEventTypeMessageIdAndPayload`,
        `Deserialize_EnvelopeWithOrderPlacedType_ReconstructsOriginalEvent`

- [x] 3. Idempotency store abstraction + in-memory fake
      - File(s): `src/OrderSaga.Choreography.Host/IIdempotencyStore.cs`,
        `src/OrderSaga.Choreography.Host/InMemoryIdempotencyStore.cs` (test double, not
        production code — production uses the DynamoDB-backed implementation from task 8)
      - Test: `tests/OrderSaga.Choreography.Host.Tests/InMemoryIdempotencyStoreTests.cs` —
        `TryClaim_FirstTime_ReturnsTrue`, `TryClaim_DuplicateMessageId_ReturnsFalse`

- [x] 4. Outbound event forwarder
      - File(s): `src/OrderSaga.Choreography.Host/IEventPublisher.cs`,
        `src/OrderSaga.Choreography.Host/OutboundEventForwarder.cs`
      - Test: `tests/OrderSaga.Choreography.Host.Tests/OutboundEventForwarderTests.cs` —
        `Publish_OrderPlaced_ForwardsToEventPublisher` (using a recording fake `IEventPublisher`)

- [x] 5. SQS message processor (claim-check + dispatch)
      - File(s): `src/OrderSaga.Choreography.Host/SqsMessageProcessor.cs`
      - Test: `tests/OrderSaga.Choreography.Host.Tests/SqsMessageProcessorTests.cs` —
        `ProcessMessage_NewEnvelope_ClaimsAndDispatchesToEventBus`,
        `ProcessMessage_DuplicateMessageId_SkipsDispatch`

- [x] 6. HTTP intake handler
      - File(s): `src/OrderSaga.Choreography.Host/OrderIntakeHandler.cs`
      - Test: `tests/OrderSaga.Choreography.Host.Tests/OrderIntakeHandlerTests.cs` —
        `Handle_ValidRequest_PublishesOrderPlacedViaEventPublisher`

- [x] 7. Real EventBridge-backed `IEventPublisher`
      - File(s): `src/OrderSaga.Choreography.Host/EventBridgeEventPublisher.cs`
      - Verification: compiles against the AWS SDK's `IAmazonEventBridge`; real behavior verified
        via LocalStack (task 21), not a unit test — thin wrapper over `PutEventsAsync` with no
        independent logic.

- [x] 8. Real DynamoDB-backed `IIdempotencyStore`
      - File(s): `src/OrderSaga.Choreography.Host/DynamoDbIdempotencyStore.cs`
      - Verification: compiles against the AWS SDK's `IAmazonDynamoDB`; real behavior (the
        conditional-write claim semantics) verified via LocalStack (task 21), not a unit test.

- [x] 9. SQS polling background service
      - File(s): `src/OrderSaga.Choreography.Host/SqsPollingBackgroundService.cs`
      - Verification: compiles against the AWS SDK's `IAmazonSQS`; wraps `SqsMessageProcessor`
        (already unit-tested in task 5) with the actual receive/delete loop. Real behavior
        verified via LocalStack (task 21).

- [x] 10. Wire the Host application
      - File(s): `src/OrderSaga.Choreography.Host/Program.cs` — registers the intake endpoint
        (`POST /orders` → `OrderIntakeHandler`), the background poller, DI for every abstraction
        and its real implementation, `InventoryParticipant`/`PaymentStub`/`ShippingStub` and a
        shared `EventBus` instance
      - Verification: `dotnet build` succeeds; the full solution's existing test suite still
        passes unchanged.
      - ⚠ Retro: wiring the app together surfaced a real gap the spec's "defer persistence"
        decision didn't fully reason through — multi-AZ means desired count ≥2, i.e. multiple
        Fargate instances, each with its own separate in-process `EventBus` and in-memory
        `InventoryItem`. The shared SQS queue load-balances which instance receives any given
        message, so a saga's events landing on different instances would silently diverge (one
        instance reserves stock; a later step lands on a different instance whose copy never saw
        that reservation). The same multi-instance reasoning that made the idempotency store need
        to be DynamoDB (shared) rather than in-memory was never applied to `InventoryItem` itself.
        Resolved by user decision: **scope this deployment to a single Fargate instance (desired
        count 1)** for now, deferring multi-instance-safe aggregate state to a future persistence
        spec, rather than pulling persistence into this spec or accepting the correctness gap.
        Task 17 updated accordingly. The signed-off spec's text is left as-is (not retroactively
        edited) per this project's established precedent — the plan file, not the spec, is where
        mid-implementation discoveries and their resolutions get recorded.

### Terraform (`infra/`)

- [x] 11. Networking module (VPC, multi-AZ)
      - File(s): `infra/modules/networking/*.tf` — VPC, public/private subnets across ≥2 AZs, IGW,
        route tables
      - Verification: `terraform validate` (from `infra/`) passes

- [x] 12. SQS queue + DLQ module
      - File(s): `infra/modules/messaging/sqs.tf` — the shared queue, its DLQ, and a redrive
        policy (small `maxReceiveCount`)
      - Verification: `terraform validate` passes

- [x] 13. EventBridge bus and rules module
      - File(s): `infra/modules/messaging/eventbridge.tf` — custom event bus, one rule per known
        event type, all targeting the SQS queue from task 12
      - Reminder for task 19 (root wiring): pass `name = "order-saga-choreography"` to this
        module so the created bus name matches `EventBridgeEventPublisher`'s hardcoded
        `EventBusName` constant (flagged as a forward-looking note back in task 7's review).
      - Verification: `terraform validate` passes

- [x] 14. DynamoDB idempotency table module
      - File(s): `infra/modules/idempotency/dynamodb.tf`
      - Verification: `terraform validate` passes

- [x] 15. IAM roles, ECR repository, CloudWatch log group
      - File(s): `infra/modules/iam-and-observability/*.tf` — ECS task execution role, task role
        (least-privilege: EventBridge `PutEvents`, SQS receive/delete, DynamoDB conditional
        `PutItem`), ECR repository, CloudWatch log group
      - Verification: `terraform validate` passes

- [x] 16. ALB and security groups module
      - File(s): `infra/modules/load-balancer/*.tf`
      - Verification: `terraform validate` passes
      - ⚠ Retro: the ALB target group's health check needs a real endpoint, and `Program.cs`
        (task 10) didn't have one — added a minimal `GET /health` to `Program.cs` as part of this
        task rather than pointing the health check at something that doesn't exist. Small, but
        another instance of infra work surfacing a gap in already-completed app-code wiring.

- [x] 17. ECS cluster and Fargate service module
      - File(s): `infra/modules/compute/*.tf` — ECS cluster, Fargate task definition (referencing
        the ECR repo, IAM roles, CloudWatch log group), service registered behind the ALB.
        **Desired count 1, not ≥2** — see task 10's retro flag: `InventoryItem`'s in-memory state
        isn't shared across instances, so a second instance would silently diverge from the first
        the moment a saga's events got load-balanced across both. The VPC/subnets/ALB stay
        multi-AZ-capable regardless (free, no reason not to); only the Fargate instance count is
        scoped down until a future persistence spec makes multi-instance state safe.
      - Verification: `terraform validate` passes
      - ⚠ Retro: first draft used `depends_on = [var.listener_arn]` to sequence the ECS service
        after the ALB listener — invalid Terraform (`depends_on` requires a real resource/module
        reference, not a string variable holding an ARN). Caught before ever running `validate`.
        Removed the unusable `listener_arn` variable; task 19's root module must instead declare
        `depends_on = [module.load_balancer]` on this module to get the same ordering guarantee
        across the module boundary.

- [x] 18. Internet egress for the private subnets
      - File(s): `infra/modules/networking/egress.tf` (renamed from the plan's original
        `vpc-endpoints.tf`) — free S3 and DynamoDB gateway endpoints, plus a single (not per-AZ)
        NAT Gateway for everything else (ECR pulls, CloudWatch Logs, EventBridge, SQS)
      - Verification: `terraform validate` passes
      - ⚠ Retro: the plan's original framing ("VPC endpoints for NAT cost minimization") assumed
        interface endpoints are always cheaper than NAT. At this deployment's real scale (a single
        instance, near-zero traffic), five interface endpoints (ECR api+dkr, Logs, EventBridge,
        SQS) across 2 AZs cost more per month (~$73) than one NAT Gateway (~$32) — the fixed
        hourly cost per endpoint-per-AZ dominates when there's no real traffic volume for the
        interface endpoints' cheaper per-GB rate to offset. Resolved by user decision: gateway
        endpoints (genuinely free) for S3/DynamoDB, one single NAT Gateway for the rest. Worth
        remembering as a general lesson, not just for this project: "VPC endpoints beat NAT" is a
        real-traffic-scale rule of thumb, not a universal one.

- [x] 19. Root module wiring
      - File(s): `infra/main.tf`, `infra/variables.tf`, `infra/outputs.tf` — composes all modules
        (11-18) together with proper references
      - Verification: `terraform validate` passes on the complete configuration

### Bridging app and infra

- [x] 20. Dockerfile for the Host application
      - File(s): `src/OrderSaga.Choreography.Host/Dockerfile`, `.dockerignore` (new, repo root)
      - Verification: `docker build` succeeds locally
      - ⚠ Retro: first build attempt failed with a NuGet "fallback package folder" error referencing
        a Windows-only path. Root cause: `COPY src/.../ src/.../` copied the *host's* Windows-built
        `bin/`/`obj/` directories into the Linux build container, clobbering the container's own
        clean restore with Windows-specific paths baked into `project.assets.json`. Fixed with a
        `.dockerignore` excluding `**/bin/`/`**/obj/` — a real, non-obvious Docker-on-Windows
        gotcha, not caught by anything in the plan or spec, only by actually running the build.

### Validation

- [x] 21. LocalStack validation (scope adjusted -- see retro)
      - File(s): `docker-compose.localstack.yml` (repo root), `docs/localstack-setup.md`
      - Verification: `terraform apply` against LocalStack succeeds for the `messaging` and
        `idempotency` modules only (not the whole root module); the Host application run locally
        via `dotnet run` (not containerized, not through ECS), configured to point at LocalStack's
        endpoint, completes one full saga run through the HTTP intake endpoint with the same
        observable outcome as the existing in-process integration tests — confirmed manually, not
        an automated xUnit test.
      - ⚠ Retro: LocalStack's free Community edition doesn't actually emulate VPC/ALB/ECS/ECR --
        those require LocalStack Pro, and it now also requires a free account/auth token just to
        run at all (not just for Pro features) -- a real, current-state surprise, resolved by the
        user creating a free account and the token being wired into a gitignored `.env` file, never
        committed. Running `terraform apply` against the whole root module would have failed or
        silently no-op'd on those resources. Resolved by user decision: validate only the modules
        with real custom application logic riding on them (messaging,
        idempotency), and run the Host app locally rather than through ECS to exercise the actual
        AWS SDK integration code. The VPC/ALB/ECS Terraform (already `terraform validate`-clean)
        gets its real test in task 22's actual AWS deploy instead.
      - ⚠ Retro: this run found and fixed three real bugs that no unit test could have — the whole
        reason this task's adjusted scope (run against real EventBridge/SQS/DynamoDB) still mattered
        despite not covering the VPC/ALB/ECS layer:
        1. **SQS message shape.** `SqsMessageProcessor` deserialized the raw SQS body directly as
           `EventEnvelope`, but a message delivered by an EventBridge rule target is the *full*
           EventBridge event structure, with the envelope nested under `"detail"`. `MessageId` came
           back null, which DynamoDB rejected as an empty `AttributeValue`. Fixed by parsing the
           `detail` property first. Existing unit tests passed because their fake message bodies
           were shaped like the envelope directly, not like a real EventBridge event — updated them
           to match reality.
        2. **Unbounded republish loop.** `OutboundEventForwarder` and every participant shared one
           `EventBus`. The poller delivering a message via `EventBus.Publish` triggered *both* the
           intended participant reaction *and* the forwarder re-sending that same event back to
           EventBridge — which came back as a new message and repeated forever. Worse, participants
           were also reacting to each other directly and synchronously in-process, completely
           bypassing the SQS round-trip the spec calls for, so a step could be processed twice even
           with re-forwarding suppressed. Fixed with a genuine two-bus separation (inbound: poller →
           participants; outbound: participants → forwarder) — see `InventoryParticipant`'s
           constructor doc comment for the full reasoning. This touched already-tested code from the
           *earlier* choreography plan (`InventoryParticipant`, `PaymentStub`, `ShippingStub`), not
           just this plan's own files — a cross-plan correction, done deliberately and reviewed, not
           silently.
        3. **Unhandled exception crashing the whole host.** `response.Messages` came back `null`
           (not an empty list) on an empty LocalStack poll; the resulting `NullReferenceException`
           wasn't caught by the per-message `try`/`catch` (it happened in the `foreach` itself) and
           took the entire application down, since `HostOptions.BackgroundServiceExceptionBehavior`
           defaults to `StopHost`. Fixed with a null-coalescing guard and an outer `try`/`catch`
           around the whole poll iteration, so a transient AWS SDK error degrades to a retry-after-
           delay instead of killing the app.
        Final verification: happy path, insufficient-stock, and payment-declined all run cleanly —
        11 total idempotency claims (5 + 2 + 4, exactly matching each path's expected event count),
        both the main queue and DLQ empty afterward. Confirmed via direct SQS/DynamoDB query API
        calls (no AWS CLI installed), not just log inspection.

- [x] 22. Real AWS deployment
      - File(s): `infra/modules/iam-and-observability/ecr.tf` (added `force_delete = true`)
      - Verification: `terraform apply` against real AWS succeeded (52 resources: 49 infra +
        3 compute, applied in two passes so the ECS service didn't try to launch against an
        image that didn't exist yet); all three saga paths run for real (happy path, insufficient
        stock, payment declined) — 11 total idempotency claims exactly matching LocalStack's
        result (5+2+4), both queues empty afterward, verified via the AWS CLI (installed via
        winget) directly against SQS/DynamoDB, not log inspection alone. Torn down immediately
        after confirming it worked; confirmed via direct AWS queries (NAT gateway, ALB, ECS
        cluster, VPC, ECR all gone) that nothing was left running or accruing cost.
      - ⚠ Retro: two real findings from this task.
        1. **The deployed image was stale.** The Docker image had been built in task 20, *before*
           any of task 21's LocalStack-discovered fixes. Every fix after that was validated by
           running `dotnet run` locally, not by rebuilding the image -- so what got pushed to ECR
           at the start of this task still had the pre-fix bugs, including the exact same
           `NullReferenceException` that crashed the whole host. Rebuilding the image before
           pushing is now something to do as standard practice whenever code changes after the
           last `docker build`, not just once per plan.
        2. **`terraform destroy` failed outright on the ECR repository** once an image had been
           pushed to it -- ECR refuses to delete a non-empty repository, and `aws_ecr_repository`
           doesn't default to `force_delete`. Given this project's whole deployment model is
           "verify, then tear down immediately," this would have failed on *every* real deploy.
           Fixed by adding `force_delete = true` -- but the fix itself required its own `apply`
           before `destroy` would honor it, since the flag lives in Terraform state, not just the
           `.tf` file; changing the file alone didn't retroactively affect the already-created
           resource.
