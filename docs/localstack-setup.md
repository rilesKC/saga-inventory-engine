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

Not containerized, not through ECS — just `dotnet run` locally, pointed at LocalStack:

```bash
cd src/OrderSaga.Choreography.Host
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test AWS_REGION=us-east-1 \
  Aws__ServiceUrl=http://localhost:4566 \
  Sqs__QueueUrl="<queue_url output from above>" \
  EventBridge__BusName="<event_bus_name output from above, "order-saga-choreography" by default>" \
  dotnet run
```

Then exercise the saga:

```bash
curl -X POST http://localhost:5000/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORDER-1","sku":"SKU-1","quantity":4,"amount":199.99}'
```

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

MongoDB Atlas isn't an AWS service, so LocalStack can't emulate the live Mongo store the full Host
needs — only the S3 archive side of persistence is worth validating here (see
`docs/specs/saga-persistence.md`); the Mongo-backed path is only exercisable against the real Atlas
cluster (see the Saga Persistence plan's real-deployment task). Apply just the bucket:

```bash
AWS_ACCESS_KEY_ID=test AWS_SECRET_ACCESS_KEY=test terraform apply -auto-approve \
  -target='module.persistence.aws_s3_bucket.archive' \
  -target='module.persistence.aws_s3_bucket_public_access_block.archive' \
  -var="localstack_endpoint=http://localhost:4566"
```

Then exercise `S3EventArchiveWriter` directly (a small standalone `dotnet run`, not the full Host)
against `order-saga-choreography-event-archive`, and confirm the object landed via a plain `curl`
against the bucket — no AWS CLI needed, same precedent as the SQS/DynamoDB checks above.

## Cleaning up

```bash
docker compose -f docker-compose.localstack.yml down
rm -f infra/terraform.tfstate infra/terraform.tfstate.backup
```

The local Terraform state only ever points at a specific LocalStack container instance's
resources — there's nothing worth preserving across restarts, so just clear it rather than trying
to reconcile against a instance that no longer exists.
