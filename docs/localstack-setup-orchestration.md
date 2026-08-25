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
plus their own queue URLs:

```bash
# Terminal 1 -- CoordinatorHost (the only one with an HTTP port)
cd src/OrderSaga.Orchestration.CoordinatorHost
ASPNETCORE_URLS=http://localhost:5100 \
  Sqs__InventoryCommandsQueueUrl="<inventory_commands_queue_url>" \
  Sqs__StatelessResponderCommandsQueueUrl="<stateless_responder_commands_queue_url>" \
  Sqs__CoordinatorInboundQueueUrl="<coordinator_inbound_queue_url>" \
  dotnet run

# Terminal 2 -- InventoryHost
cd src/OrderSaga.Orchestration.InventoryHost
Sqs__CoordinatorInboundQueueUrl="<coordinator_inbound_queue_url>" \
  Sqs__InventoryCommandsQueueUrl="<inventory_commands_queue_url>" \
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

## Cleaning up

```bash
docker compose -f docker-compose.localstack.yml down
rm -f infra/orchestration/terraform.tfstate infra/orchestration/terraform.tfstate.backup
```

Same reasoning as choreography's guide: this state only ever points at a specific LocalStack
container instance, so there's nothing worth preserving across restarts.
