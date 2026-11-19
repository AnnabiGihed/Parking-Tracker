# syntax=docker/dockerfile:1
# Build context: the repository root.
#   docker build -f src/Containers/API/ParkingTracker.ApiService/Dockerfile \
#     --secret id=github_packages_user,env=GITHUB_PACKAGES_USER \
#     --secret id=github_packages_token,env=GITHUB_PACKAGES_TOKEN -t parking-tracker-api .
# The Pivot packages come from GitHub Packages; the credentials are BuildKit secrets, never a build argument or a layer.

# mcr.microsoft.com/dotnet/sdk:10.0
FROM mcr.microsoft.com/dotnet/sdk@sha256:bef136a371963aaa46ec97882845ef6f54dec15728bb4b128b1c2a400ffbc9b4 AS build
WORKDIR /src

# Package and project files first, so the restore layer survives a change of source.
COPY global.json nuget.config .editorconfig Directory.Build.props Directory.Packages.props stylecop.json ./
COPY src/Containers/API/ParkingTracker.ApiService/ParkingTracker.ApiService.csproj src/Containers/API/ParkingTracker.ApiService/
COPY src/Containers/Aspire/Defaults/ParkingTracker.ServiceDefaults/ParkingTracker.ServiceDefaults.csproj src/Containers/Aspire/Defaults/ParkingTracker.ServiceDefaults/
COPY src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore/ParkingTracker.Persistence.EntityFrameworkCore.csproj src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore/
RUN --mount=type=secret,id=github_packages_user,env=GITHUB_PACKAGES_USER \
    --mount=type=secret,id=github_packages_token,env=GITHUB_PACKAGES_TOKEN \
    dotnet restore src/Containers/API/ParkingTracker.ApiService/ParkingTracker.ApiService.csproj

COPY src/Containers/API/ParkingTracker.ApiService/ src/Containers/API/ParkingTracker.ApiService/
COPY src/Containers/Aspire/Defaults/ParkingTracker.ServiceDefaults/ src/Containers/Aspire/Defaults/ParkingTracker.ServiceDefaults/
COPY src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore/ src/Infrastructure/ParkingTracker.Persistence.EntityFrameworkCore/
RUN dotnet publish src/Containers/API/ParkingTracker.ApiService/ParkingTracker.ApiService.csproj \
    --no-restore -c Release -o /app/publish -p:UseAppHost=false

# mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra (ICU and time-zone data, no shell, no package manager)
FROM mcr.microsoft.com/dotnet/aspnet@sha256:cae5b962bfb786606def03d250c6a645cc98839095c89e806e4bd8978a68115b AS final
WORKDIR /app
COPY --from=build /app/publish ./

# Workstation GC and a conservative heap keep the process inside its memory budget on the shared server (ADR-0028).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    DOTNET_GCConserveMemory=7
EXPOSE 8080
# The non-root "app" user of the image (uid 1654).
USER 1654
ENTRYPOINT ["dotnet", "ParkingTracker.ApiService.dll"]
