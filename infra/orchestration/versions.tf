terraform {
  required_version = ">= 1.5"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
    mongodbatlas = {
      source  = "mongodb/mongodbatlas"
      version = "~> 1.0"
    }
    random = {
      source  = "hashicorp/random"
      version = "~> 3.0"
    }
  }
}

provider "aws" {
  region = var.aws_region

  # Same LocalStack S3 path-style requirement as the app's own AmazonS3Config.ForcePathStyle --
  # false (the AWS SDK/provider default) is correct for real S3, which is why this only applies
  # when localstack_endpoint is actually set.
  s3_use_path_style = var.localstack_endpoint != ""

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
      s3                     = var.localstack_endpoint
    }
  }
}

# Relies on the MONGODB_ATLAS_PUBLIC_KEY/MONGODB_ATLAS_PRIVATE_KEY environment variables the
# provider reads automatically -- same reasoning as choreography's infra/versions.tf. Not
# LocalStack-overridable: MongoDB Atlas isn't an AWS service, so it's always the real API.
provider "mongodbatlas" {}
