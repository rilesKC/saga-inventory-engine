variable "name" {
  description = "Base name for every resource across all modules."
  type        = string
  default     = "order-saga-orchestration"
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
  default = "10.1.0.0/16"
}

variable "public_subnet_cidrs" {
  type    = list(string)
  default = ["10.1.0.0/24", "10.1.1.0/24"]
}

variable "private_subnet_cidrs" {
  type    = list(string)
  default = ["10.1.10.0/24", "10.1.11.0/24"]
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
