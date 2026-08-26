# Saga Persistence — MongoDB Atlas + S3 — Plan

Spec: docs/specs/saga-persistence.md

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):

- **`IInventoryEventStore` lives in `Inventory.Domain` itself**, not a Host-layer project.
  `Inventory.Domain` is already dependency-free (referenced by both `OrderSaga.Choreography` and
  `OrderSaga.Orchestration` with zero external packages), so an interface there costs nothing and
  keeps `InventoryParticipant`/`InventoryResponder` free of any Mongo/AWS SDK reference — exactly
  how they're already free of any SQS/EventBridge reference via `InboundEventBus`/`OutboundEventBus`.
  Same reasoning puts `ISagaStateStore` in `OrderSaga.Orchestration` (also dependency-free) rather
  than a Host project.
- **`InventoryParticipant` and `InventoryResponder` can't stay untouched.** The spec's Out of Scope
  says no business-logic changes, and this isn't one (the state machine itself doesn't change), but
  a zero-touch "observe the outbound bus and persist whatever crosses it" design — which would have
  left both classes completely untouched — turns out not to work: orchestration's `InventoryResponder`
  never puts the raw `Inventory.Domain` events (`StockReserved`, etc.) on its outbound bus at all —
  it publishes its own reply types instead (`StockReservedReply`), and those don't carry enough data
  to reconstruct the original event from outside (`StockReservedReply` has no `Quantity`). So both
  classes get the same small, symmetric change instead: an injected `IInventoryEventStore`, called
  right after `item.Handle(...)` with whatever's new in `item.UncommittedEvents`. Same class of
  change as the earlier `InboundEventBus`/`OutboundEventBus` split — wiring, not new behavior.
- **`EventBus.Subscribe<T>` only takes `Action<T>`** (synchronous) — the event-store append call
  inside these handlers uses `.GetAwaiter().GetResult()`, the same blocking-sync-over-async pattern
  this project already uses for `SqsMessagePublisher.Publish`/`EventBridgeEventPublisher`.
- **New shared project: `src/Saga.Persistence`** (not `Inventory.Persistence` — it holds both the
  Inventory event store and the `SagaState` store, since both need the same Mongo-client
  construction and the same S3-dual-write decorator machinery, just different collection shapes).
  Referenced by all three Hosts that need real persistence
  (`OrderSaga.Choreography.Host`, `OrderSaga.Orchestration.InventoryHost`,
  `OrderSaga.Orchestration.CoordinatorHost`).
- **`SagaCoordinator`'s seven near-identical `_sagas[orderId] = state;` lines become one private
  `SetSaga` helper** that updates the dictionary and calls `_store.SaveAsync(state)` — a
  simplification that falls out naturally from adding the persistence call once instead of seven
  times, not a separate refactor.
- **Startup rehydration loads every persisted saga, not just non-terminal ones.** At this project's
  demo scale, filtering out `Failed`/`Compensated`/`Completed` sagas at load time isn't worth the
  extra query complexity — simplicity over a micro-optimization nothing here needs.
- **Terraform: one new reusable module (`infra/modules/persistence`), instantiated once from each
  of the two independent roots** (`infra/`, `infra/orchestration/`) — not a shared third root. Same
  reuse-across-independent-roots pattern the `compute`/`iam-and-observability` modules already use,
  just applied to Atlas/S3 instead of ECS/IAM.
- **MongoDB Atlas M0 doesn't support VPC peering or PrivateLink** (a paid-dedicated-tier-only
  feature) — network access is an IP allowlist scoped to each stack's existing NAT gateway's static
  Elastic IP, not `0.0.0.0/0`.
- **Terraform tasks don't use the xUnit "Test:" convention** — same as both prior infra plans;
  nothing to unit test in HCL. Verification is `terraform validate`, then LocalStack (S3 only —
  MongoDB Atlas isn't an AWS service, so LocalStack can't emulate it), then the real Atlas cluster.

