# Grafirio çalışma ortamı

## Doğrulama

`node tools/quality/plan.mjs` yerel takip edilen/takip edilmeyen değişiklikler için kontrol seçimini gösterir. Commit'ler arasını karşılaştırmak için `--base <tam-commit-SHA>`, tüm alanlar için `--all` kullanılır. Silinen ve yeniden adlandırılan dosyalar da git farkı üzerinden kapsanır.

`.github/workflows/quality.yml` PR'larda ve elle çalışır; mevcut dağıtım workflow'u aynı dosyayı çağırır. Container ve masaüstü yayın işleri quality başarılı olmadan başlamaz. Workflow'un depoya gönderilmesi gerekir; yerel dosya değişikliği GitHub'da aktif kontrol oluşturmaz. PR birleştirmesini de zorunlu kontrole bağlamak için repository ruleset içinde **Quality gate** durumunu ayrıca zorunlu seçin.

Seçim muhafazakârdır: .NET tarafı seçildiyse bütün yerel birim/sözleşme projeleri Windows üzerinde çalışır; analiz API/sorgu politikası değişikliklerinde Python da çalışır. Frontend uygulamaları klasöre göre seçilir; ortak tema bütün uygulamaları etkiler. Python test dosyaları ayrı süreçlerde çalışır, çünkü mevcut bazı testler modül taklitlerini `sys.modules` içine koyar. Mevcut firma paneli testlerinde Node'un `--experimental-test-module-mocks` seçeneği gerekir.

Ön yüz build kontrolü `.invalid` alan adlı sentetik HTTPS ayarlarıyla yapılır; bu aşama canlı servislere istek göndermez. ProjectAdmin'in mevcut `npm run lint` komutu CommonJS API dosyalarını tarayıcı ortamı olarak değerlendirdiği ve mevcut bir Fast Refresh kuralı ihlali bulunduğu için geçmiyor. Bu nedenle ilk zorunlu kapı build ve davranış testlerini kapsar; lint temizliği ve bağımlılık güvenlik güncellemeleri ayrı iştir. Bu sınırlama test sonucu gibi gizlenmemelidir.

CI .NET testleri üretimdekiyle aynı `shared-packages.ref` commit'inden ortak paket üretir; özel NuGet feed kimliği istemez ve kök `nuget.config` dosyasını değiştirmez. Bridge entegrasyon testleri, canlı model kontrolleri, benchmark yayınları ve müşteri SQL çalıştırmaları bu kapıya dahil değildir. `docs/result-validation.md` özel koşulları korunur.

Yerelde Python 3.12 ile ayrı sanal ortam oluşturun ve `tools/quality/requirements.txt` bağımlılıklarını kurun. Ardından o ortamın Python'u ile `tools/quality/run-python-tests.py` çalıştırın. CI hem üretim Docker imajındaki Python 3.11'i hem yerel test ortamındaki 3.12'yi kapsar. Bu hafif test ortamı tam PyCaret eğitim/tahmin ortamını doğrulamaz. `.quality/` klasörü yerel araçlar için Git dışında tutulur.

## Tarayıcı testleri

ProjectAdmin klasöründe:

```powershell
npm ci
npx playwright install chromium
npm run test:browser
```

Playwright sürümü package-lock ile sabittir. Ek bir MCP kurulumu gerekmez. Dört başlangıç senaryosu: oturumsuz erişim, firma rolüyle platform paneline erişim engeli, teşhis araması/boş sonuç ve API kesintisinin hata olarak görünmesi.

Test giriş noktası `e2e/index.html`, test Vite yapılandırması `e2e/vite.config.mjs` dosyasıdır. Gerçek uygulama bileşenleri yüklenir; kimlik sağlayıcı yalnızca bu ayrı test sunucusunda taklit edilir. Üretim main/index/config bu taklidi içe aktarmaz. Testler dış origin isteklerini engeller; API verileri sentetiktir. Bu senaryolar gerçek OIDC, API yetkilendirmesi, SQL sonucu veya tam uçtan uca entegrasyon kanıtı değildir.

