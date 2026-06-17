# ─── Stage 1: Restore ───
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /src

# Copy all .csproj files first (layer caching)
COPY Callu.Domain/Callu.Domain.csproj Callu.Domain/
COPY Callu.Shared/Callu.Shared.csproj Callu.Shared/
COPY Callu.Application/Callu.Application.csproj Callu.Application/
COPY Callu.Infrastructure/Callu.Infrastructure.csproj Callu.Infrastructure/
COPY Callu.Api/Callu.Api.csproj Callu.Api/
COPY Callu.Worker/Callu.Worker.csproj Callu.Worker/

# Restore only the API project graph (excludes Callu.Tests, which isn't in the image).
RUN dotnet restore Callu.Api/Callu.Api.csproj

# ─── Stage 2: Build & Publish ───
FROM restore AS build
COPY . .
WORKDIR /src/Callu.Api
RUN dotnet publish -c Release -o /app/publish --no-restore

# ─── Stage 3: Runtime ───
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install Kerberos library required by Npgsql for PostgreSQL GSSAPI authentication
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd --no-create-home --shell /bin/false appuser || true

COPY --from=build /app/publish .

# Create keys directory for DataProtection and logs directory for Serilog
RUN mkdir -p /app/keys /app/logs \
    && (chown appuser:appuser /app/keys /app/logs || true)

USER appuser

EXPOSE 5095

ENV ASPNETCORE_URLS=http://+:5095
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Callu.Api.dll"]
