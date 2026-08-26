data "aws_caller_identity" "current" {}

locals {
  # Deny statement is identical for both buckets except which bucket it protects -- kept as one
  # template here instead of the same literal JSON copy-pasted into both policies below.
  deny_insecure_transport_statements = {
    for key, bucket in {
      archive      = aws_s3_bucket.archive
      archive_logs = aws_s3_bucket.archive_logs
      } : key => {
      Sid       = "DenyInsecureTransport"
      Effect    = "Deny"
      Principal = "*"
      Action    = "s3:*"
      Resource  = [bucket.arn, "${bucket.arn}/*"]
      Condition = {
        Bool = {
          "aws:SecureTransport" = "false"
        }
      }
    }
  }
}

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

# A second bucket is the standard AWS pattern for the log target. It logs to itself (AWS's own
# documented pattern for "who logs the log bucket" -- S3 access logging explicitly supports the
# target bucket being the same as the source bucket), and carries the same HTTPS-only deny
# statement as the archive bucket, so it doesn't reintroduce the two findings this whole file
# exists to close.
resource "aws_s3_bucket" "archive_logs" {
  bucket        = "${var.archive_bucket_name}-logs"
  force_destroy = true

  tags = {
    Name = "${var.archive_bucket_name}-logs"
  }
}

resource "aws_s3_bucket_public_access_block" "this" {
  for_each = {
    archive      = aws_s3_bucket.archive.id
    archive_logs = aws_s3_bucket.archive_logs.id
  }

  bucket = each.value

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
    Version   = "2012-10-17"
    Statement = [local.deny_insecure_transport_statements["archive"]]
  })
}

resource "aws_s3_bucket_policy" "archive_logs" {
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
      },
      local.deny_insecure_transport_statements["archive_logs"],
    ]
  })
}

resource "aws_s3_bucket_logging" "archive" {
  bucket = aws_s3_bucket.archive.id

  target_bucket = aws_s3_bucket.archive_logs.id
  target_prefix = "log/"
}

resource "aws_s3_bucket_logging" "archive_logs" {
  bucket = aws_s3_bucket.archive_logs.id

  target_bucket = aws_s3_bucket.archive_logs.id
  target_prefix = "log/"
}
