# LocalStack Setup

Validates the `messaging` and `idempotency` Terraform modules and the Host application's real AWS
SDK integration code (EventBridge publish, SQS poll, DynamoDB claim) against LocalStack — not the
VPC/ALB/ECS layer, which LocalStack's free Community edition doesn't emulate. See
`docs/plans/choreography-aws-infra-plan.md` task 21 for the full scope-adjustment reasoning.

## Prerequisites

- Docker
- A free LocalStack account and auth token — LocalStack now requires one to run at all, not just
  for Pro features. Sign up at [app.localstack.cloud](https://app.localstack.cloud), find your
  auth token, and put it in a `.env` file at the repo root (gitignored, never commit it):
  ```
  LOCALSTACK_AUTH_TOKEN=<your-token>
  ```

## Running LocalStack

```bash
docker compose -f docker-compose.localstack.yml up -d
```

Wait for it to be ready (the container's own Docker healthcheck can report "unhealthy" even when
LocalStack itself is fine — check its actual health endpoint instead):

```bash
curl -s http://localhost:4566/_localstack/health
```

Confirm `sqs`, `dynamodb`, `events`, `iam`, `sts`, and `s3` all show `"available"`.

## Applying Terraform against it

From `infra/`, targeting only the modules LocalStack can meaningfully emulate:

```bash
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test terraform apply -auto-approve \
  -target=module.messaging -target=module.idempotency \
  -var="localstack_endpoint=http://localhost:4566"
```

Note the `queue_url` and `event_bus_name` outputs — you'll need both for the Host app below.

## Running the Host app against it

Not containerized, not through ECS — just `dotnet run` locally, pointed at LocalStack. The Host
also unconditionally requires Mongo/S3 config at startup (it throws before ever binding a port if
these are missing) — see "Running against a real Mongo Atlas cluster too" below for where these
values come from; there's no way to start the Host without them, even for LocalStack-only checks:

```bash
cd src/OrderSaga.Choreography.Host
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=us-east-1 \
  Aws__ServiceUrl=http://localhost:4566 \
  Sqs__QueueUrl="<queue_url output from above>" \
  EventBridge__BusName="<event_bus_name output from above, "order-saga-choreography" by default>" \
  Mongo__ConnectionString="<connection string, see below>" \
  Mongo__DatabaseName="inventory" \
  Mongo__InventoryEventsCollectionName="inventory-events" \
  S3__ArchiveBucketName="order-saga-choreography-event-archive" \
  dotnet run
```

Then exercise the saga:

```bash
curl -X POST http://localhost:5000/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORDER-1","sku":"SKU-1","quantity":4,"amount":199.99}'
```

And look up what it did (added alongside the BFF/web client — see
`docs/specs/bff-web-client.md`):

```bash
curl http://localhost:5000/orders/ORDER-1
```

A healthy run returns `status: "Reserved"` (or `"Confirmed"` once payment/confirmation have also
processed) and a `history` array of the events that got there
(`StockReserved`/`ReservationConfirmed`/`ReservationReleased`).

## Verifying it actually worked

No AWS CLI needed — the SQS/DynamoDB query APIs work fine over plain `curl` against LocalStack:

```bash
# Main queue and DLQ should both settle back to 0 once the poller catches up
curl -s -X POST http://localhost:4566/000000000000/order-saga-choreography-queue \
  -H "Content-Type: application/x-www-form-urlencoded" \
  --data-urlencode "Action=GetQueueAttributes" \
  --data-urlencode "QueueUrl=http://sqs.us-east-1.localhost.localstack.cloud:4566/000000000000/order-saga-choreography-queue" \
  --data-urlencode "AttributeName.1=ApproximateNumberOfMessages" \
  --data-urlencode "Version=2012-11-05"

# Idempotency claims -- one row per distinct event actually processed
curl -s -X POST http://localhost:4566/ \
  -H "Content-Type: application/x-amz-json-1.0" \
  -H "X-Amz-Target: DynamoDB_20120810.Scan" \
  -d '{"TableName":"order-saga-choreography-idempotency"}'
```

A healthy happy-path run produces exactly 5 claims (`OrderPlaced`, `StockReserved`,
`PaymentCharged`, `ReservationConfirmed`, `ShipmentScheduled`) and an empty queue/DLQ afterward.

## S3 event archive (standalone, not through the full Host)

Apply just the bucket:

```bash
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test terraform apply -auto-approve \
  -target='module.persistence.aws_s3_bucket.archive' \
  -target='module.persistence.aws_s3_bucket_public_access_block.archive' \
  -var="localstack_endpoint=http://localhost:4566"
```

Then exercise `S3EventArchiveWriter` directly (a small standalone `dotnet run`, not the full Host)
against `order-saga-choreography-event-archive`, and confirm the object landed via a plain `curl`
against the bucket — no AWS CLI needed, same precedent as the SQS/DynamoDB checks above.

## Running against a real Mongo Atlas cluster too

MongoDB Atlas isn't an AWS service, so LocalStack can't emulate the live Mongo store the full Host
needs — and the Host requires it unconditionally at startup (see above), so there's no way to run
it against LocalStack alone. This is genuinely exercisable locally, though — confirmed working
2026-09-10 — it just needs a temporary real (free-tier) Atlas cluster alongside LocalStack:

1. **Get your machine's public IP** (`curl -s https://api.ipify.org`) — the cluster's network
   access list needs it, since nothing is deployed to provide the NAT gateway IP the module
   normally uses.
2. **Temporarily edit `infra/main.tf`'s `module "persistence"` block** — replace
   `nat_gateway_ip = module.networking.nat_gateway_ip` with your IP as a literal string. Revert
   this before committing anything; never check in a personal IP.
3. **Apply just the Atlas resources**, real credentials, no LocalStack/AWS involved for these:
   ```bash
   terraform apply -auto-approve \
     -target=module.persistence.mongodbatlas_project.this \
     -target=module.persistence.mongodbatlas_project_ip_access_list.nat_gateway \
     -target=module.persistence.mongodbatlas_cluster.this \
     -target=module.persistence.random_password.database_user \
     -target=module.persistence.mongodbatlas_database_user.this \
     -var="atlas_org_id=$MONGODB_ATLAS_ORG_ID"
   ```
   (Requires `MONGODB_ATLAS_PUBLIC_KEY`/`MONGODB_ATLAS_PRIVATE_KEY`/`MONGODB_ATLAS_ORG_ID` in the
   repo-root `.env` — the provider reads the first two automatically.)
4. **There's no root output for the connection string** (the module only exposes a Secrets
   Manager ARN, which LocalStack's free tier doesn't emulate) — pull it straight from state
   instead, which isn't redacted the way `terraform state show`'s human output is:
   ```bash
   $state = terraform state pull | ConvertFrom-Json
   $pw = ($state.resources | Where-Object { $_.type -eq "random_password" -and $_.name -eq "database_user" }).instances[0].attributes.result
   $srv = ($state.resources | Where-Object { $_.type -eq "mongodbatlas_cluster" }).instances[0].attributes.connection_strings[0].standard_srv
   # Connection string: replace "mongodb+srv://" with "mongodb+srv://app:$pw@"
   ```
