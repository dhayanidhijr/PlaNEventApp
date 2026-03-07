FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY PlaNEvent.sln ./
COPY src/PlaNEvent.Shared/PlaNEvent.Shared.csproj src/PlaNEvent.Shared/
COPY src/PlaNEvent.Api/PlaNEvent.Api.csproj src/PlaNEvent.Api/
RUN dotnet restore src/PlaNEvent.Api/PlaNEvent.Api.csproj

COPY src/PlaNEvent.Shared/ src/PlaNEvent.Shared/
COPY src/PlaNEvent.Api/ src/PlaNEvent.Api/
RUN dotnet publish src/PlaNEvent.Api/PlaNEvent.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "PlaNEvent.Api.dll"]
