FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PlaNEvent.sln ./
COPY src/PlaNEvent.Shared/PlaNEvent.Shared.csproj src/PlaNEvent.Shared/
COPY src/PlaNEvent.Client/PlaNEvent.Client.csproj src/PlaNEvent.Client/
RUN dotnet restore src/PlaNEvent.Client/PlaNEvent.Client.csproj

COPY src/PlaNEvent.Shared/ src/PlaNEvent.Shared/
COPY src/PlaNEvent.Client/ src/PlaNEvent.Client/
RUN dotnet publish src/PlaNEvent.Client/PlaNEvent.Client.csproj -c Release -o /app/publish

FROM nginx:1.27-alpine AS runtime
COPY src/PlaNEvent.Client/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
EXPOSE 80
