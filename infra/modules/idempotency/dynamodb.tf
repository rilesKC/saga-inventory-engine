# Claim-before-emit idempotency: a redelivered SQS message must not be double-processed.
# DynamoDbIdempotencyStore claims a message ID via a conditional PutItem
# (attribute_not_exists(MessageId)) -- no TTL configured here since the app code doesn't set an
# expiry attribute on write; auto-expiring old claims is a reasonable future enhancement, not
# built now, since Terraform TTL config is a no-op without the app writing the attribute.

resource "aws_dynamodb_table" "idempotency" {
  name         = var.table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "MessageId"

  attribute {
    name = "MessageId"
    type = "S"
  }

  tags = {
    Name = var.table_name
  }
}
