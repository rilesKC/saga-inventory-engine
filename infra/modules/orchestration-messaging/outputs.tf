output "inventory_commands_queue_url" {
  value = aws_sqs_queue.this["inventory-commands"].url
}

output "inventory_commands_queue_arn" {
  value = aws_sqs_queue.this["inventory-commands"].arn
}

output "stateless_responder_commands_queue_url" {
  value = aws_sqs_queue.this["stateless-responder-commands"].url
}

output "stateless_responder_commands_queue_arn" {
  value = aws_sqs_queue.this["stateless-responder-commands"].arn
}

output "coordinator_inbound_queue_url" {
  value = aws_sqs_queue.this["coordinator-inbound"].url
}

output "coordinator_inbound_queue_arn" {
  value = aws_sqs_queue.this["coordinator-inbound"].arn
}
