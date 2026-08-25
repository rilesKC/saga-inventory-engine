output "alb_dns_name" {
  value = module.load_balancer.alb_dns_name
}

output "ecr_repository_url" {
  value = module.iam_and_observability.ecr_repository_url
}

output "queue_url" {
  value = module.messaging.queue_url
}

output "event_bus_name" {
  value = module.messaging.event_bus_name
}
