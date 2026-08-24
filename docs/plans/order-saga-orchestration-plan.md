# Order Saga — Orchestration — Plan

Spec: docs/specs/order-saga-orchestration.md

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):
- Responders (`InventoryResponder`/`PaymentResponder`/`ShippingResponder`) publish their own
  dedicated `Reply` records directly, rather than forwarding `Inventory.Domain`'s own events the
  way choreography's `InventoryParticipant` did (the event-count-snapshot trick). A reply only
  needs to tell the coordinator "done" or "failed" — it doesn't need to carry the full domain
  event, so there's no need to reuse that forwarding mechanism here.
- `SagaState` is an immutable record (`OrderId`, `Sku`, `Quantity`, `Amount`, `Step`), updated via
  `with` expressions in a `Dictionary<string, SagaState>` keyed by `OrderId` — same style as
  `InventoryItem`'s own `ReservationRecord`.
- `SagaCoordinator` exposes a minimal `GetStep(orderId)` accessor so tests can assert on saga
  progress without over-exposing internals — same pattern as `InventoryItem`'s public state
  properties.

## Tasks

- [x] 1. Extract `EventBus` into a new shared project
      - File(s): `src/OrderSaga.Shared/OrderSaga.Shared.csproj` (new),
        `src/OrderSaga.Shared/EventBus.cs` (moved from `OrderSaga.Choreography`, namespace changed
        to `OrderSaga.Shared`), `tests/OrderSaga.Shared.Tests/OrderSaga.Shared.Tests.csproj` (new),
        `tests/OrderSaga.Shared.Tests/EventBusTests.cs` (moved from
        `OrderSaga.Choreography.Tests`). `OrderSaga.Choreography.csproj`'s project reference
        updated to `OrderSaga.Shared`; every file that referenced `EventBus` there gets a
        `using OrderSaga.Shared;` in place of the implicit same-namespace access. Added to
        `InventoryEngine.slnx`.
      - Test: `tests/OrderSaga.Shared.Tests/EventBusTests.cs` — the three existing tests, now
        relocated: `Publish_InvokesSubscribedHandlerWithTheEvent`,
        `Publish_WithNoSubscribers_DoesNothing`,
        `Publish_NestedPublishDuringDispatch_ProcessesBreadthFirst`. Full solution test suite (all
        30 existing tests) must stay green after the move.

- [x] 2. Scaffold the orchestration project and test project
      - File(s): `src/OrderSaga.Orchestration/OrderSaga.Orchestration.csproj` (references
        `Inventory.Domain` and `OrderSaga.Shared`),
        `tests/OrderSaga.Orchestration.Tests/OrderSaga.Orchestration.Tests.csproj`, added to
        `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Orchestration.Tests/SmokeTests.cs` — `TestProjectRuns`

- [x] 3. Command and reply types
      - File(s): `src/OrderSaga.Orchestration/Commands.cs` (`ReserveStockCommand`,
        `ConfirmReservationCommand`, `ReleaseReservationCommand`, `ChargePaymentCommand`,
        `ScheduleShipmentCommand`), `src/OrderSaga.Orchestration/Replies.cs`
        (`StockReservedReply`, `StockReservationFailedReply`, `ReservationConfirmedReply`,
        `ReservationReleasedReply`, `PaymentChargedReply`, `PaymentDeclinedReply`,
        `ShipmentScheduledReply`)
      - Test: `tests/OrderSaga.Orchestration.Tests/CommandsAndRepliesTests.cs` —
        `ReserveStockCommand_RecordsOrderIdSkuAndQuantity`

- [x] 4. Inventory responder — reserve (happy path + insufficient stock)
      - File(s): `src/OrderSaga.Orchestration/InventoryResponder.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/InventoryResponderTests.cs` —
        `OnReserveStockCommand_WithSufficientStock_PublishesStockReservedReply`,
        `OnReserveStockCommand_WithInsufficientStock_PublishesStockReservationFailedReply`

- [x] 5. Inventory responder — confirm on ConfirmReservationCommand
      - File(s): `src/OrderSaga.Orchestration/InventoryResponder.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/InventoryResponderTests.cs` —
        `OnConfirmReservationCommand_ConfirmsReservationAndPublishesReservationConfirmedReply`

