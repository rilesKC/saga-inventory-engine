resource "aws_ecs_cluster" "this" {
  name = var.name
}

resource "aws_ecs_task_definition" "this" {
  family                   = var.name
  requires_compatibilities = ["FARGATE"]
  network_mode             = "awsvpc"
  cpu                      = var.cpu
  memory                   = var.memory
  execution_role_arn       = var.task_execution_role_arn
  task_role_arn            = var.task_role_arn

  container_definitions = jsonencode([
    {
      name      = var.name
      image     = "${var.ecr_repository_url}:${var.image_tag}"
      essential = true
      portMappings = var.target_group_arn == null ? [] : [
        {
          containerPort = var.app_port
          protocol      = "tcp"
        }
      ]
      environment = concat(
        [{ name = "AWS_REGION", value = var.aws_region }],
        var.environment_variables
      )
      logConfiguration = {
        logDriver = "awslogs"
        options = {
          "awslogs-group"         = var.log_group_name
          "awslogs-region"        = var.aws_region
          "awslogs-stream-prefix" = var.name
        }
      }
    }
  ])
}

resource "aws_ecs_service" "this" {
  name            = var.name
  cluster         = aws_ecs_cluster.this.id
  task_definition = aws_ecs_task_definition.this.arn
  launch_type     = "FARGATE"

  # Defaults to 1 -- most callers hold in-memory state that isn't safe to share across instances
  # yet (see the choreography plan's task 10 retro: a second instance would silently diverge from
  # the first the moment a saga's events got load-balanced across both). Callers with no such
  # state -- the orchestration stateless-responders service -- override this to run >=2 for real.
  desired_count = var.desired_count

  network_configuration {
    subnets         = var.private_subnet_ids
    security_groups = [var.app_security_group_id]
  }

  # Skipped entirely for a service with no target group (InventoryHost, ResponderHost) -- they
  # have no HTTP surface, so there's nothing for an ALB to route to.
  dynamic "load_balancer" {
    for_each = var.target_group_arn == null ? [] : [var.target_group_arn]

    content {
      target_group_arn = load_balancer.value
      container_name   = var.name
      container_port   = var.app_port
    }
  }

  # No depends_on here -- `depends_on` needs a real resource/module reference, not a string ARN
  # variable, so that dependency can only be expressed where both modules are actually
  # instantiated. A root module wiring this service behind an ALB must declare
  # `depends_on = [module.load_balancer]` on this module so the service isn't created before the
  # ALB has a live listener.
}
