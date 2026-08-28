# Build context must be the repo root (not this directory), since this project references
# sibling projects outside its own folder:
#   docker build -f src/OrderSaga.Choreography.Host/Dockerfile -t order-saga-choreography .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OrderSaga.Choreography.Host/OrderSaga.Choreography.Host.csproj", "src/OrderSaga.Choreography.Host/"]
COPY ["src/OrderSaga.Choreography/OrderSaga.Choreography.csproj", "src/OrderSaga.Choreography/"]
COPY ["src/OrderSaga.Shared/OrderSaga.Shared.csproj", "src/OrderSaga.Shared/"]
COPY ["src/Inventory.Domain/Inventory.Domain.csproj", "src/Inventory.Domain/"]
COPY ["src/OrderSaga.Aws/OrderSaga.Aws.csproj", "src/OrderSaga.Aws/"]
COPY ["src/Saga.Persistence/Saga.Persistence.csproj", "src/Saga.Persistence/"]
COPY ["src/OrderSaga.Orchestration/OrderSaga.Orchestration.csproj", "src/OrderSaga.Orchestration/"]
RUN dotnet restore "src/OrderSaga.Choreography.Host/OrderSaga.Choreography.Host.csproj"

COPY src/OrderSaga.Choreography.Host/ src/OrderSaga.Choreography.Host/
COPY src/OrderSaga.Choreography/ src/OrderSaga.Choreography/
COPY src/OrderSaga.Shared/ src/OrderSaga.Shared/
COPY src/Inventory.Domain/ src/Inventory.Domain/
COPY src/OrderSaga.Aws/ src/OrderSaga.Aws/
COPY src/Saga.Persistence/ src/Saga.Persistence/
COPY src/OrderSaga.Orchestration/ src/OrderSaga.Orchestration/

WORKDIR /src/src/OrderSaga.Choreography.Host
RUN dotnet publish "OrderSaga.Choreography.Host.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# New Relic .NET agent (NewRelic.Agent NuGet package -- content files land at /app/newrelic in
# the publish output, confirmed by inspecting a real Linux build; the package ships no compile-
# time lib, only these runtime content files + a CoreCLR profiler). NEW_RELIC_LICENSE_KEY and
# NEW_RELIC_APP_NAME come from the container's runtime environment (ECS task definition), not
# baked in here -- same pattern as Mongo__ConnectionString.
ENV CORECLR_ENABLE_PROFILING=1
ENV CORECLR_PROFILER={36032161-FFC0-4B61-B559-F6C5D41BAE5A}
ENV CORECLR_NEWRELIC_HOME=/app/newrelic
ENV CORECLR_PROFILER_PATH=/app/newrelic/libNewRelicProfiler.so

ENTRYPOINT ["dotnet", "OrderSaga.Choreography.Host.dll"]
