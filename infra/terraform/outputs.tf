output "instance_id" {
  value = aws_instance.planevent.id
}

output "public_ip" {
  value = aws_instance.planevent.public_ip
}

output "public_dns" {
  value = aws_instance.planevent.public_dns
}
