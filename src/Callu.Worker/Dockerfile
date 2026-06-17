# ─── Stage 1: Restore ───
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

COPY Callu.Domain/Callu.Domain.csproj Callu.Domain/
COPY Callu.Shared/Callu.Shared.csproj Callu.Shared/
COPY Callu.Application/Callu.Application.csproj Callu.Application/
COPY Callu.Infrastructure/Callu.Infrastructure.csproj Callu.Infrastructure/
COPY Callu.Api/Callu.Api.csproj Callu.Api/
COPY Callu.Worker/Callu.Worker.csproj Callu.Worker/

# Restore only the Worker project graph (excludes Callu.Tests, which isn't in the image).
RUN dotnet restore Callu.Worker/Callu.Worker.csproj

# ─── Stage 2: Build & Publish ───
FROM restore AS build
COPY . .
WORKDIR /src/Callu.Worker
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Stage 3: Runtime ───
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl && rm -rf /var/lib/apt/lists/*

EXPOSE 8080

RUN useradd --no-create-home --shell /bin/false appuser || true

COPY --from=build /app/publish .

USER appuser

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Callu.Worker.dll"]
