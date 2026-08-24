output "vpc_id" {
  value = aws_vpc.this.id
}

output "public_subnet_ids" {
  value = aws_subnet.public[*].id
}

output "private_subnet_ids" {
  value = aws_subnet.private[*].id
}

output "private_route_table_id" {
  description = "Exposed so task 18's VPC endpoints module can associate route table entries for gateway endpoints (S3)."
  value       = aws_route_table.private.id
}