## Tasks

### `Inventory.Domain` — event store interface (pure, no new dependencies)

- [x] 1. `IInventoryEventStore` interface + in-memory fake
      - File(s): `src/Inventory.Domain/IInventoryEventStore.cs` — `Task AppendRangeAsync(string sku,
        IReadOnlyList<object> events, CancellationToken)`, `Task<IReadOnlyList<object>>
        LoadEventsAsync(string sku, CancellationToken)`; `src/Inventory.Domain/InMemoryInventoryEventStore.cs`
      - Test: `tests/Inventory.Domain.Tests/InMemoryInventoryEventStoreTests.cs` —
        `AppendRangeAsync_ThenLoadEventsAsync_ReturnsAppendedEventsInOrder`,
        `LoadEventsAsync_UnknownSku_ReturnsEmpty`, `AppendRangeAsync_DifferentSkus_AreIsolated`,
        `AppendRangeAsync_EmptyList_IsANoOp`

### Choreography and orchestration — inject the event store

- [x] 2. `InventoryParticipant` appends newly-produced events after each `Handle()` call
      - ⚠ Retro: task's file list only named `InventoryParticipantTests.cs`, but this constructor change has two more call sites the task didn't anticipate: `tests/OrderSaga.Choreography.Tests/OrderSagaChoreographyIntegrationTests.cs` (its own `WireSaga` helper constructs `InventoryParticipant` directly) and the Host-layer `HostParticipantWiring.Wire(...)`/`HostParticipantWiringTests.cs`/`Program.cs`, none of which are due for their *real* persistence wiring until task 12. Rather than leave the build red until task 12 (against this plan's own rule), `HostParticipantWiring.Wire` gained an `IInventoryEventStore` parameter now, and `Program.cs` passes a placeholder `InMemoryInventoryEventStore()` with a comment noting task 12 replaces it with the real Mongo-backed store. Expect the identical gap on task 3 (`InventoryResponder`'s own Host-layer wiring in `OrderSaga.Orchestration.InventoryHost`).
      - File(s): `src/OrderSaga.Choreography/InventoryParticipant.cs` — new constructor parameter
        `IInventoryEventStore eventStore`; `PublishNewEvents` also calls
        `eventStore.AppendRangeAsync(...).GetAwaiter().GetResult()` for the same slice it publishes
      - Test: `tests/OrderSaga.Choreography.Tests/InventoryParticipantTests.cs` — existing tests
        updated to pass an `InMemoryInventoryEventStore`; new:
        `OnOrderPlaced_SuccessfulReservation_AppendsStockReservedToEventStore`,
        `OnOrderPlaced_InsufficientStock_AppendsNothing`

- [x] 3. `InventoryResponder` gets the same injected event store and append-after-`Handle()` pattern
      - ⚠ Retro: predicted gap from task 2's retro confirmed exactly — `InventoryWiring.Wire` (`src/OrderSaga.Orchestration.InventoryHost/InventoryWiring.cs`), its test (`InventoryWiringTests.cs`), and `Program.cs` all needed the same placeholder-`InMemoryInventoryEventStore` treatment to keep the build green ahead of task 13's real wiring. Two-for-two on this exact class of gap now (choreography's Host, orchestration's InventoryHost) — worth explicitly checking CoordinatorHost's own wiring for the equivalent when task 5/14's `ISagaStateStore` change lands, rather than being surprised a third time.
      - File(s): `src/OrderSaga.Orchestration/InventoryResponder.cs` — same shape as task 2
      - Test: `tests/OrderSaga.Orchestration.Tests/InventoryResponderTests.cs` — existing tests
        updated to pass an `InMemoryInventoryEventStore`; new:
        `OnReserveStockCommand_SuccessfulReservation_AppendsStockReservedToEventStore`. Also update
        `tests/OrderSaga.Orchestration.Tests/OrderSagaOrchestrationIntegrationTests.cs`'s
        `InventoryResponder` construction to match.

