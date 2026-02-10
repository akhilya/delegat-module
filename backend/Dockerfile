# Stage 1: Build frontend
FROM node:20-alpine AS frontend-build
WORKDIR /app
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
# No VITE_API_SERVER = relative URLs (same origin in production)
RUN npm run build

# Stage 2: Build backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy csproj files and restore as distinct layers
COPY backend/PrintO.csproj backend/
COPY sdk/PrintO.Marketplace.SDK/PrintO.Marketplace.SDK.csproj sdk/PrintO.Marketplace.SDK/
COPY plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj plugins/PrintO.Marketplace.Ozon/
RUN dotnet restore backend/PrintO.csproj -a $TARGETARCH
RUN dotnet restore plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj -a $TARGETARCH

# Copy all source files
COPY backend/ backend/
COPY sdk/ sdk/
COPY plugins/ plugins/

# Publish backend app
RUN dotnet publish backend/PrintO.csproj -a $TARGETARCH -c Release -o /app

# Build plugins and copy DLLs to app
RUN dotnet publish plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj -a $TARGETARCH -c Release -o /plugins-out
RUN mkdir -p /app/plugins && cp /plugins-out/PrintO.Marketplace.Ozon.dll /app/plugins/

# Stage 3: Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
COPY --from=frontend-build /app/dist ./wwwroot
USER $APP_UID
ENTRYPOINT ["dotnet", "PrintO.dll"]
