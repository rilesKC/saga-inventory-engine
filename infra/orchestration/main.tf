module "networking" {
  source = "../modules/networking"

  name                 = var.name
  vpc_cidr             = var.vpc_cidr
  azs                  = var.azs
  public_subnet_cidrs  = var.public_subnet_cidrs
  private_subnet_cidrs = var.private_subnet_cidrs
}

module "orchestration_messaging" {
  source = "../modules/orchestration-messaging"

  name = var.name
}

module "idempotency" {
  source = "../modules/idempotency"

  # New, dedicated table -- not choreography's. Config-driven on the app side
  # (DynamoDbIdempotencyStore takes the table name via configuration, not a hardcoded constant),
  # so whatever name this produces is automatically correct wherever it's wired into environment
  # variables below -- no drift risk the way choreography's original hardcoded constant had.
  table_name = "${var.name}-idempotency"
}

module "load_balancer" {
  source = "../modules/load-balancer"

  name              = var.name
  vpc_id            = module.networking.vpc_id
  public_subnet_ids = module.networking.public_subnet_ids
}

# One shared Atlas cluster + S3 bucket for this stack (Coordinator's SagaState and Inventory's
# InventoryItem events live in the same cluster, different collections/databases) -- same
# independence-from-choreography reasoning as this root's other resources, per
# docs/specs/saga-persistence.md.
module "persistence" {
  source = "../modules/persistence"

  atlas_org_id        = var.atlas_org_id
  project_name        = "${var.name}-persistence"
  database_name       = "orchestration"
  nat_gateway_ip      = module.networking.nat_gateway_ip
  archive_bucket_name = "${var.name}-event-archive"
}

