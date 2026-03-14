variable "aws_region" {
  type        = string
  description = "AWS region"
  default     = "us-east-1"
}

variable "project_name" {
  type        = string
  description = "Project name for resources"
  default     = "planevent"
}

variable "ssh_cidr" {
  type        = string
  description = "CIDR block allowed to SSH"
  default     = "0.0.0.0/0"
}

variable "key_name" {
  type        = string
  description = "Optional EC2 key pair name"
  default     = null
}

variable "repository_url" {
  type        = string
  description = "Git repository clone URL"
}

variable "repository_branch" {
  type        = string
  description = "Branch to deploy"
  default     = "main"
}

variable "project_directory" {
  type        = string
  description = "Directory name on EC2"
  default     = "DaWinCalendarService"
}

variable "agent_runtime_arn" {
  type        = string
  description = "Bedrock AgentCore runtime ARN used by the application."
  default     = "arn:aws:bedrock-agentcore:us-east-1:352115226302:runtime/planeventsagedirect-e6Vq0UCkrk"
}

variable "agent_runtime_endpoint_arn" {
  type        = string
  description = "Bedrock AgentCore runtime endpoint ARN used by the application."
  default     = "arn:aws:bedrock-agentcore:us-east-1:352115226302:runtime/planeventsagedirect-e6Vq0UCkrk/runtime-endpoint/DEFAULT"
}

variable "agentcore_runtime_role_name" {
  type        = string
  description = "IAM role name used by Bedrock AgentCore runtime."
  default     = "AmazonBedrockAgentCoreRuntimeDefaultServiceRole-uo73l"
}

variable "agentcore_runtime_exec_policy_name" {
  type        = string
  description = "IAM policy name attached to AgentCore runtime execution role."
  default     = "AmazonBedrockAgentCoreRuntimeExecutionPolicy_ue1fix"
}
