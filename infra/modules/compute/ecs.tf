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
      portMappings = [
        {
          containerPort = var.app_port
          protocol      = "tcp"
        }
      ]
      environment = [
        { name = "Sqs__QueueUrl", value = var.queue_url },
        { name = "EventBridge__BusName", value = var.event_bus_name },
        { name = "AWS_REGION", value = var.aws_region },
      ]
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

  # Desired count 1, not >=2 -- see the plan's task 10 retro. InventoryItem's in-memory state
  # isn't shared across instances yet; a second instance would silently diverge from the first the
  # moment a saga's events got load-balanced across both. Scoped down deliberately until a future
  # persistence spec makes multi-instance state safe. The VPC/subnets/ALB stay multi-AZ-capable
  # regardless, so raising this back to >=2 later is a one-line change, not a redesign.
  desired_count = 1

  network_configuration {
    subnets         = var.private_subnet_ids
    security_groups = [var.app_security_group_id]
  }

  load_balancer {
    target_group_arn = var.target_group_arn
    container_name   = var.name
    container_port   = var.app_port
  }

  # No depends_on here -- `depends_on` needs a real resource/module reference, not a string ARN
  # variable, so that dependency can only be expressed where both modules are actually
  # instantiated. Task 19's root module MUST declare `depends_on = [module.load_balancer]` on this
  # module so the service isn't created before the ALB has a live listener.
}
