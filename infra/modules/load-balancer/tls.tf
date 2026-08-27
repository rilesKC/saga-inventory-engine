# No custom domain exists for this project, so a real ACM-issued certificate isn't obtainable --
# ACM needs DNS or email validation against a domain you control, and this ALB is only ever
# addressed by its AWS-generated *.elb.amazonaws.com name. A self-signed cert still encrypts
# traffic in transit end-to-end, which is the property this fix actually needs, at the cost of a
# browser/client trust warning (it isn't issued by a publicly-trusted CA). Acceptable for a
# stack that's torn down after each real-deployment validation; a real domain + Route53 + an
# ACM-issued certificate is the production-grade upgrade path.

resource "tls_private_key" "alb" {
  algorithm = "RSA"
  rsa_bits  = 2048
}

resource "tls_self_signed_cert" "alb" {
  private_key_pem = tls_private_key.alb.private_key_pem

  subject {
    common_name  = "${var.name}.internal"
    organization = "saga-inventory-engine (self-signed -- no real domain)"
  }

  validity_period_hours = 24 * 30

  allowed_uses = [
    "key_encipherment",
    "digital_signature",
    "server_auth",
  ]
}

resource "aws_acm_certificate" "alb" {
  private_key      = tls_private_key.alb.private_key_pem
  certificate_body = tls_self_signed_cert.alb.cert_pem

  lifecycle {
    create_before_destroy = true
  }
}
