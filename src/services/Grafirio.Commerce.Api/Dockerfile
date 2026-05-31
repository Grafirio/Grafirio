# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Grafirio.sln", "./"]
COPY ["src/shared/Grafirio.Shared/Grafirio.Shared.csproj", "src/shared/Grafirio.Shared/"]
COPY ["src/services/Grafirio.Commerce.Api/Grafirio.Commerce.Api.csproj", "src/services/Grafirio.Commerce.Api/"]

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
