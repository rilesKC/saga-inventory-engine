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

output "nat_gateway_ip" {
  description = "The NAT Gateway's Elastic IP -- every private-subnet task egresses through this address, so it's what the persistence module's Atlas IP allowlist scopes to instead of 0.0.0.0/0."
  value       = aws_eip.nat.public_ip
}
