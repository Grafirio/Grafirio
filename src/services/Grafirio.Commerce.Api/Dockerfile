# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# GitHub Packages credentials for the "grafirio" NuGet feed (Grafirio.Shared.* packages).
ARG GITHUB_PACKAGES_USER
ARG GITHUB_PACKAGES_TOKEN

COPY ["Grafirio.sln", "./"]
COPY ["nuget.config", "./"]
COPY ["src/services/Grafirio.Commerce.Api/Grafirio.Commerce.Api.csproj", "src/services/Grafirio.Commerce.Api/"]

RUN if [ -n "$GITHUB_PACKAGES_USER" ]; then \
        dotnet nuget update source grafirio --username "$GITHUB_PACKAGES_USER" --password "$GITHUB_PACKAGES_TOKEN" --store-password-in-clear-text --configfile ./nuget.config; \
    fi
RUN dotnet restore "src/services/Grafirio.Commerce.Api/Grafirio.Commerce.Api.csproj"

COPY . .

WORKDIR "/src/src/services/Grafirio.Commerce.Api"
RUN dotnet build "Grafirio.Commerce.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Grafirio.Commerce.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5300
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Grafirio.Commerce.Api.dll"]
