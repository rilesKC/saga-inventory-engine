variable "atlas_org_id" {
  description = "MongoDB Atlas organization ID -- Terraform creates a new project under this org."
  type        = string
}

variable "project_name" {
  description = "Name for the Atlas project this module creates -- one per saga stack, matching this project's per-stack-independence convention (each stack gets its own project/cluster, never shared)."
  type        = string
}

variable "cluster_name" {
  description = "Name for the M0 cluster within the created project."
  type        = string
  default     = "Cluster0"
}

variable "atlas_region" {
  description = "Atlas region code for the M0 tenant cluster -- Atlas's own SCREAMING_SNAKE_CASE naming, not the AWS CLI region string."
  type        = string
  default     = "US_EAST_1"
}

variable "database_user_name" {
  description = "Username for the database user this module creates, used in the app's Mongo connection string."
  type        = string
  default     = "app"
}

variable "database_name" {
  description = "Database name the created user is scoped readWrite access to."
  type        = string
}

variable "nat_gateway_ip" {
  description = "This stack's NAT gateway Elastic IP. M0 doesn't support VPC peering/PrivateLink (a paid-dedicated-tier-only feature), so network access is an IP allowlist scoped to this address rather than 0.0.0.0/0."
  type        = string
}

variable "archive_bucket_name" {
  description = "Name for the S3 archive bucket this module creates -- one per saga stack, same independence reasoning as the Atlas project above."
  type        = string
}
