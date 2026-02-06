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

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "PrintO.dll"]
