terraform {
  required_version = ">= 1.6.0"

  required_providers {
    aws = {
      source  = "hashicorp/aws"
      version = "~> 5.0"
    }
  }
}

provider "aws" {
  region = var.aws_region
}

data "aws_vpc" "default" {
  default = true
}

data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}

data "aws_ssm_parameter" "ubuntu_arm64_ami" {
  name = "/aws/service/canonical/ubuntu/server/22.04/stable/current/arm64/hvm/ebs-gp2/ami-id"
}

resource "aws_iam_role" "planevent_ec2_role" {
  name = "${var.project_name}-ec2-role"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Action = "sts:AssumeRole"
        Effect = "Allow"
        Principal = {
          Service = "ec2.amazonaws.com"
        }
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "ssm_core" {
  role       = aws_iam_role.planevent_ec2_role.name
  policy_arn = "arn:aws:iam::aws:policy/AmazonSSMManagedInstanceCore"
}

resource "aws_iam_role_policy" "planevent_ec2_agentcore_invoke" {
  name = "PlaneventAgentCoreInvoke"
  role = aws_iam_role.planevent_ec2_role.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AllowInvokeSpecificAgentCoreRuntime"
        Effect = "Allow"
        Action = [
          "bedrock-agentcore:InvokeAgentRuntime"
        ]
        Resource = [
          var.agent_runtime_arn,
          var.agent_runtime_endpoint_arn
        ]
      }
    ]
  })
}

resource "aws_iam_role" "agentcore_runtime_role" {
  name = var.agentcore_runtime_role_name
  path = "/service-role/"

  assume_role_policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Sid    = "AssumeRolePolicy"
        Effect = "Allow"
        Principal = {
          Service = "bedrock-agentcore.amazonaws.com"
        }
        Action = "sts:AssumeRole"
        Condition = {
          StringEquals = {
            "aws:SourceAccount" = data.aws_caller_identity.current.account_id
          }
          ArnLike = {
            "aws:SourceArn" = "arn:aws:bedrock-agentcore:${var.aws_region}:${data.aws_caller_identity.current.account_id}:*"
          }
        }
      }
    ]
  })
}

resource "aws_iam_policy" "agentcore_runtime_exec" {
  name = var.agentcore_runtime_exec_policy_name
  path = "/service-role/"

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [
      {
        Effect = "Allow"
        Action = [
          "logs:DescribeLogStreams",
          "logs:CreateLogGroup"
        ]
        Resource = [
          "arn:aws:logs:${var.aws_region}:${data.aws_caller_identity.current.account_id}:log-group:/aws/bedrock-agentcore/runtimes/*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "logs:DescribeLogGroups"
        ]
        Resource = [
          "arn:aws:logs:${var.aws_region}:${data.aws_caller_identity.current.account_id}:log-group:*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "logs:CreateLogStream",
          "logs:PutLogEvents"
        ]
        Resource = [
          "arn:aws:logs:${var.aws_region}:${data.aws_caller_identity.current.account_id}:log-group:/aws/bedrock-agentcore/runtimes/*:log-stream:*"
        ]
      },
      {
        Effect = "Allow"
        Action = [
          "xray:PutTraceSegments",
          "xray:PutTelemetryRecords",
          "xray:GetSamplingRules",
          "xray:GetSamplingTargets"
        ]
        Resource = ["*"]
      },
      {
        Effect   = "Allow"
        Action   = "cloudwatch:PutMetricData"
        Resource = "*"
        Condition = {
          StringEquals = {
            "cloudwatch:namespace" = "bedrock-agentcore"
          }
        }
      },
      {
        Sid    = "BedrockModelInvocation"
        Effect = "Allow"
        Action = [
          "bedrock:InvokeModel",
          "bedrock:InvokeModelWithResponseStream"
        ]
        Resource = [
          "arn:aws:bedrock:*::foundation-model/*",
          "arn:aws:bedrock:${var.aws_region}:${data.aws_caller_identity.current.account_id}:*"
        ]
      }
    ]
  })
}

resource "aws_iam_role_policy_attachment" "agentcore_runtime_exec_attach" {
  role       = aws_iam_role.agentcore_runtime_role.name
  policy_arn = aws_iam_policy.agentcore_runtime_exec.arn
}

resource "aws_iam_instance_profile" "planevent" {
  name = "${var.project_name}-instance-profile"
  role = aws_iam_role.planevent_ec2_role.name
}

resource "aws_security_group" "planevent" {
  name        = "${var.project_name}-sg"
  description = "Security group for PlaNEvent EC2"
  vpc_id      = data.aws_vpc.default.id

  ingress {
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.ssh_cidr]
  }

  ingress {
    from_port   = 80
    to_port     = 80
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  ingress {
    from_port   = 443
    to_port     = 443
    protocol    = "tcp"
    cidr_blocks = ["0.0.0.0/0"]
  }

  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }

  tags = {
    Name = "${var.project_name}-sg"
  }
}

resource "aws_instance" "planevent" {
  ami                         = data.aws_ssm_parameter.ubuntu_arm64_ami.value
  instance_type               = "t4g.small"
  subnet_id                   = data.aws_subnets.default.ids[0]
  vpc_security_group_ids      = [aws_security_group.planevent.id]
  key_name                    = var.key_name
  associate_public_ip_address = true
  iam_instance_profile        = aws_iam_instance_profile.planevent.name

  metadata_options {
    http_endpoint               = "enabled"
    http_tokens                 = "optional"
    http_put_response_hop_limit = 2
    instance_metadata_tags      = "disabled"
  }

  user_data = templatefile("${path.module}/user_data.sh.tftpl", {
    repo_url   = var.repository_url
    branch     = var.repository_branch
    projectdir = var.project_directory
  })

  root_block_device {
    volume_size = 20
    volume_type = "gp3"
  }

  tags = {
    Name = var.project_name
  }
}

data "aws_caller_identity" "current" {}
