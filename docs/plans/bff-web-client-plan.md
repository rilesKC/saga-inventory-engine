# BFF & Web Client — Plan

Spec: docs/specs/bff-web-client.md

## Design notes made while breaking this down

(implementation latitude, not new scope — flag if you'd rather go a different way)

- Choreography has no `SagaState` (confirmed: nothing in `OrderSaga.Choreography.Host`'s code path
  ever calls `ISagaStateStore.SaveAsync`). Per decision, its status is synthesized from the last
  matching Inventory event for that OrderId (`StockReserved`→"Reserved",
  `ReservationConfirmed`→"Confirmed", `ReservationReleased`→"Released"), so both hosts return the
  same `{orderId, sku, quantity, amount, status, history}` shape for the BFF to normalize.
- There's no OrderId→Sku index anywhere. Adding `LoadEventsForOrderAsync(orderId, ct)` to
  `IInventoryEventStore` — Mongo implementation filters the whole collection on the embedded
  `Payload.OrderId` field (no index needed at this scale) — so choreography can look up an order
  by ID alone, matching the spec's literal wording. Orchestration doesn't need this new method:
  `SagaState` already carries `Sku` directly, so its lookup uses the existing `LoadEventsAsync(sku)`.
- `CoordinatorHost`'s `sagaStateStore` is currently a local variable, not DI-registered. Promoting
  it (and constructing `IInventoryEventStore` the same way, reusing the already-resolved Mongo
  config) to registered singletons so the new lookup handler follows `OrderIntakeHandler`'s
  existing DI-constructor-injection convention.
- New `OrderLookupHandler` classes (one per host) follow `OrderIntakeHandler`'s exact existing
  shape: plain class, constructor-injected dependencies, one method, no ASP.NET types — testable
  directly, same as this repo's only existing precedent (neither test project uses
  `WebApplicationFactory`).
- `OrderDetails` (the response DTO) is duplicated per host rather than pulled into a shared
  project — matching this repo's existing precedent of duplicating `PlaceOrderRequest` verbatim
  across both hosts rather than sharing it.
- No TypeScript test convention exists yet anywhere in this repo. Using **Vitest** for both the
  BFF and the React app (fast, TS-native; also the tool the user's sibling Java/Angular project
  already migrated to) plus **React Testing Library** for component tests. Flag if you'd rather use
  something else.
- BFF framework: **Express** (the most standard, widely-recognized choice — keeps the new surface
  focused on learning the BFF *pattern*, not evaluating framework options). HTTP calls to the two
  .NET hosts use Node's built-in `fetch` — no extra HTTP client dependency needed.
- React scaffold: **Vite** (the current standard; Create React App is deprecated).
- The BFF only ever calls the two hosts' HTTP endpoints — it never touches Mongo/S3/AWS directly —
  so its only local config is each host's base URL (env vars), not Atlas/AWS credentials.
- No `.gitignore` entries exist yet for `node_modules/`/`dist/` since there's no JS/TS in this repo
  today — added as the first task so scaffolding doesn't immediately pollute `git status`.

## Tasks

### Shared persistence — order lookup by ID alone

- [x] 1. Add `.gitignore` entries for `node_modules/`, `dist/`, `bff/.env`, `web/.env`
      - File(s): .gitignore
      - Verification: `git status` shows no untracked noise after `npm install`/`npm run build` in
        both new projects (checked again at the end of tasks 9 and 12)

