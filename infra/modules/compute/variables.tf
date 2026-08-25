variable "name" {
  description = "Prefix used for naming every resource this module creates."
  type        = string
}

variable "aws_region" {
  type = string
}

variable "private_subnet_ids" {
  type = list(string)
}

variable "app_security_group_id" {
  type = string
}

variable "target_group_arn" {
  description = "ALB target group to register this service against. Omit (leave null) for a service with no HTTP surface -- it then relies on ECS's own task-health signal instead of a target-group health check, and gets no container port mapping or load_balancer block at all."
  type        = string
  default     = null
}

variable "task_execution_role_arn" {
  type = string
}

variable "task_role_arn" {
  type = string
}

variable "ecr_repository_url" {
  type = string
}

variable "image_tag" {
  type    = string
  default = "latest"
}

variable "log_group_name" {
  type = string
}

variable "environment_variables" {
  description = "App-specific container environment variables (queue URLs, table names, bus names, etc.), supplied by the caller. Deliberately generic -- this module is shared by services with completely different configuration shapes, not just choreography's Sqs__QueueUrl/EventBridge__BusName pair, so it can't hardcode any one service's variable names."
  type = list(object({
    name  = string
    value = string
  }))
  default = []
}

variable "app_port" {
  type    = number
  default = 8080
}

variable "cpu" {
  description = "Fargate task CPU units."
  type        = number
  default     = 256
}

variable "memory" {
  description = "Fargate task memory (MB). Must be a valid combination with var.cpu."
  type        = number
  default     = 512
}
