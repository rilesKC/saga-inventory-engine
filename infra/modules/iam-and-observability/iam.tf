data "aws_iam_policy_document" "ecs_assume_role" {
  statement {
    effect  = "Allow"
    actions = ["sts:AssumeRole"]

    principals {
      type        = "Service"
      identifiers = ["ecs-tasks.amazonaws.com"]
    }
  }
}

# Standard execution role: pulls the image from ECR, writes container logs to CloudWatch. Not
# application permissions -- that's the task role below.
resource "aws_iam_role" "task_execution" {
  name               = "${var.name}-task-execution"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume_role.json
}

resource "aws_iam_role_policy_attachment" "task_execution" {
  role       = aws_iam_role.task_execution.name
  policy_arn = "arn:aws:iam::aws:policy/service-role/AmazonECSTaskExecutionRolePolicy"
}

# Only created when the caller actually resolves a container secret -- most services here have
# none, and an empty inline policy document is invalid.
data "aws_iam_policy_document" "task_execution_extra" {
  count = length(var.task_execution_policy_statements) > 0 ? 1 : 0

  dynamic "statement" {
    for_each = var.task_execution_policy_statements

    content {
      effect    = "Allow"
      actions   = statement.value.actions
      resources = statement.value.resources
    }
  }
}

resource "aws_iam_role_policy" "task_execution_extra" {
  count = length(var.task_execution_policy_statements) > 0 ? 1 : 0

  name   = "${var.name}-task-execution-extra"
  role   = aws_iam_role.task_execution.id
  policy = data.aws_iam_policy_document.task_execution_extra[0].json
}

# Least-privilege application permissions, supplied by the caller as a generic list of statements
# -- not hardcoded to EventBridge+one-queue+one-table, since orchestration's three services each
# need a different combination of SQS send/receive/delete and DynamoDB permissions, and none of
# them need EventBridge at all.
resource "aws_iam_role" "task" {
  name               = "${var.name}-task"
  assume_role_policy = data.aws_iam_policy_document.ecs_assume_role.json
}

data "aws_iam_policy_document" "task_permissions" {
  dynamic "statement" {
    for_each = var.task_policy_statements

    content {
      effect    = "Allow"
      actions   = statement.value.actions
      resources = statement.value.resources
    }
  }
}

resource "aws_iam_role_policy" "task_permissions" {
  name   = "${var.name}-task-permissions"
  role   = aws_iam_role.task.id
  policy = data.aws_iam_policy_document.task_permissions.json
}
