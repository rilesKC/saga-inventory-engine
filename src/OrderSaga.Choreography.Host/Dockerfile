# Build context must be the repo root (not this directory), since this project references
# sibling projects outside its own folder:
#   docker build -f src/OrderSaga.Choreography.Host/Dockerfile -t order-saga-choreography .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OrderSaga.Choreography.Host/OrderSaga.Choreography.Host.csproj", "src/OrderSaga.Choreography.Host/"]
COPY ["src/OrderSaga.Choreography/OrderSaga.Choreography.csproj", "src/OrderSaga.Choreography/"]
COPY ["src/OrderSaga.Shared/OrderSaga.Shared.csproj", "src/OrderSaga.Shared/"]
COPY ["src/Inventory.Domain/Inventory.Domain.csproj", "src/Inventory.Domain/"]
RUN dotnet restore "src/OrderSaga.Choreography.Host/OrderSaga.Choreography.Host.csproj"

COPY src/OrderSaga.Choreography.Host/ src/OrderSaga.Choreography.Host/
COPY src/OrderSaga.Choreography/ src/OrderSaga.Choreography/
COPY src/OrderSaga.Shared/ src/OrderSaga.Shared/
COPY src/Inventory.Domain/ src/Inventory.Domain/

WORKDIR /src/src/OrderSaga.Choreography.Host
RUN dotnet publish "OrderSaga.Choreography.Host.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "OrderSaga.Choreography.Host.dll"]
