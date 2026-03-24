# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["src/shared/Grafirio.Shared/Grafirio.Shared.csproj", "src/shared/Grafirio.Shared/"]
COPY ["src/services/Grafirio.Basket.Api/Grafirio.Basket.Api.csproj", "src/services/Grafirio.Basket.Api/"]

# Restore dependencies
RUN dotnet restore "src/services/Grafirio.Basket.Api/Grafirio.Basket.Api.csproj"

# Copy all source code
COPY . .

# Build the project
WORKDIR "/src/src/services/Grafirio.Basket.Api"
RUN dotnet build "Grafirio.Basket.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Grafirio.Basket.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
EXPOSE 5023
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Grafirio.Basket.Api.dll"]