5. Use that connection string as `Mongo__ConnectionString` above (`Mongo__DatabaseName=inventory`,
   matching `infra/main.tf`'s `module "persistence"` block).

**Cleanup — don't skip this, it's a real (if free) cluster in your Atlas org:**

```bash
terraform destroy -auto-approve \
  -target=module.persistence.mongodbatlas_database_user.this \
  -target=module.persistence.mongodbatlas_cluster.this \
  -target=module.persistence.mongodbatlas_project_ip_access_list.nat_gateway \
  -target=module.persistence.mongodbatlas_project.this \
  -target=module.persistence.random_password.database_user \
  -var="atlas_org_id=$MONGODB_ATLAS_ORG_ID"
```

Then revert step 2's edit (`git checkout -- infra/main.tf`) and follow the "Cleaning up" section
below as usual. **Destroy the Atlas resources *before* tearing down LocalStack, not after** — the
LocalStack-backed resources (SQS/DynamoDB/S3) in the same apply need a live LocalStack endpoint to
refresh against; tearing LocalStack down first makes a combined `terraform destroy` hang retrying
a dead endpoint instead of erroring cleanly. If that happens: kill the hung `terraform` process,
remove the stale `.terraform.tfstate.lock.info`, `terraform state rm` the LocalStack-backed
resources (they don't exist anywhere anymore, so there's nothing to reconcile), then destroy just
the Atlas resources on their own.

## Cleaning up

```bash
docker compose -f docker-compose.localstack.yml down
rm -f infra/terraform.tfstate infra/terraform.tfstate.backup
```

The local Terraform state only ever points at a specific LocalStack container instance's
resources — there's nothing worth preserving across restarts, so just clear it rather than trying
to reconcile against a instance that no longer exists.
