resource "aws_ecr_repository" "this" {
  name = var.name

  # This project's whole deployment model is "verify, then tear down immediately" -- without
  # force_delete, `terraform destroy` fails outright once an image has been pushed, since ECR
  # refuses to delete a non-empty repository. Discovered by hitting exactly that error on the
  # first real teardown.
  force_delete = true

  image_scanning_configuration {
    scan_on_push = true
  }
}
