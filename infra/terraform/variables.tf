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