### `OrderSaga.Orchestration` — SagaState persistence interface (pure, no new dependencies)

- [x] 4. `ISagaStateStore` interface + in-memory fake
      - File(s): `src/OrderSaga.Orchestration/ISagaStateStore.cs` — `Task SaveAsync(SagaState state,
        CancellationToken)`, `Task<IReadOnlyList<SagaState>> LoadAllAsync(CancellationToken)`;
        `src/OrderSaga.Orchestration/InMemorySagaStateStore.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/InMemorySagaStateStoreTests.cs` —
        `SaveAsync_ThenLoadAllAsync_ReturnsSavedState`,
        `SaveAsync_SameOrderIdTwice_OverwritesPreviousVersion`,
        `LoadAllAsync_NothingSaved_ReturnsEmpty`

- [x] 5. `SagaCoordinator` gains an injected `ISagaStateStore`: rehydrates `_sagas` from
      `LoadAllAsync()` at construction, and a new private `SetSaga` helper (replacing the seven
      near-identical `_sagas[x] = y;` call sites) calls `SaveAsync` on every transition
      - ⚠ Retro: same build-green requirement as tasks 2/3 hit `CoordinatorWiring.cs`/`CoordinatorWiringTests.cs`/`Program.cs` — but this one *was* already correctly named in task 14's own file list, so it's not a fresh planning gap, just the same "domain-layer task breaks the build until a much-later Host-wiring task fixes it for real" ordering tension appearing a third time. Fixed the same way: placeholder `InMemorySagaStateStore()` in `Program.cs` with a comment noting task 14 replaces it.
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` — existing tests updated
        to pass an `InMemorySagaStateStore`; new: `Constructor_GivenPersistedSagaState_RehydratesInMemoryState`,
        `OnOrderPlaced_NewOrder_PersistsInitialState`. Also update
        `tests/OrderSaga.Orchestration.Tests/OrderSagaOrchestrationIntegrationTests.cs`'s
        `SagaCoordinator` construction to match.

### Real Mongo/S3-backed implementations (`src/Saga.Persistence`)

- [x] 6. Scaffold the project and test project
      - File(s): `src/Saga.Persistence/Saga.Persistence.csproj` (references `Inventory.Domain`,
        `OrderSaga.Orchestration`; packages `MongoDB.Driver`, `AWSSDK.S3`),
        `tests/Saga.Persistence.Tests/Saga.Persistence.Tests.csproj`, added to `InventoryEngine.slnx`
      - Test: `tests/Saga.Persistence.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 7. Real Mongo-backed `IInventoryEventStore` (config-driven connection string, database,
      collection name)
      - File(s): `src/Saga.Persistence/MongoInventoryEventStore.cs`
      - Verification: compiles against `MongoDB.Driver.IMongoClient`; real append/replay behavior
        verified against the real Atlas cluster (task 21), not a unit test — same precedent as this
        project's DynamoDB-backed stores.

- [x] 8. Real Mongo-backed `ISagaStateStore` (config-driven connection string, database, collection
      name; `SaveAsync` upserts by `OrderId`)
      - File(s): `src/Saga.Persistence/MongoSagaStateStore.cs`
      - Verification: compiles against `IMongoClient`; real behavior verified against the real
        Atlas cluster (task 21).

- [x] 9. S3 archive writer abstraction + real implementation
      - File(s): `src/Saga.Persistence/IEventArchiveWriter.cs` — `Task PutAsync(string key, string
        payload, CancellationToken)`; `src/Saga.Persistence/S3EventArchiveWriter.cs`
      - Verification: compiles against `IAmazonS3`; real behavior verified against LocalStack
        (task 20) — S3 is the one piece of this spec LocalStack can actually emulate.

