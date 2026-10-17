# Grafirio çalışma ortamı

## Doğrulama

`node tools/quality/plan.mjs` yerel takip edilen/takip edilmeyen değişiklikler için kontrol seçimini gösterir. Commit'ler arasını karşılaştırmak için `--base <tam-commit-SHA>`, tüm alanlar için `--all` kullanılır. Silinen ve yeniden adlandırılan dosyalar da git farkı üzerinden kapsanır.

`.github/workflows/quality.yml` PR'larda ve elle çalışır; mevcut dağıtım workflow'u aynı dosyayı çağırır. Container ve masaüstü yayın işleri quality başarılı olmadan başlamaz. Workflow'un depoya gönderilmesi gerekir; yerel dosya değişikliği GitHub'da aktif kontrol oluşturmaz. PR birleştirmesini de zorunlu kontrole bağlamak için repository ruleset içinde **Quality gate** durumunu ayrıca zorunlu seçin.

Seçim muhafazakârdır: .NET tarafı seçildiyse bütün yerel birim/sözleşme projeleri Windows üzerinde çalışır; analiz API/sorgu politikası değişikliklerinde Python da çalışır. Frontend uygulamaları klasöre göre seçilir; ortak tema bütün uygulamaları etkiler. Python test dosyaları ayrı süreçlerde çalışır, çünkü mevcut bazı testler modül taklitlerini `sys.modules` içine koyar. Mevcut firma paneli testlerinde Node'un `--experimental-test-module-mocks` seçeneği gerekir.

Ön yüz build kontrolü `.invalid` alan adlı sentetik HTTPS ayarlarıyla yapılır; bu aşama canlı servislere istek göndermez. ProjectAdmin'in mevcut `npm run lint` komutu CommonJS API dosyalarını tarayıcı ortamı olarak değerlendirdiği ve mevcut bir Fast Refresh kuralı ihlali bulunduğu için geçmiyor. Bu nedenle ilk zorunlu kapı build ve davranış testlerini kapsar; lint temizliği ve bağımlılık güvenlik güncellemeleri ayrı iştir. Bu sınırlama test sonucu gibi gizlenmemelidir.

CI .NET testleri üretimdekiyle aynı `shared-packages.ref` commit'inden ortak paket üretir; özel NuGet feed kimliği istemez ve kök `nuget.config` dosyasını değiştirmez. Bridge entegrasyon testleri, canlı model kontrolleri, benchmark yayınları ve müşteri SQL çalıştırmaları bu kapıya dahil değildir. Ayrı sonuç doğrulama çalışması bu dala taşınmamıştır; mevcut ölçüm kapsamı `docs/olcum/README.md` içindedir.

Yerelde Python 3.12 ile ayrı sanal ortam oluşturun ve `tools/quality/requirements.txt` bağımlılıklarını kurun. Ardından o ortamın Python'u ile `tools/quality/run-python-tests.py` çalıştırın. CI hem üretim Docker imajındaki Python 3.11'i hem yerel test ortamındaki 3.12'yi kapsar. Bu hafif test ortamı tam PyCaret eğitim/tahmin ortamını doğrulamaz. `.quality/` klasörü yerel araçlar için Git dışında tutulur.

Analiz motoru kaynakları/bağımlılıkları veya kalite araçları değiştiğinde **Analysis production image** kontrolü ayrıca gerçek Dockerfile ile tam üretim imajını derler, `pip check` çalıştırır ve ağ erişimi kapalı konteynerde uygulama, PyCaret regresyon/zaman serisi ve veritabanı sürücülerini içe aktarır. İmaj registry'ye gönderilmez ve dağıtım yapılmaz; bu kontrol Quality gate'e dahildir. Canlı veri bağlantısı veya model servisi çağrısı yapmaz. PyCaret 3.3.2 için üretim Python sürümü 3.11'dir; 3.12 birim testlerinin geçmesi bütün ML ortamının o sürümde desteklendiğini göstermez. Dependabot Python imajının minor yükseltmelerini önermez; Python sürümü yükseltmesi ML bağımlılıklarıyla birlikte ayrı doğrulanmalıdır.

## Tarayıcı testleri

ProjectAdmin klasöründe:

```powershell
npm ci
npx playwright install chromium
npm run test:browser
```

Playwright sürümü package-lock ile sabittir. Ek bir MCP kurulumu gerekmez. Altı senaryo: oturumsuz erişim, firma rolüyle platform paneline erişim engeli, teşhis araması/boş sonuç, API kesintisinin hata olarak görünmesi, filtreyi koruyan sayfalama ve takip numarasıyla ayrıntı açma.

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

## Bağımlılık, gizli bilgi ve tekrar otomasyonu

