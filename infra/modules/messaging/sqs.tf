# One shared queue for the whole service, not one per event type or participant -- the service
# dispatches internally by event type through the existing EventBus, same as it always has.

resource "aws_sqs_queue" "dlq" {
  name = "${var.name}-dlq"

  tags = {
    Name = "${var.name}-dlq"
  }
}

resource "aws_sqs_queue" "this" {
  name = "${var.name}-queue"

  # DLQ-backed retry: after max_receive_count failed processing attempts (the message's visibility
  # timeout keeps expiring without it being deleted -- see SqsPollingBackgroundService), redirect
  # to the DLQ instead of retrying forever.
  redrive_policy = jsonencode({
    deadLetterTargetArn = aws_sqs_queue.dlq.arn
    maxReceiveCount     = var.max_receive_count
  })

  tags = {
    Name = "${var.name}-queue"
  }
}
