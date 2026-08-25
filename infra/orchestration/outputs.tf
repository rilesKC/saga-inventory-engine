output "alb_dns_name" {
  value = module.load_balancer.alb_dns_name
}

output "coordinator_ecr_repository_url" {
  value = module.coordinator_iam_and_observability.ecr_repository_url
}

output "inventory_ecr_repository_url" {
  value = module.inventory_iam_and_observability.ecr_repository_url
}

output "responder_ecr_repository_url" {
  value = module.responder_iam_and_observability.ecr_repository_url
}

output "inventory_commands_queue_url" {
  value = module.orchestration_messaging.inventory_commands_queue_url
}

output "stateless_responder_commands_queue_url" {
  value = module.orchestration_messaging.stateless_responder_commands_queue_url
}

output "coordinator_inbound_queue_url" {
  value = module.orchestration_messaging.coordinator_inbound_queue_url
}

output "idempotency_table_name" {
  value = module.idempotency.table_name
}
