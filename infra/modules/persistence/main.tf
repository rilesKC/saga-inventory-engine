# One Atlas project + M0 cluster + database user per calling stack, matching this project's
# per-stack-independence convention (each stack gets its own everything, never shared -- see
# docs/specs/saga-persistence.md). M0 doesn't support VPC peering/PrivateLink (a paid-dedicated-
# tier-only feature), so network access is an IP allowlist scoped to this stack's NAT gateway
# Elastic IP instead of 0.0.0.0/0.

resource "mongodbatlas_project" "this" {
  name   = var.project_name
  org_id = var.atlas_org_id
}

resource "mongodbatlas_project_ip_access_list" "nat_gateway" {
  project_id = mongodbatlas_project.this.id
  ip_address = var.nat_gateway_ip
  comment    = "Fargate egress via this stack's NAT gateway"
}

resource "mongodbatlas_cluster" "this" {
  project_id = mongodbatlas_project.this.id
  name       = var.cluster_name

  provider_name               = "TENANT"
  backing_provider_name       = "AWS"
  provider_region_name        = var.atlas_region
  provider_instance_size_name = "M0"
}

# Generated here, not caller-supplied -- one fewer secret for a human to invent or store. Only the
# resulting connection string (the sensitive output below) needs handling from here on.
resource "random_password" "database_user" {
  length  = 24
  special = false
}

resource "mongodbatlas_database_user" "this" {
  project_id         = mongodbatlas_project.this.id
  username           = var.database_user_name
  password           = random_password.database_user.result
  auth_database_name = "admin"

  roles {
    role_name     = "readWrite"
    database_name = var.database_name
  }
}
