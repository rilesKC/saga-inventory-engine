# Build context must be the repo root (not this directory), since this project references
# sibling projects outside its own folder:
#   docker build -f src/OrderSaga.Orchestration.InventoryHost/Dockerfile -t order-saga-orchestration-inventory .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OrderSaga.Orchestration.InventoryHost/OrderSaga.Orchestration.InventoryHost.csproj", "src/OrderSaga.Orchestration.InventoryHost/"]
COPY ["src/OrderSaga.Orchestration.Messaging/OrderSaga.Orchestration.Messaging.csproj", "src/OrderSaga.Orchestration.Messaging/"]
COPY ["src/OrderSaga.Orchestration/OrderSaga.Orchestration.csproj", "src/OrderSaga.Orchestration/"]
COPY ["src/OrderSaga.Shared/OrderSaga.Shared.csproj", "src/OrderSaga.Shared/"]
COPY ["src/Inventory.Domain/Inventory.Domain.csproj", "src/Inventory.Domain/"]
RUN dotnet restore "src/OrderSaga.Orchestration.InventoryHost/OrderSaga.Orchestration.InventoryHost.csproj"

COPY src/OrderSaga.Orchestration.InventoryHost/ src/OrderSaga.Orchestration.InventoryHost/
COPY src/OrderSaga.Orchestration.Messaging/ src/OrderSaga.Orchestration.Messaging/
COPY src/OrderSaga.Orchestration/ src/OrderSaga.Orchestration/
COPY src/OrderSaga.Shared/ src/OrderSaga.Shared/
COPY src/Inventory.Domain/ src/Inventory.Domain/

WORKDIR /src/src/OrderSaga.Orchestration.InventoryHost
RUN dotnet publish "OrderSaga.Orchestration.InventoryHost.csproj" -c Release -o /app/publish --no-restore

# Runtime-only base (not aspnet) -- this Host has no HTTP surface, matching its
# Microsoft.NET.Sdk.Worker shape and the spec's "ECS's own task-health signal" decision. No
# EXPOSE/ASPNETCORE_HTTP_PORTS -- there's no port to listen on.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OrderSaga.Orchestration.InventoryHost.dll"]
