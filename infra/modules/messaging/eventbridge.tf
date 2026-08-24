# Every event round-trips through EventBridge, not just the external trigger -- one rule per
# known event type, all targeting the single shared SQS queue. Matches OutboundEventForwarder's
# explicit per-type subscriptions (EventTypeRegistry) on the app side.

resource "aws_cloudwatch_event_bus" "this" {
  name = var.name
}

locals {
  event_types = [
    "OrderPlaced",
    "StockReserved",
    "StockReservationFailed",
    "PaymentCharged",
    "PaymentDeclined",
    "ReservationConfirmed",
    "ReservationReleased",
    "ShipmentScheduled",
  ]
}

resource "aws_cloudwatch_event_rule" "this" {
  for_each = toset(local.event_types)

  name           = "${var.name}-${each.value}"
  event_bus_name = aws_cloudwatch_event_bus.this.name

  event_pattern = jsonencode({
    detail-type = [each.value]
  })
}

resource "aws_cloudwatch_event_target" "this" {
  for_each = aws_cloudwatch_event_rule.this

  rule           = each.value.name
  event_bus_name = aws_cloudwatch_event_bus.this.name
  arn            = aws_sqs_queue.this.arn
}

# EventBridge needs explicit permission to send to this queue -- without this policy, PutEvents
# succeeds but the rule silently never delivers anything (a classic, hard-to-diagnose gap).
resource "aws_sqs_queue_policy" "allow_eventbridge" {
  queue_url = aws_sqs_queue.this.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect    = "Allow"
        Principal = { Service = "events.amazonaws.com" }
        Action    = "sqs:SendMessage"
        Resource  = aws_sqs_queue.this.arn
        Condition = {
          ArnEquals = {
            "aws:SourceArn" = [for rule in aws_cloudwatch_event_rule.this : rule.arn]
          }
        }
      }
    ]
  })
}
