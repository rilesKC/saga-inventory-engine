output "table_name" {
  value = aws_dynamodb_table.idempotency.name
}

output "table_arn" {
  value = aws_dynamodb_table.idempotency.arn
}
