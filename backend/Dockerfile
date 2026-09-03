# Stage 1: Build backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy csproj files and restore as distinct layers
COPY backend/Delegat.csproj backend/
COPY sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj sdk/Delegat.Marketplace.SDK/
COPY plugins/Delegat.Marketplace.Ozon/Delegat.Marketplace.Ozon.csproj plugins/Delegat.Marketplace.Ozon/
COPY plugins/Delegat.Marketplace.Ozon/sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj plugins/Delegat.Marketplace.Ozon/sdk/Delegat.Marketplace.SDK/
COPY plugins/Delegat.Marketplace.Wildberries/Delegat.Marketplace.Wildberries.csproj plugins/Delegat.Marketplace.Wildberries/
COPY plugins/Delegat.Marketplace.Wildberries/sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj plugins/Delegat.Marketplace.Wildberries/sdk/Delegat.Marketplace.SDK/
COPY plugins/Delegat.Marketplace.OpenCart/Delegat.Marketplace.OpenCart.csproj plugins/Delegat.Marketplace.OpenCart/
COPY plugins/Delegat.Marketplace.OpenCart/sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj plugins/Delegat.Marketplace.OpenCart/sdk/Delegat.Marketplace.SDK/
COPY plugins/Delegat.Marketplace.YandexMarket/Delegat.Marketplace.YandexMarket.csproj plugins/Delegat.Marketplace.YandexMarket/
COPY plugins/Delegat.Marketplace.YandexMarket/sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj plugins/Delegat.Marketplace.YandexMarket/sdk/Delegat.Marketplace.SDK/
COPY plugins/Delegat.Marketplace.WooCommerce/Delegat.Marketplace.WooCommerce.csproj plugins/Delegat.Marketplace.WooCommerce/
COPY plugins/Delegat.Marketplace.WooCommerce/sdk/Delegat.Marketplace.SDK/Delegat.Marketplace.SDK.csproj plugins/Delegat.Marketplace.WooCommerce/sdk/Delegat.Marketplace.SDK/
RUN dotnet restore backend/Delegat.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.Ozon/Delegat.Marketplace.Ozon.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.Wildberries/Delegat.Marketplace.Wildberries.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.OpenCart/Delegat.Marketplace.OpenCart.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.YandexMarket/Delegat.Marketplace.YandexMarket.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.WooCommerce/Delegat.Marketplace.WooCommerce.csproj -a $TARGETARCH

# Copy all source files
COPY backend/ backend/
COPY sdk/ sdk/
COPY plugins/ plugins/
COPY VERSION VERSION

# Do not let host build artifacts overwrite Linux restore outputs.
RUN find backend sdk plugins -depth -type d \( -name bin -o -name obj -o -name .vs \) -exec rm -rf {} +

# Publish backend app (plugins are built separately for target arch below).
# Prefer build-arg APP_VERSION; otherwise bake root VERSION (never silent "dev").
ARG APP_VERSION=
RUN APP_VER="${APP_VERSION:-$(tr -d '[:space:]' < VERSION)}" \
 && dotnet publish backend/Delegat.csproj -a $TARGETARCH -c Release -o /app \
      /p:InformationalVersion=$APP_VER /p:BuildPlugins=false /p:PlaywrightPlatform=linux

# Build plugins and copy DLLs to app (plugins/Directory.Build.props points to top-level sdk)
RUN dotnet publish plugins/Delegat.Marketplace.Ozon/Delegat.Marketplace.Ozon.csproj -a $TARGETARCH -c Release -o /plugins-out/ozon
RUN dotnet publish plugins/Delegat.Marketplace.Wildberries/Delegat.Marketplace.Wildberries.csproj -a $TARGETARCH -c Release -o /plugins-out/wb
RUN dotnet publish plugins/Delegat.Marketplace.OpenCart/Delegat.Marketplace.OpenCart.csproj -a $TARGETARCH -c Release -o /plugins-out/opencart
RUN dotnet publish plugins/Delegat.Marketplace.YandexMarket/Delegat.Marketplace.YandexMarket.csproj -a $TARGETARCH -c Release -o /plugins-out/yandex
RUN dotnet publish plugins/Delegat.Marketplace.WooCommerce/Delegat.Marketplace.WooCommerce.csproj -a $TARGETARCH -c Release -o /plugins-out/woocommerce
RUN mkdir -p /app/plugins \
 && cp /plugins-out/ozon/Delegat.Marketplace.Ozon.dll /app/plugins/ \
 && cp /plugins-out/wb/Delegat.Marketplace.Wildberries.dll /app/plugins/ \
 && cp /plugins-out/opencart/Delegat.Marketplace.OpenCart.dll /app/plugins/ \
 && cp /plugins-out/yandex/Delegat.Marketplace.YandexMarket.dll /app/plugins/ \
 && cp /plugins-out/woocommerce/Delegat.Marketplace.WooCommerce.dll /app/plugins/

# Stage 2: Final image (Chromium for catalog scrape when unofficial HTTP is blocked)
FROM mcr.microsoft.com/playwright/dotnet:v1.61.0-noble
RUN apt-get update && apt-get install -y --no-install-recommends curl default-mysql-client && rm -rf /var/lib/apt/lists/*
EXPOSE 5000
WORKDIR /app
COPY --from=build --chown=1000:1000 /app .
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
USER 1000
ENTRYPOINT ["dotnet", "Delegat.dll"]