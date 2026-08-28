# New Relic license key, stored the same way as the Mongo connection string (Secrets Manager, not
# a plain environment variable) even though it's less sensitive than a DB credential -- it's still
# an account-wide secret readable by anyone with ecs:DescribeTaskDefinition otherwise. One secret
# for this whole stack (unlike Mongo's, which is owned by the persistence module since that module
# generates the value) since this one is just a passthrough of var.new_relic_license_key.

resource "aws_secretsmanager_secret" "new_relic_license_key" {
  name        = "${var.name}-new-relic-license-key"
  description = "New Relic license key for ${var.name}'s APM instrumentation."

  # See modules/persistence/secrets.tf's connection_string secret for why this is 0, not the
  # 30-day default -- this project deploys/tears down repeatedly by design.
  recovery_window_in_days = 0
}

resource "aws_secretsmanager_secret_version" "new_relic_license_key" {
  secret_id     = aws_secretsmanager_secret.new_relic_license_key.id
  secret_string = var.new_relic_license_key
}
