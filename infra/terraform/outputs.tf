output "instance_id" {
  value = aws_instance.planevent.id
}

output "public_ip" {
  value = aws_instance.planevent.public_ip
}

output "public_dns" {
  value = aws_instance.planevent.public_dns
}

output "ec2_role_name" {
  value = aws_iam_role.planevent_ec2_role.name
}

output "agentcore_runtime_role_arn" {
  value = aws_iam_role.agentcore_runtime_role.arn
}

output "agentcore_runtime_exec_policy_arn" {
  value = aws_iam_policy.agentcore_runtime_exec.arn
}
