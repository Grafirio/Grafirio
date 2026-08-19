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
# Tarball, dal adindaki "/" karakterini "-" yaparak aciliyor: "feat/x" klasoru
# "Packages-feat-x" olarak cikiyor, yani klasor adi ref ile birebir ayni degil.
# Klasoru adiyla aramak yerine icerigi tek bir yere aciyoruz; boylece ref'in
# bicimi derlemeyi kirmiyor.
RUN mkdir -p /tmp/shared \
 && curl -sSLf "https://github.com/Grafirio/Packages/archive/refs/heads/${SHARED_PACKAGES_REF}.tar.gz" \
      | tar xz -C /tmp/shared --strip-components=1 \
 && for csproj in /tmp/shared/src/*/*.csproj; do \
      dotnet pack "$csproj" -c Release -o /local-packages; \
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
