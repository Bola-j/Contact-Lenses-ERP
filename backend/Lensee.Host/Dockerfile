# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["backend/Lensee.Host/Lensee.Host.csproj", "backend/Lensee.Host/"]
COPY ["backend/Lensee.SharedKernel/Lensee.SharedKernel.csproj", "backend/Lensee.SharedKernel/"]
COPY ["backend/Lensee.Modules.Identity/Lensee.Modules.Identity.csproj", "backend/Lensee.Modules.Identity/"]
COPY ["backend/Lensee.Modules.Catalog/Lensee.Modules.Catalog.csproj", "backend/Lensee.Modules.Catalog/"]
COPY ["backend/Lensee.Modules.Inventory/Lensee.Modules.Inventory.csproj", "backend/Lensee.Modules.Inventory/"]
COPY ["backend/Lensee.Modules.CRM/Lensee.Modules.CRM.csproj", "backend/Lensee.Modules.CRM/"]
COPY ["backend/Lensee.Modules.Operations/Lensee.Modules.Operations.csproj", "backend/Lensee.Modules.Operations/"]
COPY ["backend/Lensee.Modules.Payments/Lensee.Modules.Payments.csproj", "backend/Lensee.Modules.Payments/"]
COPY ["backend/Lensee.Modules.Notifications/Lensee.Modules.Notifications.csproj", "backend/Lensee.Modules.Notifications/"]
COPY ["backend/Lensee.Modules.Reporting/Lensee.Modules.Reporting.csproj", "backend/Lensee.Modules.Reporting/"]
RUN dotnet restore "./backend/Lensee.Host/Lensee.Host.csproj"
COPY . .
WORKDIR "/src/backend/Lensee.Host"
RUN dotnet build "./Lensee.Host.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Lensee.Host.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Lensee.Host.dll"]