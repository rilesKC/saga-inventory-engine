# Direct SQS, no EventBridge -- every orchestration command has exactly one possible consumer, so
# there's no fan-out for EventBridge's rule-based routing to do anything useful with (see
# docs/specs/orchestration-aws-infra.md). Three queues, each scoped to what its one consuming
# service actually needs to receive: the Coordinator polls only coordinator-inbound; each responder
# service polls only its own command queue.

locals {
  queue_names = [
    "inventory-commands",
    "stateless-responder-commands",
    "coordinator-inbound",
  ]
}

resource "aws_sqs_queue" "dlq" {
  for_each = toset(local.queue_names)

  name = "${var.name}-${each.value}-dlq"

  tags = {
    Name = "${var.name}-${each.value}-dlq"
  }
}

resource "aws_sqs_queue" "this" {
  for_each = toset(local.queue_names)

  name = "${var.name}-${each.value}"

  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq[each.value].arn
    maxReceiveCount     = var.max_receive_count
  })

  tags = {
    Name = "${var.name}-${each.value}"
  }
}
