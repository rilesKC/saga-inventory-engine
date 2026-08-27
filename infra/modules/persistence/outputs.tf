output "connection_string_secret_arn" {
  description = "ARN of the Secrets Manager secret holding the full mongodb+srv connection string (credentials included). Pass to the compute module's secret_environment_variables (ECS's `secrets` block), not as a plain environment_variables entry -- the caller never sees the raw string, only this ARN."
  value       = aws_secretsmanager_secret.connection_string.arn
}

output "project_id" {
  value = mongodbatlas_project.this.id
}

output "archive_bucket_name" {
  value = aws_s3_bucket.archive.bucket
}

output "archive_bucket_arn" {
  value = aws_s3_bucket.archive.arn
}
