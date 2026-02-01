FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy csproj and restore as distinct layers
COPY *.csproj .
RUN dotnet restore -a $TARGETARCH

# Copy and publish app and libraries
COPY . .
RUN dotnet publish -a $TARGETARCH -c Release -o /app

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
EXPOSE 5000
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "PrintO.dll"]