# Inventory Aggregate — Plan

Spec: docs/specs/inventory-aggregate.md

**Scope note:** this plan covers the pure domain layer only — the aggregate, its event log, and
the projection, as an in-memory/replay model with no persistence or messaging infra. That matches
the spec's own scope boundary (saga wiring and infra integration are separate, later specs).

**Design notes made while breaking this down** (implementation latitude, not new scope — flag if
you'd rather go a different way):
- Quantity accounting: `AvailableQuantity = TotalQuantity - ReservedQuantity - DeductedQuantity`.
  `ReserveStock` moves quantity from free → Reserved (available decreases). `ConfirmReservation`
  moves it from Reserved → Deducted (available unchanged, since it was already reduced at reserve
  time). `ReleaseReservation` moves it from Reserved back to free (available increases).
- Domain rule violations (insufficient stock, invalid state transition) throw a domain exception
  rather than returning a `Result<T>` — matches idiomatic .NET DDD default, no existing repo
  convention to follow yet since this is the first project.
- Initial stock quantity is set via an aggregate creation/seed step (`StockSeeded`), since no
  in-scope behavior is testable without an aggregate that has *some* starting quantity. This is
  the minimum viable construction path, not the "replenishment" feature the spec excludes —
  there's no ongoing add-more-stock command.

## Tasks

- [x] 1. Scaffold the solution and projects
      - File(s): `InventoryEngine.sln`, `src/Inventory.Domain/Inventory.Domain.csproj`,
        `tests/Inventory.Domain.Tests/Inventory.Domain.Tests.csproj`
      - Test: `tests/Inventory.Domain.Tests/SmokeTests.cs` — `TestProjectRuns` (trivial sanity
        assertion confirming the test project builds and executes before any real logic exists)

- [ ] 2. Define the command and event record types
      - File(s): `src/Inventory.Domain/Commands.cs` (`ReserveStock`, `ConfirmReservation`,
        `ReleaseReservation`), `src/Inventory.Domain/Events.cs` (`StockSeeded`, `StockReserved`,
        `ReservationConfirmed`, `ReservationReleased`)
      - Test: `tests/Inventory.Domain.Tests/EventsTests.cs` —
        `StockReserved_RecordsSkuOrderIdAndQuantity`

- [ ] 3. Aggregate creation (seed initial stock)
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `Seed_SetsAvailableQuantityToInitialQuantity`,
        `Seed_EmitsStockSeededEvent`

- [ ] 4. Reserve stock — happy path
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ReserveStock_WithSufficientQuantity_EmitsStockReservedAndReducesAvailableQuantity`

- [ ] 5. Reserve stock — rejected when insufficient available quantity
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ReserveStock_ExceedingAvailableQuantity_ThrowsInsufficientStockExceptionAndEmitsNoEvent`

- [ ] 6. Reserve stock — idempotent on duplicate request for the same order+SKU
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ReserveStock_DuplicateForSameOrderAndSku_ReturnsExistingReservationWithoutReducingAvailableAgain`

- [ ] 7. Confirm reservation — happy path
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ConfirmReservation_OnReservedHold_EmitsReservationConfirmedAndMovesQuantityToDeducted`

- [ ] 8. Confirm reservation — rejected if already released
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ConfirmReservation_AlreadyReleased_ThrowsInvalidReservationStateException`

- [ ] 9. Release reservation — happy path
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ReleaseReservation_OnReservedHold_EmitsReservationReleasedAndRestoresAvailableQuantity`

- [ ] 10. Release reservation — rejected if already confirmed
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `ReleaseReservation_AlreadyConfirmed_ThrowsInvalidReservationStateException`

- [ ] 11. Rehydrate the aggregate from its event history
      - File(s): `src/Inventory.Domain/InventoryItem.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryItemTests.cs` —
        `LoadFromHistory_ReplayingAFullLifecycle_ReconstructsTheSameStateAsLiveMethodCalls`

- [ ] 12. Projection: available quantity per SKU
      - File(s): `src/Inventory.Domain/InventoryProjection.cs`
      - Test: `tests/Inventory.Domain.Tests/InventoryProjectionTests.cs` —
        `Apply_SeededThenReserved_ReflectsReducedAvailableQuantity`,
        `Apply_ReservationReleased_RestoresAvailableQuantity`,
        `Apply_ReservationConfirmed_LeavesAvailableQuantityUnchanged`
