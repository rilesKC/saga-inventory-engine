variable "table_name" {
  description = "Must match DynamoDbIdempotencyStore's hardcoded TableName constant."
  type        = string
  default     = "order-saga-choreography-idempotency"
}
