output "task_execution_role_arn" {
  value = aws_iam_role.task_execution.arn
}

output "task_role_arn" {
  value = aws_iam_role.task.arn
}

output "ecr_repository_url" {
  value = aws_ecr_repository.this.repository_url
}

output "log_group_name" {
  value = aws_cloudwatch_log_group.this.name
}
