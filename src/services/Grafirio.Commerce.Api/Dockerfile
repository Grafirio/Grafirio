# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Grafirio.sln", "./"]
COPY ["nuget.config", "./"]
COPY ["src/services/Grafirio.Commerce.Api/Grafirio.Commerce.Api.csproj", "src/services/Grafirio.Commerce.Api/"]

# Grafirio.Shared.* paketleri ayri bir depoda: github.com/Grafirio/Packages.
#
# GitHub Packages'in NuGet feed'i, paket public olsa bile kimlik dogrulamasi
# istiyor ve CI'da calisan bir kimlik bulunamadi. Kaynak deposu public oldugu
# icin paketleri burada kaynagindan derleyip yerel bir feed olarak kullaniyoruz;
# boylece derleme hicbir token'a bagli kalmiyor.
ARG SHARED_PACKAGES_REF=main
RUN curl -sSLf "https://github.com/Grafirio/Packages/archive/refs/heads/${SHARED_PACKAGES_REF}.tar.gz" \
      | tar xz -C /tmp \
 && for proj in Grafirio.Shared.Infrastructure Grafirio.Shared.Identity Grafirio.Contracts.AI; do \
      dotnet pack "/tmp/Packages-${SHARED_PACKAGES_REF}/src/${proj}/${proj}.csproj" \
        -c Release -o /local-packages; \
    done \
 && dotnet nuget add source /local-packages --name local-packages --configfile ./nuget.config
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
