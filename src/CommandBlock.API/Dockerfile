# syntax=docker/dockerfile:1.7

# ---------- Stage 1: frontend ----------
FROM oven/bun:1.3.14 AS frontend-build
WORKDIR /src/frontend
COPY src/CommandBlock.Frontend/package.json src/CommandBlock.Frontend/bun.lock ./
RUN bun install
COPY src/CommandBlock.Frontend .
RUN bun run build:agent --output-path /artifacts/frontend

# ---------- Stage 2: backend restore ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0.300 AS backend-deps
WORKDIR /src/backend
COPY ["src/CommandBlock.API/CommandBlock.API.csproj",                       "CommandBlock.API/"]
COPY ["src/CommandBlock.Application/CommandBlock.Application.csproj",       "CommandBlock.Application/"]
COPY ["src/CommandBlock.Infrastructure/CommandBlock.Infrastructure.csproj", "CommandBlock.Infrastructure/"]
COPY ["src/CommandBlock.Domain/CommandBlock.Domain.csproj",                 "CommandBlock.Domain/"]
COPY ["src/Directory.Build.props",                            "./"]
RUN dotnet restore "CommandBlock.API/CommandBlock.API.csproj"

# ---------- Stage 3: backend publish ----------
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY src .
# Directory.Build.props (at /src/backend) reads ..\application.properties to stamp the version - i.e.
# /src/application.properties. It lives at the repo root, not under src/, so copy it explicitly;
# without it the assembly version falls back to .NET's default 1.0.0 and the UI shows "v1.0.0".
COPY application.properties /src/application.properties
RUN dotnet publish "CommandBlock.API/CommandBlock.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# ---------- Stage 4: runtime (distroless Azure Linux) ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0.8-azurelinux3.0 AS final
WORKDIR /app
EXPOSE 8080
# The Minecraft hostname router listens here - one public port fronting every provisioned server.
EXPOSE 25565
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production

# Run as root so /var/run/docker.sock is accessible out of the box on every host
# (Docker Desktop on Windows/Mac exposes the socket as root:root 0660; Linux exposes
# it as root:docker 0660 with a host-specific GID). CommandBlock needs full Docker API access
# to provision sibling containers - dropping privilege inside the container while
# holding the socket gains nothing, since socket access is root-equivalent on the host
# either way. Override with `user: "<uid>:<gid>"` in compose if you have a different
# threat model and a known GID to map.
USER root

COPY --from=backend-build /app/publish .
COPY --from=frontend-build /artifacts/frontend/browser ./wwwroot

ENTRYPOINT ["dotnet", "CommandBlock.API.dll"]