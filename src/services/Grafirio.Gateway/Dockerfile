# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["Grafirio.sln", "./"]
COPY ["nuget.config", "./"]
COPY ["src/services/Grafirio.Gateway/Grafirio.Gateway.csproj", "src/services/Grafirio.Gateway/"]

# Grafirio.Shared.* paketleri ayri bir depoda: github.com/Grafirio/Packages.
#
# GitHub Packages'in NuGet feed'i, paket public olsa bile kimlik dogrulamasi
# istiyor ve CI'da calisan bir kimlik bulunamadi. Kaynak deposu public oldugu
# icin paketleri burada kaynagindan derleyip yerel bir feed olarak kullaniyoruz;
# boylece derleme hicbir token'a bagli kalmiyor.
# Hangi surumun derlenecegi. CI bunu shared-packages.ref dosyasindan
# --build-arg ile GECIYOR; buradaki "main" yalnizca elle derleme icin.
#
# Dal DEGIL commit sabitlemenin sebebi somut: CI yalnizca dosyasi degisen
# servisi yeniden derliyor, dolayisiyla "main" kullanildiginda Packages
# ilerledikce servisler birbirinden ayrisiyordu — biri yeni kutuphaneyle,
# otekiler eskisiyle. Ayrica bu depoda hicbir sey degismeden derleme
# kirilabiliyordu. Ayrintisi: shared-packages.md
ARG SHARED_PACKAGES_REF=main

# Tarball RUN degil ADD ile aliniyor. RUN satiri degismedigi icin Docker onu
# onbellekten kullaniyordu: Packages main ilerlese bile derleme eski
# paketlerle yapiliyor ve hata "kod bozuk" gibi gorunuyordu. CI temiz
# calistiricida kostugu icin orada hic ortaya cikmiyor, yalnizca yerelde
# isiriyor ve sessizce isiriyor.
#
# ADD uzak dosyayi her derlemede cekip icerigini karsilastiriyor: dal
# ilerlediginde sonraki katmanlarin onbellegi dusuyor, ilerlemediginde
# korunuyor. Commit bilgisini ayrica sorgulamak yerine dogrudan tarball
# kullaniliyor — tek indirme, ayrica GitHub API'nin kimliksiz saatlik
# limitine takilma riski yok (takilsaydi butun derlemeler patlardi).
ADD https://github.com/Grafirio/Packages/archive/${SHARED_PACKAGES_REF}.tar.gz /tmp/shared-packages.tar.gz

# Tarball, dal adindaki "/" karakterini "-" yaparak aciliyor: "feat/x" klasoru
# "Packages-feat-x" olarak cikiyor, yani klasor adi ref ile birebir ayni degil.
# Klasoru adiyla aramak yerine icerigi tek bir yere aciyoruz; boylece ref'in
# bicimi derlemeyi kirmiyor.
RUN mkdir -p /tmp/shared \
 && tar xzf /tmp/shared-packages.tar.gz -C /tmp/shared --strip-components=1 \
 && for csproj in /tmp/shared/src/*/*.csproj; do \
      dotnet pack "$csproj" -c Release -o /local-packages; \
    done \
 && dotnet nuget add source /local-packages --name local-packages --configfile ./nuget.config

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
