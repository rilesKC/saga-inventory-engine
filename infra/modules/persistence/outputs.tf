output "connection_string" {
  description = "Full mongodb+srv connection string, credentials included -- ready to pass straight to MongoClient."
  value = replace(
    mongodbatlas_cluster.this.connection_strings[0].standard_srv,
    "mongodb+srv://",
    "mongodb+srv://${var.database_user_name}:${random_password.database_user.result}@"
  )
  sensitive = true
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
