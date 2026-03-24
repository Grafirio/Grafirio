# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["src/shared/Grafirio.Shared/Grafirio.Shared.csproj", "src/shared/Grafirio.Shared/"]
COPY ["src/services/Grafirio.DataAnalysis.Api/Grafirio.DataAnalysis.Api.csproj", "src/services/Grafirio.DataAnalysis.Api/"]

# Restore dependencies
RUN dotnet restore "src/services/Grafirio.DataAnalysis.Api/Grafirio.DataAnalysis.Api.csproj"

# Copy all source code
COPY . .

# Build the project
WORKDIR "/src/src/services/Grafirio.DataAnalysis.Api"
RUN dotnet build "Grafirio.DataAnalysis.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Grafirio.DataAnalysis.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 5221
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Grafirio.DataAnalysis.Api.dll"]
