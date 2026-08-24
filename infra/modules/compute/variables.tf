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
  type = string
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

variable "queue_url" {
  type = string
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
