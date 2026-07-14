# ── Build stage ──────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and all project files first (layer cache friendly)
COPY lansee.sln .
COPY Lansee.Host/Lansee.Host.csproj                         Lansee.Host/
COPY Lansee.SharedKernel/Lansee.SharedKernel.csproj         Lansee.SharedKernel/
COPY Lansee.Modules.Identity/Lansee.Modules.Identity.csproj Lansee.Modules.Identity/
# ... repeat for each module .csproj

# Restore (cached unless .csproj files change)
RUN dotnet restore Lansee.Host/Lansee.Host.csproj

# Copy all source
COPY . .

# Publish
RUN dotnet publish Lansee.Host/Lansee.Host.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Runtime stage ─────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Non-root user for security
RUN adduser --disabled-password --gecos "" lansee
USER lansee

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "Lansee.Host.dll"]