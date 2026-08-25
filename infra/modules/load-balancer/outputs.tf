output "alb_dns_name" {
  value = aws_lb.this.dns_name
}

output "target_group_arn" {
  value = aws_lb_target_group.this.arn
}

output "app_security_group_id" {
  description = "Attach to the Fargate service (task 17) so it only accepts traffic from the ALB."
  value       = aws_security_group.app.id
}

output "listener_arn" {
  value = aws_lb_listener.http.arn
}
