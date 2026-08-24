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

- [ ] 12. SQS queue + DLQ module
      - File(s): `infra/modules/messaging/sqs.tf` — the shared queue, its DLQ, and a redrive
        policy (small `maxReceiveCount`)
      - Verification: `terraform validate` passes

- [ ] 13. EventBridge bus and rules module
      - File(s): `infra/modules/messaging/eventbridge.tf` — custom event bus, one rule per known
        event type, all targeting the SQS queue from task 12
      - Verification: `terraform validate` passes

- [ ] 14. DynamoDB idempotency table module
      - File(s): `infra/modules/idempotency/dynamodb.tf`
      - Verification: `terraform validate` passes

- [ ] 15. IAM roles, ECR repository, CloudWatch log group
      - File(s): `infra/modules/iam-and-observability/*.tf` — ECS task execution role, task role
        (least-privilege: EventBridge `PutEvents`, SQS receive/delete, DynamoDB conditional
        `PutItem`), ECR repository, CloudWatch log group
      - Verification: `terraform validate` passes

- [ ] 16. ALB and security groups module
      - File(s): `infra/modules/load-balancer/*.tf`
      - Verification: `terraform validate` passes

- [ ] 17. ECS cluster and Fargate service module
      - File(s): `infra/modules/compute/*.tf` — ECS cluster, Fargate task definition (referencing
        the ECR repo, IAM roles, CloudWatch log group), service registered behind the ALB.
        **Desired count 1, not ≥2** — see task 10's retro flag: `InventoryItem`'s in-memory state
        isn't shared across instances, so a second instance would silently diverge from the first
        the moment a saga's events got load-balanced across both. The VPC/subnets/ALB stay
        multi-AZ-capable regardless (free, no reason not to); only the Fargate instance count is
        scoped down until a future persistence spec makes multi-instance state safe.
      - Verification: `terraform validate` passes

- [ ] 18. VPC endpoints for NAT cost minimization
      - File(s): `infra/modules/networking/vpc-endpoints.tf` — ECR (api + dkr), S3 gateway
        endpoint (for ECR image layers), CloudWatch Logs; exact endpoint list finalized here based
        on what task 17's Fargate service actually needs
      - Verification: `terraform validate` passes

- [ ] 19. Root module wiring
      - File(s): `infra/main.tf`, `infra/variables.tf`, `infra/outputs.tf` — composes all modules
        (11-18) together with proper references
      - Verification: `terraform validate` passes on the complete configuration

### Bridging app and infra

- [ ] 20. Dockerfile for the Host application
      - File(s): `src/OrderSaga.Choreography.Host/Dockerfile`
      - Verification: `docker build` succeeds locally

### Validation

- [ ] 21. LocalStack validation
      - File(s): `infra/localstack.tf` or equivalent LocalStack-specific override/config,
        `docs/localstack-setup.md` (or similar) documenting how to run it
      - Verification: `terraform apply` against LocalStack succeeds; one full saga run through the
        HTTP intake endpoint (LocalStack-backed EventBridge/SQS/DynamoDB) ends with the same
        observable outcome as the existing in-process integration tests (happy path, both
        compensation paths) — confirmed manually, not as an automated xUnit test.

- [ ] 22. Real AWS deployment
      - File(s): none new
      - Verification: `terraform apply` against real AWS succeeds; one real order run through the
        deployed HTTP endpoint completes successfully; teardown/cost note recorded once confirmed
        working, so the deployment isn't left running indefinitely by accident.
