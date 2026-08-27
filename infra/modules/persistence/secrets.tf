# The Mongo connection string (credentials included) used to be passed to ECS as a plain
# `environment` variable -- readable by anyone with ecs:DescribeTaskDefinition, in CloudTrail, or
# via Terraform state access, with no rotation path. It's created here (not in the root modules)
# since this module already owns the credential's whole lifecycle -- the caller only ever sees this
# secret's ARN, never the raw string, and wires it into the compute module's
# `secret_environment_variables` (ECS's `secrets` block) instead of `environment_variables`.

resource "aws_secretsmanager_secret" "connection_string" {
  name        = "${var.project_name}-mongo-connection-string"
  description = "MongoDB Atlas connection string (credentials included) for ${var.project_name}."
}

resource "aws_secretsmanager_secret_version" "connection_string" {
  secret_id = aws_secretsmanager_secret.connection_string.id
  secret_string = replace(
    mongodbatlas_cluster.this.connection_strings[0].standard_srv,
    "mongodb+srv://",
    "mongodb+srv://${var.database_user_name}:${random_password.database_user.result}@"
  )
}
