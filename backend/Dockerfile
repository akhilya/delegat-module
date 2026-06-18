# Stage 1: Build backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy csproj files and restore as distinct layers
COPY backend/PrintO.csproj backend/
COPY sdk/PrintO.Marketplace.SDK/PrintO.Marketplace.SDK.csproj sdk/PrintO.Marketplace.SDK/
COPY plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj plugins/PrintO.Marketplace.Ozon/
COPY plugins/PrintO.Marketplace.Wildberries/PrintO.Marketplace.Wildberries.csproj plugins/PrintO.Marketplace.Wildberries/
COPY plugins/PrintO.Marketplace.OpenCart/PrintO.Marketplace.OpenCart.csproj plugins/PrintO.Marketplace.OpenCart/
RUN dotnet restore backend/PrintO.csproj -a $TARGETARCH
RUN dotnet restore plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj -a $TARGETARCH
RUN dotnet restore plugins/PrintO.Marketplace.Wildberries/PrintO.Marketplace.Wildberries.csproj -a $TARGETARCH
RUN dotnet restore plugins/PrintO.Marketplace.OpenCart/PrintO.Marketplace.OpenCart.csproj -a $TARGETARCH

# Copy all source files
COPY backend/ backend/
COPY sdk/ sdk/
COPY plugins/ plugins/

# Publish backend app
RUN dotnet publish backend/PrintO.csproj -a $TARGETARCH -c Release -o /app

# Build plugins and copy DLLs to app
RUN dotnet publish plugins/PrintO.Marketplace.Ozon/PrintO.Marketplace.Ozon.csproj -a $TARGETARCH -c Release -o /plugins-out/ozon
RUN dotnet publish plugins/PrintO.Marketplace.Wildberries/PrintO.Marketplace.Wildberries.csproj -a $TARGETARCH -c Release -o /plugins-out/wb
RUN dotnet publish plugins/PrintO.Marketplace.OpenCart/PrintO.Marketplace.OpenCart.csproj -a $TARGETARCH -c Release -o /plugins-out/opencart
RUN mkdir -p /app/plugins && cp /plugins-out/ozon/PrintO.Marketplace.Ozon.dll /app/plugins/ && cp /plugins-out/wb/PrintO.Marketplace.Wildberries.dll /app/plugins/ && cp /plugins-out/opencart/PrintO.Marketplace.OpenCart.dll /app/plugins/

# Stage 2: Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "PrintO.dll"]
