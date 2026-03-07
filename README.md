# PlaNEvent

PlaNEvent is a starter implementation of a calendar and occurrence booking platform using:

- Blazor WebAssembly frontend (`src/PlaNEvent.Client`)
- ASP.NET Core Web API backend (`src/PlaNEvent.Api`)
- Shared contracts (`src/PlaNEvent.Shared`)
- PostgreSQL for persistence

## Implemented foundations

- JWT-based login/registration/profile management
- Role model: `Admin`, `Standard`, `Customer`
- Admin URL and API for user management: `/admin/users` and `/api/admin/*`
- Calendar and occurrence management with multi-slot and multi-day scheduling
- Publish occurrences for public sale URL: `/sale/{ownerSlug}`
- Booking flow for published occurrences
- Swagger enabled at `/swagger`

## Local development

1. Restore and build:

```powershell
$env:APPDATA = (Resolve-Path .).Path
dotnet restore PlaNEvent.sln --configfile NuGet.Config
dotnet build src/PlaNEvent.Api/PlaNEvent.Api.csproj -c Release --no-restore
dotnet build src/PlaNEvent.Client/PlaNEvent.Client.csproj -c Release --no-restore
```

2. Run with Docker Compose:

```powershell
docker compose up --build
```

3. Access:

- Client: `http://localhost:8080`
- API: `http://localhost:5000`
- Swagger: `http://localhost:5000/swagger`
- Default admin: `admin@planevent.local` / `Admin123!`

## Terraform (EC2 t4g.small)

Terraform files are under `infra/terraform` and provision:

- `t4g.small` ARM instance
- Security group for ports `22`, `80`, `443`, `8080`, `5000`
- Bootstrap script installing Docker + Compose and running the app

Usage:

```powershell
cd infra/terraform
copy terraform.tfvars.example terraform.tfvars
terraform init
terraform apply
```

## CI/CD

GitHub Actions workflow: `.github/workflows/ci-cd.yml`

- Builds solution on push to `main`
- Deploys to EC2 using AWS SSM Run Command
- Requires secrets: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`, `EC2_INSTANCE_ID`

## Notes

- This is a production-ready scaffold with core use-case coverage and extension points.
- Additional hardening is still recommended: refresh tokens, email verification, rate limiting, migrations, HTTPS termination, payment integration, and observability.
