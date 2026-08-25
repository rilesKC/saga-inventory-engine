variable "name" {
  description = "Prefix used for naming every resource this module creates."
  type        = string
}

variable "event_bus_arn" {
  description = "ARN of the EventBridge bus the task role may PutEvents to (from the messaging module)."
  type        = string
}

variable "queue_arn" {
  description = "ARN of the SQS queue the task role may receive/delete from (from the messaging module)."
  type        = string
}

variable "idempotency_table_arn" {
  description = "ARN of the DynamoDB idempotency table the task role may PutItem to (from the idempotency module)."
  type        = string
}

variable "log_retention_days" {
  description = "CloudWatch log group retention."
  type        = number
  default     = 14
}
