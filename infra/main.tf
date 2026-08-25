module "networking" {
  source = "./modules/networking"

  name                 = var.name
  vpc_cidr             = var.vpc_cidr
  azs                  = var.azs
  public_subnet_cidrs  = var.public_subnet_cidrs
  private_subnet_cidrs = var.private_subnet_cidrs
}

module "messaging" {
  source = "./modules/messaging"

  name = var.name
}

module "idempotency" {
  source = "./modules/idempotency"

  # table_name intentionally not overridden -- its own default already matches
  # DynamoDbIdempotencyStore's hardcoded TableName constant exactly.
}

module "iam_and_observability" {
  source = "./modules/iam-and-observability"

  name = var.name
  task_policy_statements = [
    { actions = ["events:PutEvents"], resources = [module.messaging.event_bus_arn] },
    { actions = ["sqs:ReceiveMessage", "sqs:DeleteMessage", "sqs:GetQueueAttributes"], resources = [module.messaging.queue_arn] },
    { actions = ["dynamodb:PutItem"], resources = [module.idempotency.table_arn] },
  ]
}

module "load_balancer" {
  source = "./modules/load-balancer"

  name              = var.name
  vpc_id            = module.networking.vpc_id
  public_subnet_ids = module.networking.public_subnet_ids
}

module "compute" {
  source = "./modules/compute"

  name                    = var.name
  aws_region              = var.aws_region
  private_subnet_ids      = module.networking.private_subnet_ids
  app_security_group_id   = module.load_balancer.app_security_group_id
  target_group_arn        = module.load_balancer.target_group_arn
  task_execution_role_arn = module.iam_and_observability.task_execution_role_arn
  task_role_arn           = module.iam_and_observability.task_role_arn
  ecr_repository_url      = module.iam_and_observability.ecr_repository_url
  image_tag               = var.image_tag
  log_group_name          = module.iam_and_observability.log_group_name
  environment_variables = [
    { name = "Sqs__QueueUrl", value = module.messaging.queue_url },
    { name = "EventBridge__BusName", value = module.messaging.event_bus_name },
  ]

  # Real resource/module reference, unlike the string-ARN attempt task 17's retro flagged --
  # ensures the ALB listener exists before the ECS service tries to register against it.
  depends_on = [module.load_balancer]
}
