variable "name" {
  description = "Prefix used for naming every resource this module creates."
  type        = string
}

variable "max_receive_count" {
  description = "Number of delivery attempts before a message moves to the DLQ."
  type        = number
  default     = 3
}
