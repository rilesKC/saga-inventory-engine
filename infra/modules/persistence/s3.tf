data "aws_caller_identity" "current" {}

resource "aws_s3_bucket" "archive" {
  bucket = var.archive_bucket_name

  # This project's ECR repos needed force_delete added after `terraform destroy` failed once they
  # held an image (see docs/plans/choreography-aws-infra-plan.md) -- applying that lesson here from
  # the start rather than retrofitting it after the same failure recurs for S3.
  force_destroy = true

  tags = {
    Name = var.archive_bucket_name
  }
}

resource "aws_s3_bucket_public_access_block" "archive" {
  bucket = aws_s3_bucket.archive.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

# Denies any request to this bucket that doesn't use TLS, regardless of caller identity or
# permissions -- SonarCloud flags a bucket with no policy enforcing this (S3 traffic is otherwise
# allowed over plain HTTP, exposing archived event/saga-state payloads in transit).
resource "aws_s3_bucket_policy" "archive_https_only" {
  bucket = aws_s3_bucket.archive.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "DenyInsecureTransport"
        Effect    = "Deny"
        Principal = "*"
        Action    = "s3:*"
        Resource = [
          aws_s3_bucket.archive.arn,
          "${aws_s3_bucket.archive.arn}/*",
        ]
        Condition = {
          Bool = {
            "aws:SecureTransport" = "false"
          }
        }
      }
    ]
  })
}

# SonarCloud flags a bucket with no server access logging as a security finding. A second bucket
# is the standard AWS pattern for the log target -- it isn't itself logged (that would be circular),
# which is the accepted stopping point for this rule, not an oversight.
resource "aws_s3_bucket" "archive_logs" {
  bucket        = "${var.archive_bucket_name}-logs"
  force_destroy = true

  tags = {
    Name = "${var.archive_bucket_name}-logs"
  }
}

resource "aws_s3_bucket_public_access_block" "archive_logs" {
  bucket = aws_s3_bucket.archive_logs.id

  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_policy" "archive_logs_allow_log_delivery" {
  bucket = aws_s3_bucket.archive_logs.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid       = "S3ServerAccessLogsPolicy"
        Effect    = "Allow"
        Principal = { Service = "logging.s3.amazonaws.com" }
        Action    = "s3:PutObject"
        Resource  = "${aws_s3_bucket.archive_logs.arn}/*"
        Condition = {
          ArnLike = {
            "aws:SourceArn" = aws_s3_bucket.archive.arn
          }
          StringEquals = {
            "aws:SourceAccount" = data.aws_caller_identity.current.account_id
          }
        }
      }
    ]
  })
}

resource "aws_s3_bucket_logging" "archive" {
  bucket = aws_s3_bucket.archive.id

  target_bucket = aws_s3_bucket.archive_logs.id
  target_prefix = "log/"
}