# Outbound-only security group for the two services with no HTTP surface (Inventory, Responder) --
# small and specific enough to this root's shape that it doesn't warrant its own module the way
# the ALB-attached security group does inside load-balancer/.
resource "aws_security_group" "background_worker" {
  name        = "${var.name}-background-worker"
  description = "Outbound-only security group for services with no inbound HTTP surface."
  vpc_id      = module.networking.vpc_id

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

# Per-service config for the IAM and compute modules below -- least-privilege policy statements and
# container environment variables differ per service, but the module shape (source, variable names)
# is identical, so a for_each over this map replaces what was three near-identical copies of each
# module block.
locals {
  services = {
    coordinator = {
      task_policy_statements = [
        {
          # Includes coordinator-inbound itself -- OrderIntakeHandler publishes the initial
          # OrderPlaced trigger onto that same queue it also polls, not just the two command queues.
          actions = ["sqs:SendMessage"]
          resources = [
            module.orchestration_messaging.inventory_commands_queue_arn,
            module.orchestration_messaging.stateless_responder_commands_queue_arn,
            module.orchestration_messaging.coordinator_inbound_queue_arn,
          ]
        },
        {
          actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
          resources = [module.orchestration_messaging.coordinator_inbound_queue_arn]
        },
        {
          actions   = ["dynamodb:PutItem", "dynamodb:DeleteItem"]
          resources = [module.idempotency.table_arn]
        },
        {
          actions   = ["s3:PutObject"]
          resources = ["${module.persistence.archive_bucket_arn}/*"]
        },
      ]
      app_security_group_id = module.load_balancer.app_security_group_id
      target_group_arn      = module.load_balancer.target_group_arn
      # Raised from 1 as part of the Saga Persistence spec -- proof that SagaState's Mongo-backed
      # persistence actually unblocks multi-instance operation, not just that the config now
      # allows it. See docs/plans/saga-persistence-plan.md task 21.
      desired_count = 2
      environment_variables = [
        { name = "Sqs__InventoryCommandsQueueUrl", value = module.orchestration_messaging.inventory_commands_queue_url },
        { name = "Sqs__StatelessResponderCommandsQueueUrl", value = module.orchestration_messaging.stateless_responder_commands_queue_url },
        { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
        { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
        { name = "Mongo__ConnectionString", value = module.persistence.connection_string },
        { name = "Mongo__DatabaseName", value = "orchestration" },
        { name = "Mongo__SagaStateCollectionName", value = "saga-state" },
        { name = "S3__ArchiveBucketName", value = module.persistence.archive_bucket_name },
      ]
    }
    inventory = {
      task_policy_statements = [
        {
          actions   = ["sqs:SendMessage"]
          resources = [module.orchestration_messaging.coordinator_inbound_queue_arn]
        },
        {
          actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
          resources = [module.orchestration_messaging.inventory_commands_queue_arn]
        },
        {
          actions   = ["dynamodb:PutItem", "dynamodb:DeleteItem"]
          resources = [module.idempotency.table_arn]
        },
        {
          actions   = ["s3:PutObject"]
          resources = ["${module.persistence.archive_bucket_arn}/*"]
        },
      ]
      app_security_group_id = aws_security_group.background_worker.id
      target_group_arn      = null
      # Raised from 1 as part of the Saga Persistence spec -- proof that InventoryItem's
      # Mongo-backed persistence actually unblocks multi-instance operation, not just that the
      # config now allows it. See docs/plans/saga-persistence-plan.md task 21.
      desired_count = 2
      environment_variables = [
        { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
        { name = "Sqs__InventoryCommandsQueueUrl", value = module.orchestration_messaging.inventory_commands_queue_url },
        { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
        { name = "Mongo__ConnectionString", value = module.persistence.connection_string },
        { name = "Mongo__DatabaseName", value = "orchestration" },
        { name = "Mongo__InventoryEventsCollectionName", value = "inventory-events" },
        { name = "S3__ArchiveBucketName", value = module.persistence.archive_bucket_name },
      ]
    }
    responder = {
      task_policy_statements = [
        {
          actions   = ["sqs:SendMessage"]
          resources = [module.orchestration_messaging.coordinator_inbound_queue_arn]
        },
        {
          actions   = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"]
          resources = [module.orchestration_messaging.stateless_responder_commands_queue_arn]
        },
        {
          actions   = ["dynamodb:PutItem", "dynamodb:DeleteItem"]
          resources = [module.idempotency.table_arn]
        },
      ]
      app_security_group_id = aws_security_group.background_worker.id
      target_group_arn      = null
      # The one service in this deployment genuinely safe to run multi-instance today: neither
      # PaymentResponder nor ShippingResponder holds any cross-order state, unlike the Coordinator's
      # SagaState or the Inventory responder's InventoryItem. See docs/specs/orchestration-aws-infra.md.
      desired_count = 2
      environment_variables = [
        { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
        { name = "Sqs__StatelessResponderCommandsQueueUrl", value = module.orchestration_messaging.stateless_responder_commands_queue_url },
        { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
      ]
    }
  }
}

module "iam_and_observability" {
  source   = "../modules/iam-and-observability"
  for_each = local.services

  name                   = "${var.name}-${each.key}"
  task_policy_statements = each.value.task_policy_statements
}

module "compute" {
  source   = "../modules/compute"
  for_each = local.services

  name                    = "${var.name}-${each.key}"
  aws_region              = var.aws_region
  private_subnet_ids      = module.networking.private_subnet_ids
  app_security_group_id   = each.value.app_security_group_id
  target_group_arn        = each.value.target_group_arn
  task_execution_role_arn = module.iam_and_observability[each.key].task_execution_role_arn
  task_role_arn           = module.iam_and_observability[each.key].task_role_arn
  ecr_repository_url      = module.iam_and_observability[each.key].ecr_repository_url
  image_tag               = var.image_tag
  log_group_name          = module.iam_and_observability[each.key].log_group_name
  environment_variables   = each.value.environment_variables
  desired_count           = coalesce(each.value.desired_count, 1)

  # Real resource/module reference, matching choreography's pattern -- ensures the ALB listener
  # exists before the ECS service tries to register against it. Harmless for inventory/responder,
  # which don't attach to the ALB at all.
  depends_on = [module.load_balancer]
}
