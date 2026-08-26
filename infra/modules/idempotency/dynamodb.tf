# Claim-before-emit idempotency: a redelivered SQS message must not be double-processed.
# DynamoDbIdempotencyStore claims a message ID via a conditional PutItem
# (attribute_not_exists(MessageId) OR ExpiresAt < now), writing an ExpiresAt attribute on every
# claim. TTL exists so a claim orphaned by a genuine process crash (the app dies between claiming
# and either completing or hitting the catch-and-release path -- nothing throws, so ReleaseAsync
# never runs) doesn't block that message from ever being reprocessed: without this, the claim
# would sit forever and every redelivery would silently no-op. The store's own conditional-write
# check (ExpiresAt < now) is what actually makes a stale claim reclaimable promptly; this table's
# TTL setting only governs when DynamoDB physically deletes the item for storage/cost hygiene, not
# whether it's usable again (TTL deletion isn't instant/guaranteed-timely per AWS's own docs).

resource "aws_dynamodb_table" "idempotency" {
  name         = var.table_name
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "MessageId"

  attribute {
    name = "MessageId"
    type = "S"
  }

  ttl {
    attribute_name = "ExpiresAt"
    enabled        = true
  }

  tags = {
    Name = var.table_name
  }
}
