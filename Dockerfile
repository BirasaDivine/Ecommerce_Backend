# syntax=docker/dockerfile:1

# ---- Build: restore and publish the API project only (the test project is never copied in) ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore against the csproj alone first so this layer is cached until dependencies change.
COPY dotnet-tools.json ./
COPY Ecommerce_Backend/Ecommerce_Backend.csproj Ecommerce_Backend/
RUN dotnet restore Ecommerce_Backend/Ecommerce_Backend.csproj && dotnet tool restore

COPY Ecommerce_Backend/ Ecommerce_Backend/
RUN dotnet publish Ecommerce_Backend/Ecommerce_Backend.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    -p:UseAppHost=false

# ---- Migration bundle: one self-contained executable that applies the EF Core migrations ----
FROM build AS migrations-build
ARG TARGETARCH
RUN case "$TARGETARCH" in arm64) RID=linux-arm64 ;; *) RID=linux-x64 ;; esac \
    && dotnet ef migrations bundle \
        --project Ecommerce_Backend/Ecommerce_Backend.csproj \
        --self-contained \
        --target-runtime "$RID" \
        --output /app/efbundle \
        --force

# ---- Migrator: runs the bundle once and exits (no SDK, no source, no app) ----
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrator
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp
USER $APP_UID
ENTRYPOINT ["./efbundle"]

# ---- Runtime: slim ASP.NET image, running as the image's built-in non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# The commit this image was built from; reported by /version (build with --build-arg COMMIT_SHA=<sha>).
ARG COMMIT_SHA=unknown
ENV Version__Commit=$COMMIT_SHA

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# $APP_UID is the unprivileged "app" user shipped with the .NET 8+ base images.
USER $APP_UID

ENTRYPOINT ["dotnet", "Ecommerce_Backend.dll"]