- [x] 10. S3-dual-writing decorator for `IInventoryEventStore` (inner Mongo write must succeed; the
      archive write is best-effort — logged and swallowed on failure, never rethrown)
      - ⚠ Retro: task's file list didn't anticipate needing an `ILogger` dependency, but "logged and swallowed" in the task's own description implies one — added `ILogger<S3ArchivingInventoryEventStore>` as a third constructor parameter (new `Microsoft.Extensions.Logging.Abstractions` package reference on `Saga.Persistence.csproj`), tests use `NullLogger<T>.Instance` (a real null-object utility from that same package, not a mock).
      - File(s): `src/Saga.Persistence/S3ArchivingInventoryEventStore.cs`
      - Test: `tests/Saga.Persistence.Tests/S3ArchivingInventoryEventStoreTests.cs` —
        `AppendRangeAsync_InnerStoreSucceeds_AlsoWritesToArchive`,
        `AppendRangeAsync_ArchiveWriterThrows_StillSucceeds` (fake inner store + a throwing fake
        `IEventArchiveWriter`), `AppendRangeAsync_InnerStoreThrows_ArchiveNeverCalledAndExceptionPropagates`

- [x] 11. S3-dual-writing decorator for `ISagaStateStore` (same shape as task 10)
      - File(s): `src/Saga.Persistence/S3ArchivingSagaStateStore.cs`
      - Test: `tests/Saga.Persistence.Tests/S3ArchivingSagaStateStoreTests.cs` —
        `SaveAsync_InnerStoreSucceeds_AlsoWritesToArchive`,
        `SaveAsync_ArchiveWriterThrows_StillSucceeds`,
        `SaveAsync_InnerStoreThrows_ArchiveNeverCalledAndExceptionPropagates`

### Host wiring

- [x] 12. Choreography Host: load-or-seed startup logic, real persistence DI
      - File(s): `src/OrderSaga.Choreography.Host/Program.cs` — on startup, `LoadEventsAsync("SKU-1")`;
        if non-empty, `InventoryItem.LoadFromHistory(...)`; else `InventoryItem.Seed(...)` then
        `AppendRangeAsync` its one `StockSeeded` event; wires `S3ArchivingInventoryEventStore`
        (wrapping `MongoInventoryEventStore`, config-driven connection/database/collection/bucket)
        into `InventoryParticipant`'s constructor
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

- [x] 13. Orchestration InventoryHost: same load-or-seed startup logic, real persistence DI
      - ⚠ Retro: needed a new `AwsClientFactory.CreateS3Client` method (`src/OrderSaga.Orchestration.Messaging/AwsClientFactory.cs`, +`AWSSDK.S3` package there) not named in this task's file list — orchestration's three Hosts share LocalStack-override-aware client construction through that factory rather than choreography's inline-per-Host pattern, and task 12 (choreography) had no equivalent shared factory to extend, so this divergence wasn't visible until this task's Host actually needed an S3 client.
      - File(s): `src/OrderSaga.Orchestration.InventoryHost/Program.cs` — same shape as task 12,
        wired into `InventoryResponder`'s constructor
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

- [x] 14. Orchestration CoordinatorHost: `CoordinatorWiring` and `Program.cs` wired for persisted
      `SagaState`
      - File(s): `src/OrderSaga.Orchestration.CoordinatorHost/CoordinatorWiring.cs` (new
        `ISagaStateStore` parameter, passed through to `new SagaCoordinator(...)`),
        `src/OrderSaga.Orchestration.CoordinatorHost/Program.cs` (wires
        `S3ArchivingSagaStateStore` wrapping `MongoSagaStateStore`)
      - Test: `tests/OrderSaga.Orchestration.CoordinatorHost.Tests/CoordinatorWiringTests.cs` —
        existing test updated to pass an `InMemorySagaStateStore`
      - Verification: `dotnet build` succeeds; full solution test suite still passes unchanged.

### Prerequisite

