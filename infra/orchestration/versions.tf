terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  # Only set when validating/running against LocalStack -- unset (empty map) for a real deployment.
  dynamic "endpoints" {
    for_each = var.localstack_endpoint == "" ? [] : [1]

    content {
      ec2                    = var.localstack_endpoint
      ecr                    = var.localstack_endpoint
      ecs                    = var.localstack_endpoint
      elasticloadbalancingv2 = var.localstack_endpoint
      dynamodb               = var.localstack_endpoint
      sqs                    = var.localstack_endpoint
      iam                    = var.localstack_endpoint
      logs                   = var.localstack_endpoint
      sts                    = var.localstack_endpoint
    }
  }
}
