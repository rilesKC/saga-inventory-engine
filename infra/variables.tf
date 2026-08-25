variable "name" {
  description = "Base name for every resource across all modules. The EventBridge bus this produces flows to the app as configuration (EventBridge__BusName), so overriding it is safe; the idempotency table name is not wired the same way yet and must still match DynamoDbIdempotencyStore's hardcoded TableName constant."
  type        = string
  default     = "order-saga-choreography"
}

variable "aws_region" {
  type    = string
  default = "us-east-1"
}

variable "azs" {
  type    = list(string)
  default = ["us-east-1a", "us-east-1b"]
}

variable "vpc_cidr" {
  type    = string
  default = "10.0.0.0/16"
}

variable "public_subnet_cidrs" {
  type    = list(string)
  default = ["10.0.0.0/24", "10.0.1.0/24"]
}

variable "private_subnet_cidrs" {
  type    = list(string)
  default = ["10.0.10.0/24", "10.0.11.0/24"]
}

variable "image_tag" {
  type    = string
  default = "latest"
}

variable "localstack_endpoint" {
  description = "Set (e.g. http://localhost:4566) to validate/run against LocalStack instead of real AWS. Leave empty for a real deployment."
  type        = string
  default     = ""
}