Varsayılan test portu 4179'dur. Farklı çalışma için `$env:GRAFIRIO_BROWSER_PORT='...'` kullanın. Var olan sunucu yeniden kullanılmaz; dolu portta test durur. İz/görüntü yalnızca başarısız testlerde saklanır ve Git dışında kalır.

## Worktree ve test verisi izolasyonu

Mevcut uygun worktree'yi kullanın. Yeni worktree'de takip edilmeyen dosyalar, `.env`, node_modules veya Python ortamı otomatik taşınmaz. Proje rehberleri ve test altyapısı ilgili commit'te bulunmalıdır.

`docker-compose.yml` sabit isim/port kullanır; yalnızca farklı `-p` vermek yeterli değildir. Tam uygulama yığınını ikinci kez başlatmayın. Yeni `docker-compose.worktree.yml` dosyası **bağımsız sentetik test altyapısıdır**, ana compose dosyasına eklenen override değildir. PostgreSQL, Redis ve SQL Server sağlar; uygulama servisleri, Keycloak ve canlı bağlantılar taşımaz.

Depo/worktree kökünde:

```powershell
./scripts/New-WorktreeEnvironment.ps1
docker compose --env-file .worktree/test.env -f docker-compose.worktree.yml config --quiet
docker compose --env-file .worktree/test.env -f docker-compose.worktree.yml up -d
```

Hazırlayıcı script yalnızca Git dışında `.worktree/test.env` oluşturur; konteyner başlatmaz, veri taşımaz. Worktree yolundan türetilen farklı proje adı, proje kapsamlı volume'lar, rastgele yerel parola, `GrafirioEval_*` PostgreSQL veritabanı ve yalnızca loopback'te yayımlanan ayrı portlar kullanılır. Port doluysa dosya yazılmaz; `-Slot 42` gibi farklı bir slot seçin. Varsayılan slotların çakışma ihtimali vardır; başlangıç kontrolü o anda dinlenen portları denetler, sonradan başka süreç aynı portu alırsa Docker/test sunucusu durur.

Mevcut ortam dosyası yeniden yazılmaz: eski volume parolasıyla yeni parola arasında uyumsuzluk oluşmaz. Ana `.env` kopyalanmaz. `GRAFIRIO_TEST_*` veya `COMPOSE_PROJECT_NAME` değişkenlerini dışarıdan farklı değerlerle tanımlamayın; Compose ortam değişkenlerini env dosyasından önce kullanır. Kimlik bilgilerini rapora/komut geçmişine yapıştırmayın.

Konteynerleri durdurmak için aynı `--env-file` ve `-f` ile `stop` kullanın. Volume silen komutları otomatik temizlik olarak eklemeyin. Bu altyapı Bridge entegrasyon fixture'larının sabit adreslerini kendiliğinden değiştirmez. SQL Server `master` ile başlar; sonuç benchmark'ının ayrılmış READ_ONLY veritabanı, düşük yetkili kullanıcı ve onaylı fixture koşullarını ayrıca hazırlamak gerekir. Bu dosyanın SA kullanıcısı sonuç benchmark'ında kullanılamaz.

## Verimlilik pilotu

Henüz eski/yeni yöntem karşılaştırması yapılmadı. Pilot ölçümlerini `evals/development/pilot.csv` içinde tutun. Kod anlama, hata düzeltme, sözleşme değişikliği ve tekrar eden kod görevlerinden benzer zorlukta örnekler seçin. Aynı modeli, benzer ortamı ve aynı doğrulama kapsamını kullanın; aynı görevi tekrar çözmenin öğrenme avantajını kaydedin. Eski yönteme ait ölçüm yoksa sayı uydurmayın.

Görev süresi, müdahale, ilk doğrulama sonucu ve sonradan bulunan regresyonları değerlendirin. Token/maliyet bilgisi görev bazında erişilemiyorsa boş bırakın; hesap genelindeki kullanım yüzdesini görev maliyeti saymayın. Pilot için üretim verisi veya model benchmark'ı çalıştırılması gerekmez.
