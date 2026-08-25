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

module "coordinator_iam_and_observability" {
  source = "../modules/iam-and-observability"

  name = "${var.name}-coordinator"
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
  ]
}

module "inventory_iam_and_observability" {
  source = "../modules/iam-and-observability"

  name = "${var.name}-inventory"
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
  ]
}

module "responder_iam_and_observability" {
  source = "../modules/iam-and-observability"

  name = "${var.name}-responder"
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
}

module "coordinator_compute" {
  source = "../modules/compute"

  name                    = "${var.name}-coordinator"
  aws_region              = var.aws_region
  private_subnet_ids      = module.networking.private_subnet_ids
  app_security_group_id   = module.load_balancer.app_security_group_id
  target_group_arn        = module.load_balancer.target_group_arn
  task_execution_role_arn = module.coordinator_iam_and_observability.task_execution_role_arn
  task_role_arn           = module.coordinator_iam_and_observability.task_role_arn
  ecr_repository_url      = module.coordinator_iam_and_observability.ecr_repository_url
  image_tag               = var.image_tag
  log_group_name          = module.coordinator_iam_and_observability.log_group_name
  environment_variables = [
    { name = "Sqs__InventoryCommandsQueueUrl", value = module.orchestration_messaging.inventory_commands_queue_url },
    { name = "Sqs__StatelessResponderCommandsQueueUrl", value = module.orchestration_messaging.stateless_responder_commands_queue_url },
    { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
    { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
  ]

  # Real resource/module reference, matching choreography's pattern -- ensures the ALB listener
  # exists before the ECS service tries to register against it.
  depends_on = [module.load_balancer]
}

module "inventory_compute" {
  source = "../modules/compute"

  name                    = "${var.name}-inventory"
  aws_region              = var.aws_region
  private_subnet_ids      = module.networking.private_subnet_ids
  app_security_group_id   = aws_security_group.background_worker.id
  task_execution_role_arn = module.inventory_iam_and_observability.task_execution_role_arn
  task_role_arn           = module.inventory_iam_and_observability.task_role_arn
  ecr_repository_url      = module.inventory_iam_and_observability.ecr_repository_url
  image_tag               = var.image_tag
  log_group_name          = module.inventory_iam_and_observability.log_group_name
  environment_variables = [
    { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
    { name = "Sqs__InventoryCommandsQueueUrl", value = module.orchestration_messaging.inventory_commands_queue_url },
    { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
  ]
}

module "responder_compute" {
  source = "../modules/compute"

  name                    = "${var.name}-responder"
  aws_region              = var.aws_region
  private_subnet_ids      = module.networking.private_subnet_ids
  app_security_group_id   = aws_security_group.background_worker.id
  task_execution_role_arn = module.responder_iam_and_observability.task_execution_role_arn
  task_role_arn           = module.responder_iam_and_observability.task_role_arn
  ecr_repository_url      = module.responder_iam_and_observability.ecr_repository_url
  image_tag               = var.image_tag
  log_group_name          = module.responder_iam_and_observability.log_group_name
  environment_variables = [
    { name = "Sqs__CoordinatorInboundQueueUrl", value = module.orchestration_messaging.coordinator_inbound_queue_url },
    { name = "Sqs__StatelessResponderCommandsQueueUrl", value = module.orchestration_messaging.stateless_responder_commands_queue_url },
    { name = "Dynamo__IdempotencyTableName", value = module.idempotency.table_name },
  ]
}
