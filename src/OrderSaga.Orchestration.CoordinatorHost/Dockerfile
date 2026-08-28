# Build context must be the repo root (not this directory), since this project references
# sibling projects outside its own folder:
#   docker build -f src/OrderSaga.Orchestration.CoordinatorHost/Dockerfile -t order-saga-orchestration-coordinator .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OrderSaga.Orchestration.CoordinatorHost/OrderSaga.Orchestration.CoordinatorHost.csproj", "src/OrderSaga.Orchestration.CoordinatorHost/"]
COPY ["src/OrderSaga.Orchestration.Messaging/OrderSaga.Orchestration.Messaging.csproj", "src/OrderSaga.Orchestration.Messaging/"]
COPY ["src/OrderSaga.Orchestration/OrderSaga.Orchestration.csproj", "src/OrderSaga.Orchestration/"]
COPY ["src/OrderSaga.Shared/OrderSaga.Shared.csproj", "src/OrderSaga.Shared/"]
# Inventory.Domain isn't referenced by this Host directly -- it's a transitive dependency of
# OrderSaga.Orchestration (InventoryResponder), needed here purely so that project restores.
COPY ["src/Inventory.Domain/Inventory.Domain.csproj", "src/Inventory.Domain/"]
COPY ["src/OrderSaga.Aws/OrderSaga.Aws.csproj", "src/OrderSaga.Aws/"]
COPY ["src/Saga.Persistence/Saga.Persistence.csproj", "src/Saga.Persistence/"]
RUN dotnet restore "src/OrderSaga.Orchestration.CoordinatorHost/OrderSaga.Orchestration.CoordinatorHost.csproj"

COPY src/OrderSaga.Orchestration.CoordinatorHost/ src/OrderSaga.Orchestration.CoordinatorHost/
COPY src/OrderSaga.Orchestration.Messaging/ src/OrderSaga.Orchestration.Messaging/
COPY src/OrderSaga.Orchestration/ src/OrderSaga.Orchestration/
COPY src/OrderSaga.Shared/ src/OrderSaga.Shared/
COPY src/Inventory.Domain/ src/Inventory.Domain/
COPY src/OrderSaga.Aws/ src/OrderSaga.Aws/
COPY src/Saga.Persistence/ src/Saga.Persistence/

WORKDIR /src/src/OrderSaga.Orchestration.CoordinatorHost
RUN dotnet publish "OrderSaga.Orchestration.CoordinatorHost.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# New Relic .NET agent -- see OrderSaga.Choreography.Host/Dockerfile for why these specific paths
# (confirmed by inspecting a real Linux build's publish output).
ENV CORECLR_ENABLE_PROFILING=1
ENV CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A}
ENV CORECLR_NEWRELIC_HOME=/app/newrelic
ENV CORECLR_PROFILER_PATH=/app/newrelic/libNewRelicProfiler.so

ENTRYPOINT ["dotnet", "OrderSaga.Orchestration.CoordinatorHost.dll"]
