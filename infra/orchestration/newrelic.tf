# New Relic license key, stored the same way as the Mongo connection string (Secrets Manager, not
# a plain environment variable) even though it's less sensitive than a DB credential -- it's still
# an account-wide secret readable by anyone with ecs:DescribeTaskDefinition otherwise. One secret
# shared across all three services in this root (Coordinator, Inventory, Responder), matching how
# they already share one Mongo Atlas cluster and one idempotency table.

resource "aws_secretsmanager_secret" "new_relic_license_key" {
  name        = "${var.name}-new-relic-license-key"
  description = "New Relic license key for ${var.name}'s APM instrumentation."
}

resource "aws_secretsmanager_secret_version" "new_relic_license_key" {
  secret_id     = aws_secretsmanager_secret.new_relic_license_key.id
  secret_string = var.new_relic_license_key
}
