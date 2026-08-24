variable "name" {
  description = "Prefix used for naming every resource this module creates."
  type        = string
}

variable "vpc_id" {
  type = string
}

variable "public_subnet_ids" {
  description = "The ALB itself must span multiple AZs -- required regardless of the Fargate service's own instance count."
  type        = list(string)
}

variable "app_port" {
  description = "Port the Host application listens on inside the container."
  type        = number
  default     = 8080
}
