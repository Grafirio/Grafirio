# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["nuget.config", "./"]
COPY ["src/services/Grafirio.DataAnalysis.Api/Grafirio.DataAnalysis.Api.csproj", "src/services/Grafirio.DataAnalysis.Api/"]
# Bridge protokolu proje referansi: restore, referans verilen csproj'u da
# gormek zorunda. Eksik olsaydi "restore" asamasi duserdi ve sebebi
# Dockerfile'a bakmadan anlasilmazdi.
COPY ["src/bridge/Grafirio.Bridge.Contracts/Grafirio.Bridge.Contracts.csproj", "src/bridge/Grafirio.Bridge.Contracts/"]

# Grafirio.Shared.* ve Grafirio.Contracts.AI paketleri ayri bir depoda:
# github.com/Grafirio/Packages.
#
# GitHub Packages'in NuGet feed'i, paket public olsa bile kimlik dogrulamasi
# istiyor ve CI'da calisan bir kimlik bulunamadi. Kaynak deposu public oldugu
# icin paketleri burada kaynagindan derleyip yerel bir feed olarak kullaniyoruz;
# boylece derleme hicbir token'a bagli kalmiyor.
# GECICI: Grafirio/Packages#1 main'e merge edilene kadar paketler o PR'in
# dalindan uretiliyor. #49 master'a girdiginde PR henuz aciktı ve derleme
# Grafirio.Shared.Identity.Permissions ad alanini bulamiyordu.
#
# PR merge edilir edilmez bu satir "main" e donmeli; donmezse master, baska
# bir deponun feature dalina bagli kalir ve o dal silindiginde sessizce
# kirilir. Dal, geri alinana kadar silinmemeli.
ARG SHARED_PACKAGES_REF=feat/shared-permissions
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
