# LocalStack Setup — Orchestration

Validates the `orchestration-messaging` and `idempotency` Terraform modules and all three Hosts'
real AWS SDK integration code (SQS send/poll, DynamoDB claim) against LocalStack — not the
VPC/ALB/ECS/ECR layer, which LocalStack's free Community edition doesn't emulate. Same scope
reasoning as choreography's setup guide (`docs/localstack-setup.md`).

## Prerequisites

Same as choreography's guide: Docker, and a free LocalStack account + auth token in the repo
root's gitignored `.env`.

## Running LocalStack

```bash
docker compose -f docker-compose.localstack.yml up -d
curl -s http://localhost:4566/_localstack/health
```

Confirm `sqs`, `dynamodb`, `iam`, and `sts` all show `"available"` (no `events` needed here — this
deployment doesn't use EventBridge at all).

## Applying Terraform against it

From `infra/orchestration/`:

```bash
terraform init
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test terraform apply -auto-approve \
  -target=module.orchestration_messaging -target=module.idempotency \
  -var="localstack_endpoint=http://localhost:4566"
```

Note the four outputs — `coordinator_inbound_queue_url`, `inventory_commands_queue_url`,
`stateless_responder_commands_queue_url`, `idempotency_table_name` — you'll need all four for the
three Host apps below.

## Running the Host apps against it

Not containerized, not through ECS — three separate `dotnet run` processes, each pointed at
LocalStack. All three need `AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=us-east-1
Aws__ServiceUrl=http://localhost:4566 Dynamo__IdempotencyTableName=<idempotency_table_name output>`
plus their own queue URLs. CoordinatorHost and InventoryHost also unconditionally require Mongo/S3
config at startup (they throw before ever binding/listening if it's missing) — see "Running
against a real Mongo Atlas cluster too" below for where these values come from. ResponderHost is
the only one of the three with no Mongo/S3 dependency at all:

```bash
# Terminal 1 -- CoordinatorHost (the only one with an HTTP port)
cd src/OrderSaga.Orchestration.CoordinatorHost
ASPNETCORE_URLS=http://localhost:5100 \
  Sqs__InventoryCommandsQueueUrl="<inventory_commands_queue_url>" \
  Sqs__StatelessResponderCommandsQueueUrl="<stateless_responder_commands_queue_url>" \
  Sqs__CoordinatorInboundQueueUrl="<coordinator_inbound_queue_url>" \
  Mongo__ConnectionString="<connection string, see below>" \
  Mongo__DatabaseName="orchestration" \
  Mongo__SagaStateCollectionName="saga-state" \
  Mongo__InventoryEventsCollectionName="inventory-events" \
  S3__ArchiveBucketName="order-saga-orchestration-event-archive" \
  dotnet run

# Terminal 2 -- InventoryHost
cd src/OrderSaga.Orchestration.InventoryHost
Sqs__CoordinatorInboundQueueUrl="<coordinator_inbound_queue_url>" \
  Sqs__InventoryCommandsQueueUrl="<inventory_commands_queue_url>" \
  Mongo__ConnectionString="<connection string, see below>" \
  Mongo__DatabaseName="orchestration" \
  Mongo__InventoryEventsCollectionName="inventory-events" \
  S3__ArchiveBucketName="order-saga-orchestration-event-archive" \
  dotnet run

# Terminal 3 -- ResponderHost
cd src/OrderSaga.Orchestration.ResponderHost
Sqs__CoordinatorInboundQueueUrl="<coordinator_inbound_queue_url>" \
  Sqs__StatelessResponderCommandsQueueUrl="<stateless_responder_commands_queue_url>" \
  dotnet run
```

Then exercise the saga via the Coordinator's HTTP endpoint:

```bash
curl -X POST http://localhost:5100/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORDER-1","sku":"SKU-1","quantity":4,"amount":199.99}'
```

And look up what it did (added alongside the BFF/web client — see
`docs/specs/bff-web-client.md`):

```bash
curl http://localhost:5100/orders/ORDER-1
```

A healthy run returns `status` in the same vocabulary Choreography uses (`"Reserved"`,
`"Confirmed"`, `"Released"` — derived from Inventory event history, not the raw `SagaStep`), a
`sagaStep` field with the richer orchestration-only detail (`ReservingStock`, `AwaitingPayment`,
`Confirming`, `SchedulingShipment`, `Completed`, `Compensating`, `Compensated`, `Failed`), and a
`history` array of the events that got there.

## Verifying it actually worked

Same approach as choreography's guide — direct SQS/DynamoDB query API calls over plain `curl`, no
AWS CLI needed. Check each of the three queues' `ApproximateNumberOfMessages` (all settle back to
0 once every service catches up) and scan the idempotency table for claim counts.

A healthy run produces, per saga path (message counts differ from choreography's because
orchestration's command/reply pattern means more hops per step):

- **Happy path**: 9 claims (`OrderPlaced`, `ReserveStockCommand`, `StockReservedReply`,
  `ChargePaymentCommand`, `PaymentChargedReply`, `ConfirmReservationCommand`,
  `ReservationConfirmedReply`, `ScheduleShipmentCommand`, `ShipmentScheduledReply`).
- **Insufficient stock**: 3 claims (`OrderPlaced`, `ReserveStockCommand`,
  `StockReservationFailedReply`) — the saga ends there, nothing to compensate.
- **Payment declined**: 7 claims (`OrderPlaced`, `ReserveStockCommand`, `StockReservedReply`,
  `ChargePaymentCommand`, `PaymentDeclinedReply`, `ReleaseReservationCommand`,
  `ReservationReleasedReply`).

## S3 event archive (standalone, not through the full Hosts)

Same pattern as choreography's guide, against `order-saga-orchestration-event-archive` (one shared
bucket for both the Coordinator's `SagaState` and the Inventory responder's `InventoryItem`
events, per `docs/specs/saga-persistence.md`). Apply just the bucket, then exercise
`S3EventArchiveWriter` directly via a small standalone `dotnet run` and confirm via `curl`, same as
choreography's guide.

## Running against a real Mongo Atlas cluster too

Same reasoning as choreography's guide — MongoDB Atlas isn't an AWS service, so LocalStack can't
emulate it, but CoordinatorHost and InventoryHost both require it unconditionally at startup.
Genuinely exercisable locally, confirmed working 2026-09-10, with a temporary real (free-tier)
cluster alongside LocalStack:

1. Get your public IP (`curl -s https://api.ipify.org`).
2. Temporarily edit **`infra/orchestration/main.tf`**'s `module "persistence"` block — replace
   `nat_gateway_ip = module.networking.nat_gateway_ip` with your IP as a literal string. Revert
   before committing.
3. Apply just the Atlas resources:
   ```bash
   terraform apply -auto-approve \
     -target=module.persistence.mongodbatlas_project.this \
     -target=module.persistence.mongodbatlas_project_ip_access_list.nat_gateway \
     -target=module.persistence.mongodbatlas_cluster.this \
     -target=module.persistence.random_password.database_user \
     -target=module.persistence.mongodbatlas_database_user.this \
     -var="atlas_org_id=$MONGODB_ATLAS_ORG_ID"
   ```
4. Pull the connection string from state (no root output exposes it):
   ```bash
   $state = terraform state pull | ConvertFrom-Json
   $pw = ($state.resources | Where-Object { $_.type -eq "random_password" -and $_.name -eq "database_user" }).instances[0].attributes.result
   $srv = ($state.resources | Where-Object { $_.type -eq "mongodbatlas_cluster" }).instances[0].attributes.connection_strings[0].standard_srv
   # Connection string: replace "mongodb+srv://" with "mongodb+srv://app:$pw@"
   ```
5. Use it as `Mongo__ConnectionString` above (`Mongo__DatabaseName=orchestration`, matching
   `infra/orchestration/main.tf`'s `module "persistence"` block).

**Cleanup — a real cluster exists in your Atlas org until you do this:**

```bash
terraform destroy -auto-approve \
  -target=module.persistence.mongodbatlas_database_user.this \
  -target=module.persistence.mongodbatlas_cluster.this \
  -target=module.persistence.mongodbatlas_project_ip_access_list.nat_gateway \
  -target=module.persistence.mongodbatlas_project.this \
  -target=module.persistence.random_password.database_user \
  -var="atlas_org_id=$MONGODB_ATLAS_ORG_ID"
```

Then revert step 2 (`git checkout -- infra/orchestration/main.tf`) and follow "Cleaning up" below.
**Destroy the Atlas resources before tearing down LocalStack, not after** — a combined destroy
that also targets the LocalStack-backed messaging/idempotency/S3 resources needs a live LocalStack
endpoint to refresh against; killing LocalStack first makes it hang retrying a dead endpoint
instead of failing cleanly. If that happens: kill the hung `terraform` process, remove the stale
`.terraform.tfstate.lock.info`, `terraform state rm` the LocalStack-backed resources (nothing to
reconcile — they don't exist anywhere anymore), then destroy just the Atlas resources.

## Cleaning up

```bash
docker compose -f docker-compose.localstack.yml down
rm -f infra/orchestration/terraform.tfstate infra/orchestration/terraform.tfstate.backup
```

Same reasoning as choreography's guide: this state only ever points at a specific LocalStack
container instance, so there's nothing worth preserving across restarts.
