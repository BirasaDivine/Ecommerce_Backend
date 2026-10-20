# syntax=docker/dockerfile:1

# ---- Build: restore and publish the API project only (the test project is never copied in) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the csproj alone first so this layer is cached until dependencies change.
COPY Ecommerce_Backend/Ecommerce_Backend.csproj Ecommerce_Backend/
RUN dotnet restore Ecommerce_Backend/Ecommerce_Backend.csproj

COPY Ecommerce_Backend/ Ecommerce_Backend/
RUN dotnet publish Ecommerce_Backend/Ecommerce_Backend.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---- Runtime: slim ASP.NET image, running as the image's built-in non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# $APP_UID is the unprivileged "app" user shipped with the .NET 8+ base images.
USER $APP_UID

ENTRYPOINT ["dotnet", "Ecommerce_Backend.dll"]
