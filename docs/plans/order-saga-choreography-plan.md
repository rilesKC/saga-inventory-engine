# Order Saga — Choreography — Plan

Spec: docs/specs/order-saga-choreography.md

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):
- `EventBus.Publish(object @event)` is non-generic, dispatching by `@event.GetType()` — the same
  runtime-type-switch style already used in `InventoryItem`/`InventoryProjection`, and it lets the
  bus forward Inventory.Domain's own events (`StockReserved`, etc.) without needing them to
  implement a shared marker interface. `Subscribe<TEvent>` stays generic for a typed call site.
- `InventoryItem.UncommittedEvents` accumulates for the aggregate's whole lifetime, not just the
  latest `Handle` call — `InventoryParticipant` snapshots `UncommittedEvents.Count` before calling
  `Handle`, then only publishes events at/after that index. No changes needed to
  `Inventory.Domain` itself.
- `InventoryParticipant` looks up which `InventoryItem` to act on via a `Dictionary<string,
  InventoryItem>` keyed by SKU (populated externally, e.g. by a test) — the natural minimal lookup
  given events carry `Sku`, not extra generality beyond the spec's single-line-item scope.
- Payment stub's threshold: `Amount > 500m` declines, otherwise charges.

## Tasks

- [ ] 1. Scaffold the project and test project
      - File(s): `src/OrderSaga.Choreography/OrderSaga.Choreography.csproj`,
        `tests/OrderSaga.Choreography.Tests/OrderSaga.Choreography.Tests.csproj` (references
        `Inventory.Domain`), added to `InventoryEngine.slnx`
      - Test: `tests/OrderSaga.Choreography.Tests/SmokeTests.cs` — `TestProjectRuns`

- [ ] 2. Event bus: publish/subscribe roundtrip
      - File(s): `src/OrderSaga.Choreography/EventBus.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/EventBusTests.cs` —
        `Publish_InvokesSubscribedHandlerWithTheEvent`,
        `Publish_WithNoSubscribers_DoesNothing`

- [ ] 3. Saga-level trigger and notification event types
      - File(s): `src/OrderSaga.Choreography/SagaEvents.cs` (`OrderPlaced`,
        `StockReservationFailed`, `PaymentCharged`, `PaymentDeclined`, `ShipmentScheduled`)
      - Test: `tests/OrderSaga.Choreography.Tests/SagaEventsTests.cs` —
        `OrderPlaced_RecordsOrderIdSkuQuantityAndAmount`

- [ ] 4. Inventory participant — reserve on OrderPlaced (happy path)
      - File(s): `src/OrderSaga.Choreography/InventoryParticipant.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/InventoryParticipantTests.cs` —
        `OnOrderPlaced_WithSufficientStock_PublishesStockReserved`

- [ ] 5. Inventory participant — insufficient stock publishes a failure notification
      - File(s): `src/OrderSaga.Choreography/InventoryParticipant.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/InventoryParticipantTests.cs` —
        `OnOrderPlaced_WithInsufficientStock_PublishesStockReservationFailed`

- [ ] 6. Inventory participant — confirm on PaymentCharged
      - File(s): `src/OrderSaga.Choreography/InventoryParticipant.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/InventoryParticipantTests.cs` —
        `OnPaymentCharged_ConfirmsReservationAndPublishesReservationConfirmed`

- [ ] 7. Inventory participant — release on PaymentDeclined
      - File(s): `src/OrderSaga.Choreography/InventoryParticipant.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/InventoryParticipantTests.cs` —
        `OnPaymentDeclined_ReleasesReservationAndPublishesReservationReleased`

- [ ] 8. Payment stub — threshold rule
      - File(s): `src/OrderSaga.Choreography/PaymentStub.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/PaymentStubTests.cs` —
        `OnStockReserved_AmountAtOrBelowThreshold_PublishesPaymentCharged`,
        `OnStockReserved_AmountAboveThreshold_PublishesPaymentDeclined`

- [ ] 9. Shipping stub — always succeeds
      - File(s): `src/OrderSaga.Choreography/ShippingStub.cs`
      - Test: `tests/OrderSaga.Choreography.Tests/ShippingStubTests.cs` —
        `OnReservationConfirmed_PublishesShipmentScheduled`

- [ ] 10. Integration: full happy path
      - File(s): none new (wires tasks 2–9 together in the test)
      - Test: `tests/OrderSaga.Choreography.Tests/OrderSagaChoreographyIntegrationTests.cs` —
        `OrderPlaced_HappyPath_EndsWithShipmentScheduledAndReservationConfirmed`

- [ ] 11. Integration: insufficient-stock compensation path
      - File(s): none new
      - Test: `tests/OrderSaga.Choreography.Tests/OrderSagaChoreographyIntegrationTests.cs` —
        `OrderPlaced_InsufficientStock_PublishesFailureAndNeverReachesPaymentOrShipping`

- [ ] 12. Integration: payment-declined compensation path
      - File(s): none new
      - Test: `tests/OrderSaga.Choreography.Tests/OrderSagaChoreographyIntegrationTests.cs` —
        `OrderPlaced_PaymentDeclined_ReleasesReservationAndNeverReachesShipping`
