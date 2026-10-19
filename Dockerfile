# LevBakehila API - .NET 8 image for Render (or any Docker host).
# Build from the repository root:  docker build -t levbakehila-api .
# All secrets (connection string, Jwt:Key, Seed passwords) are passed as environment variables at run time.

# ---------- Build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first, so "dotnet restore" is cached until a dependency changes
COPY LevBakehila.Core/LevBakehila.Core.csproj LevBakehila.Core/
COPY LevBakehila.Data/LevBakehila.Data.csproj LevBakehila.Data/
COPY LevBakehila.Service/LevBakehila.Service.csproj LevBakehila.Service/
COPY LevBakehila.API/LevBakehila.API.csproj LevBakehila.API/
RUN dotnet restore LevBakehila.API/LevBakehila.API.csproj

COPY LevBakehila.Core/ LevBakehila.Core/
COPY LevBakehila.Data/ LevBakehila.Data/
COPY LevBakehila.Service/ LevBakehila.Service/
COPY LevBakehila.API/ LevBakehila.API/
RUN dotnet publish LevBakehila.API/LevBakehila.API.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# ---------- Run ----------
# Debian-based aspnet image (not alpine / chiseled): it includes tzdata, needed for "Asia/Jerusalem" in the Seed
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# NLog also writes files to /app/logs; the non-root user needs write access there
RUN mkdir -p /app/logs && chown "$APP_UID" /app/logs
USER $APP_UID

# Kestrel listens on all interfaces, port 10000 - Render's default port for web services.
# If PORT is set (Render sets it), Program.cs listens on 0.0.0.0:$PORT instead, so both always agree.
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "LevBakehila.API.dll"]