- [x] 15. MongoDB Atlas account + API key setup (manual, non-code)
      - File(s): `.env` — `MONGODB_ATLAS_PUBLIC_KEY`, `MONGODB_ATLAS_PRIVATE_KEY`,
        `MONGODB_ATLAS_ORG_ID` added (gitignored, same pattern as the LocalStack token and AWS keys)
      - Verification: an Atlas organization and a programmatic API key exist; confirmed for real
        when task 18's `terraform init`/`plan` successfully authenticates against them.

### Terraform (`infra/modules/persistence/`, `infra/`, `infra/orchestration/`)

- [x] 16. New `persistence` module: Atlas cluster (M0) + database user + IP-allowlist network access
      - File(s): `infra/modules/persistence/{main,variables,outputs,versions}.tf` — an M0-capable
        Atlas cluster resource, a database user scoped to the module's `database_name` variable, and
        a project IP access list entry for the caller-supplied NAT gateway Elastic IP
      - Verification: `terraform validate`

- [x] 17. Same module: S3 bucket + bucket policy scoped to the calling stack's task role(s)
      - ⚠ Retro: task's own description said "S3 bucket + bucket policy scoped to the calling stack's task role(s)," but the actual scoping mechanism already exists — the `iam-and-observability` module's caller-supplied `task_policy_statements` list (confirmed by reading its `iam.tf`) is where an `s3:PutObject` statement naming this bucket's ARN belongs, in tasks 18/19. This task just needed to create the bucket and expose its ARN as an output; a bucket *policy* resource here would have duplicated that existing mechanism, not complemented it.
      - File(s): `infra/modules/persistence/s3.tf`
      - Verification: `terraform validate`

- [x] 18. Wire the `persistence` module into choreography's root
      - ⚠ Retro: task's file list didn't anticipate that `module.networking` had no output for the NAT gateway's Elastic IP at all (the `aws_eip.nat` resource existed but was never exposed) — added `output "nat_gateway_ip"` to `infra/modules/networking/outputs.tf` first, since the persistence module's IP-allowlist variable (task 16) needs a real value to bind to, not a placeholder.
      - File(s): `infra/main.tf` (one `persistence` module instance; one `database_name` for
        `InventoryItem`'s events), `infra/variables.tf` (Atlas org ID/API key variables), an added
        `s3:PutObject` statement on the Inventory-holding service's `task_policy_statements`,
        `desired_count` raised from `1` to `2` on that same service
      - Verification: `terraform validate` passes on the complete configuration

- [x] 19. Wire the `persistence` module into orchestration's root
      - File(s): `infra/orchestration/main.tf` (one `persistence` module instance; two
        `database_name`s or two databases within it — `InventoryItem` events and `SagaState`
        snapshots), `infra/orchestration/variables.tf`, added `s3:PutObject` statements on both the
        Coordinator's and the Inventory responder's `task_policy_statements`, `desired_count` raised
        from `1` to `2` on both of those services
      - Verification: `terraform validate` passes on the complete configuration

### Validation

- [x] 20. LocalStack validation (S3 portion only)
      - ⚠ Retro: real findings, both fixed. (1) `mongodbatlas` provider missed from the S3-only apply's targeting concern initially, resolved by using `-target` to scope to just the two bucket resources rather than the whole `persistence` module, avoiding needing real Atlas credentials for an S3-only validation pass. (2) The exact "virtual-hosted vs. path-style" LocalStack S3 defect already fixed once on the C# SDK side (`AmazonS3Config.ForcePathStyle`) recurred identically on the Terraform AWS provider side (`s3_use_path_style`) — same defect *class*, different layer, matching this project's now-familiar pattern of a fix needing to land twice because the two layers don't share code. Fixed in both `infra/versions.tf` and `infra/orchestration/versions.tf` proactively (only hit the error once, in choreography's root).
      - File(s): docs update — extend an existing LocalStack setup doc or add a new one
        (implementation detail)
      - Verification: `terraform apply` against LocalStack succeeds for the `persistence` module's
        S3 resources; `S3EventArchiveWriter`/`S3ArchivingInventoryEventStore`/
        `S3ArchivingSagaStateStore` exercised against LocalStack's S3 emulation directly (a small
        standalone run, not a full Host), confirming the dual-write path's actual AWS SDK usage
        before spending any real Atlas/AWS time on it.

