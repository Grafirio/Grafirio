# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["src/shared/Grafirio.Shared/Grafirio.Shared.csproj", "src/shared/Grafirio.Shared/"]
COPY ["src/services/Grafirio.Gateway/Grafirio.Gateway.csproj", "src/services/Grafirio.Gateway/"]

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
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 5000
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Grafirio.Gateway.dll"]