İlk paket uygulama çalışma zamanına eklenmez: Hypothesis ve FsCheck test bağımlılıklarıdır; jscpd ayrı `tools/quality/package.json` altında; Gitleaks/actionlint yalnızca `.quality/bin` altına kurulur. İkili araç sürümleri ve Windows/Linux x64 SHA-256 değerleri `tools/quality/binaries.json` içinde sabittir. Kurucu yalnızca gereken çalıştırılabilir dosyayı çıkarır, bütün arşivi açmaz. Diğer mimariler henüz desteklenmez.

Yerelde, depo kökünden:

```powershell
# python yerine kalite sanal ortamının Python yolunu kullanabilirsiniz.
python tools/quality/install-binaries.py gitleaks actionlint
python tools/quality/scan-secrets.py
npm ci --ignore-scripts --prefix tools/quality
npm run duplicates --prefix tools/quality
```

Gitleaks varsayılan olarak yalnızca **stage edilmiş farkı** tarar; commit öncesi ilgili dosyaları `git add` ile seçin. Takip edilmeyen `.env` veya worktree sırlarını taramaz. Commit aralığı için `--base <tam-SHA>`, bütün commit geçmişi için `--history` kullanın. Bulgu çıkışı 1, araç/yapılandırma hatası 2'dir. Rapor yalnızca kural, dosya, satır, commit ve parmak izi alanlarını içerir; eşleşme metni/anahtar değeri ve ham scanner çıktısı rapora aktarılmaz. Bu maskeleme sentetik token içeren bağımsız Git depolarında test edilir. Bulgu, aktif bir anahtarın doğrulandığı anlamına gelmez; araç dış servislerde kimlik bilgisi denemez.

`repository-checks.yml`, kalite kapısı içinden her PR/dağıtım kontrolünde çağrılır. Yeni commit aralığındaki gizli bilgi bulguları ve workflow hataları kapıyı durdurur. Elle dağıtımda olayın başlangıç commit'i yoksa son commit farkı taranır. Haftalık salı 08:00 İstanbul koşusu ve doğrudan elle başlatılan bakım koşusu (varsayılan `history_scan=true`) bütün geçmişi tarar; eski bulgular da inceleme için başarısız sonuç oluşturabilir. Bu geçmiş taraması olağan PR kapısından ayrıdır. Ham bulgu değerleri hiçbir rapora konmaz. Tekrar tespiti mevcut tekrar oranı nedeniyle kapıyı durdurmaz; araç çalışamazsa başarısız olur. Bakım raporları 14 gün saklanır.

`jscpd` yalnızca `src` içinde çalışır; node_modules, bin/obj, dist, migration, tarayıcı fixture ve sıkıştırılmış çıktılar dışlanır. İlk yerel koşuda 23 tekrar adayı (satır bazında yaklaşık %1,10) bulundu. Bu değer hedef eşik veya onaylanmış refaktör listesi değildir. Temalar, test/örnek kodlar ve kasıtlı tekrarlar insan incelemesi gerektirebilir. Rapor `artifacts/quality/duplicates/jscpd-report.json` dosyasındadır.

`.github/dependabot.yml` npm, pip, Docker ve GitHub Actions için salı günleri haftalık PR planlar; otomatik merge yapmaz. npm/pip/Docker ana sürüm yükseltmeleri ve sayısal/ML Python paketlerinin yan sürüm yükseltmeleri bu ilk pakette otomatik önerilmez; bu sınırlamalar güvenlik güncellemesi kapsamını da etkileyebilir. GitHub Actions ana sürüm PR'ları açıktır. `Grafirio.*` paketleri ve `shared-packages.ref` olağan sürüm güncellemesi olarak değiştirilmez.

**NuGet botu ertelendi:** Kullanıcının tercihiyle NuGet güncelleme girdisi ve kullanılmayan registry secret referansları çıkarıldı. .NET birim/özellik testleri ve sabit ortak paket kaynağıyla CI doğrulaması devam eder. Bot ileride eklenecekse özel paket feed'ine ayrı, salt-okunur Dependabot kimliği hazırlanmalıdır. Config default branch'e ulaşmadan ve GitHub'ın Dependabot/Actions özellikleri açık olmadan diğer zamanlanmış işler aktif sayılmaz.

Hypothesis, Python'da 3 özellik için en fazla 200'er deterministik örnek; FsCheck, .NET'te 4 özellik için 200'er örnek üretir. Bağlı filtre değerlerinin ayrı kalması, ay başı sınırı, identifier kaçışı, allowlist ayrımı ve ikinci yazma ifadesinin reddi kontrol edilir. Bu sınırlı üretim kapsamı bütün olası girdilerin güvenli olduğunu kanıtlamaz. Var olan örnek testleri ve veritabanı yetki kontrolleri korunur.