- [x] 2. Add `LoadEventsForOrderAsync(orderId, ct)` to `IInventoryEventStore` + implement on the
      in-memory fake (filters stored events across all SKUs by matching `OrderId`)
      - ⚠ Retro: this task and task 3 could not actually be independently buildable/committable as
        split — a new `IInventoryEventStore` member must be implemented by every implementer
        simultaneously for the solution to compile in C#, so the solution-wide build stayed red
        between this task and task 3. It also surfaced four more hand-rolled test-double
        implementers (`InventoryParticipantTests.cs`, `InventoryResponderTests.cs`,
        `S3ArchivingInventoryEventStoreTests.cs` ×3) outside either task's stated `File(s)` list
        that needed the new method too. Both tasks were done back-to-back in the same turn to keep
        the red window minimal; a future plan adding a member to an existing multi-implementer
        interface should scope one task per interface-change (all implementers together) rather
        than splitting "add to interface + primary implementation" from "add to remaining
        implementations."
      - File(s): src/Inventory.Domain/IInventoryEventStore.cs, src/Inventory.Domain/InMemoryInventoryEventStore.cs
      - Test: tests/Inventory.Domain.Tests/InMemoryInventoryEventStoreTests.cs —
        `LoadEventsForOrderAsync_NoMatchingEvents_ReturnsEmpty`,
        `LoadEventsForOrderAsync_MatchingEvents_ReturnsOnlyThatOrdersEventsAcrossSkus`

