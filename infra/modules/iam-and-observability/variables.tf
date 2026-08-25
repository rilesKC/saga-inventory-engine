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

variable "log_retention_days" {
  description = "CloudWatch log group retention."
  type        = number
  default     = 14
}
