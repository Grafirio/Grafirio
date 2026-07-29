# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["nuget.config", "./"]
COPY ["src/services/Grafirio.Gateway/Grafirio.Gateway.csproj", "src/services/Grafirio.Gateway/"]

# GitHub Packages credentials for the "grafirio" NuGet feed (Grafirio.Shared.* packages).
ARG GITHUB_PACKAGES_USER
ARG GITHUB_PACKAGES_TOKEN
RUN if [ -n "$GITHUB_PACKAGES_USER" ]; then \
        dotnet nuget update source grafirio --username "$GITHUB_PACKAGES_USER" --password "$GITHUB_PACKAGES_TOKEN" --store-password-in-clear-text --configfile ./nuget.config; \
    fi

# Restore dependencies
RUN dotnet restore "src/services/Grafirio.Gateway/Grafirio.Gateway.csproj"

# Copy all source code
COPY . .

# Build the project
WORKDIR "/src/src/services/Grafirio.Gateway"
RUN dotnet build "Grafirio.Gateway.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Grafirio.Gateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5000
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Grafirio.Gateway.dll"]
