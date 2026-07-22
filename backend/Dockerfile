# Stage 1: Build backend
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
RUN dotnet restore backend/Delegat.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.Ozon/Delegat.Marketplace.Ozon.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.Wildberries/Delegat.Marketplace.Wildberries.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.OpenCart/Delegat.Marketplace.OpenCart.csproj -a $TARGETARCH
RUN dotnet restore plugins/Delegat.Marketplace.YandexMarket/Delegat.Marketplace.YandexMarket.csproj -a $TARGETARCH

# Copy all source files
COPY backend/ backend/
COPY sdk/ sdk/
COPY plugins/ plugins/

# Do not let host build artifacts overwrite Linux restore outputs.
RUN find backend sdk plugins -depth -type d \( -name bin -o -name obj -o -name .vs \) -exec rm -rf {} +

# Publish backend app (plugins are built separately for target arch below)
ARG APP_VERSION=dev
RUN dotnet publish backend/Delegat.csproj -a $TARGETARCH -c Release -o /app /p:InformationalVersion=$APP_VERSION /p:BuildPlugins=false

# Build plugins and copy DLLs to app (plugins/Directory.Build.props points to top-level sdk)
RUN dotnet publish plugins/Delegat.Marketplace.Ozon/Delegat.Marketplace.Ozon.csproj -a $TARGETARCH -c Release -o /plugins-out/ozon
RUN dotnet publish plugins/Delegat.Marketplace.Wildberries/Delegat.Marketplace.Wildberries.csproj -a $TARGETARCH -c Release -o /plugins-out/wb
RUN dotnet publish plugins/Delegat.Marketplace.OpenCart/Delegat.Marketplace.OpenCart.csproj -a $TARGETARCH -c Release -o /plugins-out/opencart
RUN dotnet publish plugins/Delegat.Marketplace.YandexMarket/Delegat.Marketplace.YandexMarket.csproj -a $TARGETARCH -c Release -o /plugins-out/yandex
RUN mkdir -p /app/plugins && cp /plugins-out/ozon/Delegat.Marketplace.Ozon.dll /app/plugins/ && cp /plugins-out/wb/Delegat.Marketplace.Wildberries.dll /app/plugins/ && cp /plugins-out/opencart/Delegat.Marketplace.OpenCart.dll /app/plugins/ && cp /plugins-out/yandex/Delegat.Marketplace.YandexMarket.dll /app/plugins/

# Stage 2: Final image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl default-mysql-client && rm -rf /var/lib/apt/lists/*
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Delegat.dll"]