- [x] 21. Real deployment: both Atlas clusters + both S3 buckets, both stacks redeployed with real
      persistence, `desired_count` raised, multi-instance and crash-recovery proven, torn down
      - ⚠ Retro: this task's own description assumed tasks 1–20 already made multi-instance operation correct, and that this task only needed to *prove* it. That assumption was wrong, caught only by actually running two concurrent instances against real AWS — and it was wrong in five distinct, escalating ways, not one:
        1. **Inventory-side concurrency (choreography + orchestration).** `InventoryParticipant`/`InventoryResponder` each held one long-lived in-memory `InventoryItem` per process, loaded once at startup — Mongo persistence made that survive a *restart*, but two *simultaneously running* task instances never re-synced with each other. First real order after `desired_count = 2` threw `KeyNotFoundException` the moment a later command for the same order landed on the other instance. Fixed via `IInventoryEventStore.AppendRangeAsync` gaining an `expectedEventCount` parameter and `ConcurrencyConflictException`, with both participants rewritten to reload-mutate-append-retry per command. Done with the user's explicit go-ahead ("go with option 1, fix it properly") after presenting three options.
        2. **Missing IAM permission (choreography).** `dynamodb:DeleteItem` was never granted to the choreography task role — a pre-existing gap from the original PR #2, never exercised until `SqsMessageProcessor`'s `ReleaseAsync` catch-path was actually hit in a live deployment. The resulting `AccessDenied` masked the real triggering exception and left an idempotency claim stuck, silently no-op'ing redelivery instead of retrying. Fixed in `infra/main.tf`.
        3. **Mongo unique-index contamination.** After fix #1 shipped, redeployment crashed on startup with a duplicate-key error building the new `{Sku, Sequence}` index, because documents written by the *old* pre-fix store had no `Sequence` field at all. Fixed by recreating both Atlas clusters from scratch (`terraform apply -replace`) rather than attempting a live data migration from a sandbox that can't reach Mongo directly (see below).
        4. **The same inventory-side bug, recurring in `SagaCoordinator`.** Found proactively (via code inspection, not a failed test) before it could cause a live failure: `SagaCoordinator` had `desired_count = 2` from task 19 but was never given the equivalent fix. Applied the identical pattern: `SagaState.Version`, a second (deliberately separate) `OrderSaga.Orchestration.ConcurrencyConflictException`, `ISagaStateStore.TryLoadAsync`, and `MongoSagaStateStore`'s unique-index-on-`OrderId` + conditional-upsert redesign.
        5. **A fourth, previously-unnoticed instance of the same bug class, in `PaymentStub` (choreography).** `PaymentStub` cached `OrderPlaced.Amount` in an in-memory `Dictionary<string, decimal>` keyed by `OrderId`, then looked it up on `StockReserved` — under `desired_count = 2`, a `StockReserved` landing on the task instance that never saw the matching `OrderPlaced` threw `KeyNotFoundException`. This component wasn't touched by tasks 1–20 at all, since it has no store of its own — it's a stub standing in for an external payment processor. Fixed (user chose "thread `Amount` through the domain event" over "give the stub its own store") by adding `Amount` to `Inventory.Domain.ReserveStock`/`StockReserved`, making `PaymentStub` fully stateless; `PaymentResponder`/`ShippingResponder` (orchestration's equivalent stubs) were checked and found already stateless, no fix needed there.
        6. **A sixth bug, orthogonal to the concurrency class: `MongoSagaStateStore` crashed the coordinator on every restart.** Unlike `MongoInventoryEventStore` (which nests each event under a `Payload` sub-document and only deserializes that), `MongoSagaStateStore` stores `SagaState` as a flat top-level document and deserialized the *whole* document read back from Mongo — which carries Mongo's own auto-generated `_id` field once written. `SagaState` has no property for `_id`, so `BsonSerializer.Deserialize<SagaState>` threw `FormatException` on every document that had actually round-tripped through the collection. Since `SagaCoordinator`'s constructor loads all sagas synchronously at startup, this crashed the coordinator on every single restart once real `SagaState` data existed — confirmed as a live outage (`runningCount` stuck at 1/2, restarting every ~40s) during this task's own crash-recovery test. Fixed by stripping `_id` before deserializing inside `MongoSagaStateStore` itself, deliberately *not* adding a MongoDB.Bson attribute to `SagaState` (`OrderSaga.Orchestration` has no dependency on MongoDB.Bson, by design — the same reasoning `MongoInventoryEventStore` already followed for `Inventory.Domain`'s events).

        Two systemic causes tie these together: (a) none of tasks 1–20 could exercise real concurrent-instance behavior or real Mongo round-trips, since unit tests use in-memory doubles and LocalStack doesn't run MongoDB (Atlas isn't an AWS service) — this class of bug was structurally invisible until this task's real deployment; (b) this sandbox's MongoDB.Driver TLS handshake fails (`Win32Exception 0x80090304`, an SChannel/LSA restriction specific to this shell, not an Atlas or app-code issue), so every one of these was diagnosed and verified through CloudWatch Logs, SQS queue/DLQ depth, and ECS service health instead of direct Mongo queries — the plan's original verification bullet assumed direct Mongo access that turned out not to be available.
      - File(s): `src/Inventory.Domain/IInventoryEventStore.cs`, `InMemoryInventoryEventStore.cs`, `Commands.cs`, `Events.cs`, `InventoryItem.cs` (expected-version check, `ConcurrencyConflictException`, `Amount` on `ReserveStock`/`StockReserved`); `src/Saga.Persistence/MongoInventoryEventStore.cs` (unique `{Sku, Sequence}` index, duplicate-key → conflict), `MongoSagaStateStore.cs` (unique `OrderId` index, conditional upsert, `_id` stripped before deserialize), `S3ArchivingInventoryEventStore.cs`/`S3ArchivingSagaStateStore.cs` (signature passthrough); `src/OrderSaga.Choreography/InventoryParticipant.cs`, `PaymentStub.cs`; `src/OrderSaga.Orchestration/InventoryResponder.cs`, `SagaState.cs`, `ConcurrencyConflictException.cs`, `ISagaStateStore.cs`, `InMemorySagaStateStore.cs`, `SagaCoordinator.cs`; `infra/main.tf` (`dynamodb:DeleteItem`); plus every test file that previously seeded state by mutating a shared object directly instead of through the event/state store, and new regression tests (`InventoryParticipantTests`/`InventoryResponderTests`/`SagaCoordinatorTests`' race-injecting-store tests, `PaymentStubTests`' stateless-resolution test, `SagaStateBsonSerializationTests`' `_id`-stripping test)
      - Verification: `terraform apply` succeeds for both stacks' `persistence` resources (including
        one `-replace` of both Atlas clusters to clear index-incompatible legacy data); both stacks'
        affected Hosts rebuilt/redeployed (four separate image rebuilds across the session as each
        successive bug was found and fixed); happy path / insufficient stock / payment declined all
        exercised cleanly against both real Atlas clusters with `desired_count = 2`, confirmed via
        CloudWatch Logs (zero warnings/errors in the final clean pass) and SQS queue/DLQ depth (0
        across every queue in both stacks); **a reservation made against one task instance is
        confirmed visible when a different instance handles the next request for the same order/SKU**
        for both the Inventory aggregate (choreography and orchestration) and `SagaState`
        (orchestration), via `stop-task` against a live instance mid-saga followed by confirming the
        replacement instance completes the flow with no errors and the queue drains to 0; torn down
        after, confirmed via direct AWS and Atlas API queries that nothing is left running or accruing cost.
