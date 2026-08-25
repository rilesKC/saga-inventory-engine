# Internet egress for the private subnets: one single NAT Gateway (not one per AZ -- matches the
# already-reduced-redundancy posture of task 17's single Fargate instance) for ECR image pulls,
# CloudWatch Logs, EventBridge, and SQS calls, plus free gateway endpoints for S3 (ECR image
# layers live in S3) and DynamoDB (no hourly cost, so there's no reason not to use them).
#
# Named egress.tf rather than the plan's original "vpc-endpoints.tf" since it now covers NAT too,
# not just endpoints -- see task 18's retro in the plan file for why interface endpoints alone
# turned out to be the more expensive choice at this deployment's real scale.

resource "aws_eip" "nat" {
  domain = "vpc"

  tags = {
    Name = "${var.name}-nat"
  }
}

resource "aws_nat_gateway" "this" {
  allocation_id = aws_eip.nat.id
  subnet_id     = aws_subnet.public[0].id

  tags = {
    Name = "${var.name}-nat"
  }

  depends_on = [aws_internet_gateway.this]
}

resource "aws_route" "private_nat" {
  route_table_id         = aws_route_table.private.id
  destination_cidr_block = "0.0.0.0/0"
  nat_gateway_id         = aws_nat_gateway.this.id
}

resource "aws_vpc_endpoint" "s3" {
  vpc_id            = aws_vpc.this.id
  service_name      = "com.amazonaws.${data.aws_region.current.name}.s3"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = [aws_route_table.private.id]
}

resource "aws_vpc_endpoint" "dynamodb" {
  vpc_id            = aws_vpc.this.id
  service_name      = "com.amazonaws.${data.aws_region.current.name}.dynamodb"
  vpc_endpoint_type = "Gateway"
  route_table_ids   = [aws_route_table.private.id]
}

data "aws_region" "current" {}
