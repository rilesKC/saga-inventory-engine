resource "aws_security_group" "alb" {
  name        = "${var.name}-alb"
  description = "Allows inbound HTTP/HTTPS from the internet to the ALB. HTTP is a redirect-to-HTTPS listener only -- see aws_lb_listener.http."
  vpc_id      = var.vpc_id

  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}

# Attached to the Fargate service (task 17) -- only accepts traffic from the ALB, not the internet
# directly.
resource "aws_security_group" "app" {
  name        = "${var.name}-app"
  description = "Allows inbound traffic from the ALB only."
  vpc_id      = var.vpc_id

  ingress {
    from_port       = var.app_port
    to_port         = var.app_port
    protocol        = "tcp"
    security_groups = [aws_security_group.alb.id]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
}
