variable "name" {
  description = "Prefix used for naming every resource this module creates."
  type        = string
}

variable "task_policy_statements" {
  description = "Least-privilege IAM policy statements for this service's task role, supplied by the caller -- deliberately generic (actions + resources per statement) rather than hardcoded to any one service's fixed set of permissions."
  type = list(object({
    actions   = list(string)
    resources = list(string)
  }))
}

variable "task_execution_policy_statements" {
  description = "Extra least-privilege IAM policy statements for this service's *execution* role, beyond the standard AmazonECSTaskExecutionRolePolicy (ECR pull, CloudWatch Logs write). Needed for secretsmanager:GetSecretValue on any ARN this service's compute module resolves via secret_environment_variables -- ECS uses the execution role, not the task role, to fetch container secrets at startup. Empty for services with no secrets."
  type = list(object({
    actions   = list(string)
    resources = list(string)
  }))
  default = []
}

variable "log_retention_days" {
  description = "CloudWatch log group retention."
  type        = number
  default     = 14
}