- [x] 6. Inventory responder — release on ReleaseReservationCommand
      - File(s): `src/OrderSaga.Orchestration/InventoryResponder.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/InventoryResponderTests.cs` —
        `OnReleaseReservationCommand_ReleasesReservationAndPublishesReservationReleasedReply`

- [x] 7. Payment responder — threshold rule
      - File(s): `src/OrderSaga.Orchestration/PaymentResponder.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/PaymentResponderTests.cs` —
        `OnChargePaymentCommand_AmountAtOrBelowThreshold_PublishesPaymentChargedReply`,
        `OnChargePaymentCommand_AmountAboveThreshold_PublishesPaymentDeclinedReply`

- [x] 8. Shipping responder — always succeeds
      - File(s): `src/OrderSaga.Orchestration/ShippingResponder.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/ShippingResponderTests.cs` —
        `OnScheduleShipmentCommand_PublishesShipmentScheduledReply`

- [x] 9. Saga coordinator — OrderPlaced entry point
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` —
        `OnOrderPlaced_CreatesSagaStateAndPublishesReserveStockCommand`
      - ⚠ Retro: the spec said the coordinator reacts to "the same trigger as choreography," but
        `OrderPlaced` was defined inside `OrderSaga.Choreography`, which `OrderSaga.Orchestration`
        can't reference without breaking the independence the `EventBus` extraction (task 1) was
        for. Moved `OrderPlaced` into `OrderSaga.Shared` too, same reasoning as task 1 — it's
        genuinely shared trigger data, not implementation-specific. Both plans should have named
        `OrderPlaced` explicitly as shared infrastructure when task 1 moved `EventBus`, not just
        discovered it here.

- [x] 10. Saga coordinator — reserve replies
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` —
        `OnStockReservedReply_PublishesChargePaymentCommandWithSagaAmount`,
        `OnStockReservationFailedReply_MarksSagaFailed`

- [x] 11. Saga coordinator — payment replies
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` —
        `OnPaymentChargedReply_PublishesConfirmReservationCommand`,
        `OnPaymentDeclinedReply_PublishesReleaseReservationCommand`

- [x] 12. Saga coordinator — confirm/release replies
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` —
        `OnReservationConfirmedReply_PublishesScheduleShipmentCommand`,
        `OnReservationReleasedReply_MarksSagaCompensated`

- [x] 13. Saga coordinator — shipment reply (terminal, success)
      - File(s): `src/OrderSaga.Orchestration/SagaCoordinator.cs`
      - Test: `tests/OrderSaga.Orchestration.Tests/SagaCoordinatorTests.cs` —
        `OnShipmentScheduledReply_MarksSagaCompleted`

- [x] 14. Integration: full happy path
      - File(s): none new (wires tasks 3–13 together in the test)
      - Test: `tests/OrderSaga.Orchestration.Tests/OrderSagaOrchestrationIntegrationTests.cs` —
        `OrderPlaced_HappyPath_EndsWithSagaCompletedAndReservationConfirmed`
      - Note (not a Retro flag — no signal fired, but worth recording): passed first try, unlike
        choreography's equivalent test. The shared `EventBus`'s breadth-first fix (made during
        choreography) already protects this, and orchestration's linear command→reply chain never
        has two different participants both reacting to the same trigger the way choreography's
        `InventoryParticipant` and `PaymentStub` both reacted to `OrderPlaced` — a real structural
        difference between the two coordination styles' exposure to that class of bug.

- [ ] 15. Integration: insufficient-stock compensation path
      - File(s): none new
      - Test: `tests/OrderSaga.Orchestration.Tests/OrderSagaOrchestrationIntegrationTests.cs` —
        `OrderPlaced_InsufficientStock_MarksSagaFailedAndNeverReachesPaymentOrShipping`

- [ ] 16. Integration: payment-declined compensation path
      - File(s): none new
      - Test: `tests/OrderSaga.Orchestration.Tests/OrderSagaOrchestrationIntegrationTests.cs` —
        `OrderPlaced_PaymentDeclined_MarksSagaCompensatedAndNeverReachesShipping`
