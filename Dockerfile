# Base images are pinned by digest, so a new upstream image cannot change the build unnoticed.
# The tag before the @ says which version the digest belongs to. Both digests are
# multi-architecture (amd64 and arm64).

# Build stage: the full SDK, never shipped. It runs on the machine's own architecture even
# when the image is built for another one; the published application is portable.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /source

# Restore first, from the project files only, so this layer is reused until a dependency changes.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Carts.Domain/Carts.Domain.csproj src/Carts.Domain/
COPY src/Carts.Api/Carts.Api.csproj src/Carts.Api/
RUN dotnet restore src/Carts.Api/Carts.Api.csproj

COPY src/ src/
RUN dotnet publish src/Carts.Api/Carts.Api.csproj \
    --configuration Release --no-restore --output /app -p:UseAppHost=false

# Runtime stage: the ASP.NET Core runtime and the published application, nothing else.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:21c3f9798416cba4c0576a947c286231cb71ea2884896cb968253f5c9668f930 AS runtime

WORKDIR /app
COPY --from=build /app ./

# The image names no environment, so it runs as Production unless it is told otherwise.
# APP_UID is the unprivileged user the .NET images provide.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Carts.Api.dll"]
