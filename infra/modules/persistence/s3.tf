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