- [x] 3. Implement `LoadEventsForOrderAsync` on `MongoInventoryEventStore` (Mongo filter on the
      embedded `Payload.OrderId` field, no Sku scoping) and pass it through
      `S3ArchivingInventoryEventStore` unchanged
      - File(s): src/Saga.Persistence/MongoInventoryEventStore.cs, src/Saga.Persistence/S3ArchivingInventoryEventStore.cs
      - Verification: real behavior verified against the real Atlas cluster (same precedent as
        this project's other Mongo-backed methods — not unit tested); manually seed events for two
        different SKUs sharing overlapping sequences and confirm the method returns only the
        matching OrderId's events regardless of SKU

### Choreography Host — GET /orders/{id}

- [x] 4. Add `OrderStatusProjection` (pure function: given a list of Inventory events, returns
      "Reserved"/"Confirmed"/"Released", or null if empty)
      - File(s): src/OrderSaga.Choreography.Host/OrderStatusProjection.cs (new)
      - Test: tests/OrderSaga.Choreography.Host.Tests/OrderStatusProjectionTests.cs —
        `Project_NoEvents_ReturnsNull`, `Project_OnlyStockReserved_ReturnsReserved`,
        `Project_ReservedThenConfirmed_ReturnsConfirmed`, `Project_ReservedThenReleased_ReturnsReleased`

- [x] 5. Add `OrderLookupHandler` (calls `LoadEventsForOrderAsync`, returns null if empty, otherwise
      an `OrderDetails` combining the projected status and the event list) + `OrderDetails` record
      - File(s): src/OrderSaga.Choreography.Host/OrderLookupHandler.cs (new), src/OrderSaga.Choreography.Host/OrderDetails.cs (new)
      - Test: tests/OrderSaga.Choreography.Host.Tests/OrderLookupHandlerTests.cs —
        `Handle_UnknownOrderId_ReturnsNull` (seed `InMemoryInventoryEventStore` with a different
        order's events only), `Handle_KnownOrderId_ReturnsDetailsWithHistoryAndStatus`

- [x] 6. Wire `GET /orders/{id}` into `Program.cs` (register `OrderLookupHandler` in DI, add route
      returning 404 when the handler returns null)
      - File(s): src/OrderSaga.Choreography.Host/Program.cs
      - Verification: `dotnet run` (per docs/localstack-setup.md), `curl -X POST /orders` then
        `curl GET /orders/{id}` — confirm the response shape and status; `curl GET /orders/unknown-id`
        returns 404

### Orchestration CoordinatorHost — GET /orders/{id}

- [x] 7. Add `OrderLookupHandler` (calls `ISagaStateStore.TryLoadAsync(orderId)`; returns null if
      not found; otherwise calls the existing `IInventoryEventStore.LoadEventsAsync(state.Sku)`,
      filters to that OrderId, maps `SagaState.Step` to a status string) + `OrderDetails` record
      - File(s): src/OrderSaga.Orchestration.CoordinatorHost/OrderLookupHandler.cs (new), src/OrderSaga.Orchestration.CoordinatorHost/OrderDetails.cs (new)
      - Test: tests/OrderSaga.Orchestration.CoordinatorHost.Tests/OrderLookupHandlerTests.cs —
        `Handle_UnknownOrderId_ReturnsNull` (using `InMemorySagaStateStore` + `InMemoryInventoryEventStore`),
        `Handle_KnownOrderId_ReturnsDetailsWithHistoryAndStatus`

- [x] 8. Promote `sagaStateStore` from a local variable to a registered DI singleton; construct and
      register `IInventoryEventStore` the same way (reusing the already-resolved Mongo config);
      wire `GET /orders/{id}` into `Program.cs`
      - File(s): src/OrderSaga.Orchestration.CoordinatorHost/Program.cs
      - Verification: `dotnet run` (per docs/localstack-setup-orchestration.md) — confirm the
        existing `POST /orders` and coordinator wiring still work unchanged (the promoted
        `sagaStateStore` must still be the same instance passed into `CoordinatorWiring.Wire(...)`);
        `curl GET /orders/{id}` after placing an order returns the expected shape; unknown ID
        returns 404

### BFF (Node/TypeScript, local-only)

- [ ] 9. Scaffold the BFF project (npm, TypeScript, Express) with a health check
      - File(s): bff/package.json, bff/tsconfig.json, bff/src/index.ts (new)
      - Verification: `npm run dev` starts the server; `GET /health` returns 200

- [ ] 10. `POST /orders` — takes `{ stack: "choreography" | "orchestration", ...order fields }`,
      proxies to the corresponding host's `POST /orders`, returns its status/body; 400 for an
      invalid `stack` value
      - File(s): bff/src/config.ts (new — host base URLs from env vars), bff/src/routes/orders.ts (new)
      - Test: bff/tests/orders.route.test.ts (Vitest) — routes to choreography when
        `stack: "choreography"`, routes to orchestration when `stack: "orchestration"`, returns 400
        for an invalid `stack` value (stub the downstream `fetch` call manually, no mocking library)

- [ ] 11. `GET /orders/:id?stack=...` — proxies to the chosen host's new `GET /orders/{id}`,
      passes through 404, normalizes both hosts' responses into one shared shape
      - File(s): bff/src/routes/orders.ts (extend)
      - Test: bff/tests/orders.route.test.ts — proxies to the requested stack and passes through a
        404, normalizes a successful response into the unified shape

### Web (React + TypeScript, local-only)

- [ ] 12. Scaffold the React app (Vite + TypeScript)
      - File(s): web/package.json, web/vite.config.ts, web/src/main.tsx, web/src/App.tsx (new)
      - Verification: `npm run dev` starts the Vite dev server; default page loads in a browser

- [ ] 13. `PlaceOrderForm` component (OrderId, Sku, Quantity, Amount, stack selector) — submits to
      the BFF's `POST /orders`
      - File(s): web/src/components/PlaceOrderForm.tsx (new)
      - Test: web/tests/PlaceOrderForm.test.tsx (Vitest + React Testing Library) — submitting the
        form calls the BFF's `POST /orders` with the entered values

- [ ] 14. `OrderLookup` component (OrderId input + stack selector) — calls the BFF's
      `GET /orders/:id`, renders status + history, shows a not-found message on 404
      - File(s): web/src/components/OrderLookup.tsx (new)
      - Test: web/tests/OrderLookup.test.tsx — looking up a known order renders its status and
        history; looking up an unknown order renders a not-found message

- [ ] 15. Wire both components into `App.tsx`; manual end-to-end pass
      - File(s): web/src/App.tsx
      - Verification: run both .NET hosts locally (per docs/localstack-setup*.md), the BFF, and the
        React dev server together; place an order through each stack via the UI, look up each by
        ID, confirm history/status render correctly; confirm a nonexistent ID shows not-found
