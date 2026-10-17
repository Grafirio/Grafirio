# Grafirio geliştirme ve öğrenme haritası

17 Ekim 2026 tarihli ilk depo incelemesi. Bu belge tam mimari denetimi veya başarılı test raporu değildir. Kaynak dosyalar ve çalıştırma yapılandırması değiştikçe ilgili bölümü güncelleyin.

Otomatik kalite kontrolleri, tarayıcı testleri, worktree test ortamı ve verimlilik pilotu için [çalışma ortamı rehberi](calisma-ortami.md) kullanılır.

## Nereden başlanır?

| Alan | Başlangıç noktası | İncelenecek ilişki |
| --- | --- | --- |
| Ana ön yüz | `src/front/grifirio.front` | React sayfası → istemci → gateway rotası |
| Firma paneli | `src/front/admins/grifirio.useradmin` | Oturum, bağlantı seçimi, analiz sunumu, polling |
| Yönetim paneli | `src/front/admins/grifirio.projectadmin` | Teşhis ve semantik ölçüm ekranları → admin API |
| Gateway | `src/services/Grafirio.Gateway` | Dış rota, yönlendirme, kimlik ve başlıklar |
| Kimlik | `src/services/Grafirio.Identity.Api` ve `keycloak` | Firma erişimi ve rol/sözleşme ilişkileri |
| Commerce | `src/services/Grafirio.Commerce.Api` | Gerçek endpoint ve bağımlılıklarını kaynakta doğrulayın |
| Analiz API | `src/services/Grafirio.DataAnalysis.Api/Program.cs` | DI kayıtları, endpoint eşleme, veri kaynakları, telemetri |
| Soru akışı | `Features/Agent/AgentQueryEndpoints.cs` ve `Infrastructure/Services/PyCaretQueryService.cs` (Analiz API içinde) | Soru kabulü, çeviri, iş durumu ve sonuç; dalları kaynakta takip edin |
| Python motoru | `src/ai/ai-service-pycaret-engine/main.py` | İç API → query_spec/data_port/agent_jobs ve analiz modülleri |
| Müşteri veri erişimi | `src/bridge/README.md`, `src/bridge`, Analiz API `Data/Access` | Doğrudan bağlantı/Bridge seçimi, SignalR ve yerel yürütme |
| Ortak SQL kuralları | `src/shared/Grafirio.QueryPolicy` | İzinli sorgular ve tablo/erişim doğrulama |
| Gözlemlenebilirlik | `src/shared/Grafirio.Telemetry`, Analiz API `Infrastructure/Telemetry` | İstek kimliği, teşhis kaydı, metrik ve log ilişkisi |
| Ölçüm | `tools/Grafirio.Measurement`, `tools/Grafirio.SemanticEval`, `evals` | Ölçüm seti, beklenen sonuç ve rapor kapsamı |

Bir akışı anlatırken bu tabloyu çağrı zinciri yerine kullanmayın. UI çağrısını, gateway rotasını, endpoint'i, servis metodunu ve sonraki sınırı ayrı ayrı doğrulayın. İptal, timeout, tekrar deneme ve çevrimdışı Bridge davranışını ilgili koddan çıkarın.

## Test seçme haritası

Aşağıdaki komutlar başlangıç örnekleridir; bu belge hazırlanırken çalıştırılmadı. Önce proje bağımlılıklarını ve test dosyalarının ortam koşullarını okuyun. Komutlar aksi belirtilmedikçe depo kökünden çalıştırılır.

| Değişiklik | Başlangıç kontrolü |
| --- | --- |
| Analiz API | `dotnet test tests/Grafirio.DataAnalysis.Tests/Grafirio.DataAnalysis.Tests.csproj` |
| SQL politikası | `dotnet test tests/Grafirio.QueryPolicy.Tests/Grafirio.QueryPolicy.Tests.csproj`; ardından etkilenen API/Bridge testleri |
| Bridge | `tests/Grafirio.Bridge.Tests`, `tests/Grafirio.Bridge.Desktop.Tests`; entegrasyon ortamı uygunsa `tests/Grafirio.Bridge.IntegrationTests` |
| Gateway / Identity | `tests/Grafirio.Gateway.Tests` / `tests/Grafirio.Identity.Tests` içindeki proje ile `dotnet test` |
| Ölçüm / semantik değerlendirme | `tests/Grafirio.Measurement.Tests` / `tests/Grafirio.SemanticEval.Tests` içindeki proje ile `dotnet test` |
| Admin panelleri saf JS davranışı | İlgili panel klasöründe `node --experimental-test-module-mocks --test tests/*.test.js` |
| Firma paneli desktop auth | Firma paneli klasöründe `node --test src/auth/desktop/desktopAuth.test.js` |
| React bileşenleri | İlgili uygulama klasöründe `npm run lint`, `npm run build`; etkilenen kullanıcı akışını tarayıcıda doğrulama |
| Python sorgu kuralları | Motor klasöründe `python test_query_spec.py`; dosya kendi çalıştırıcısını içerir |
| Diğer Python davranışları | Test içeriği ve `requirements-test.txt` doğrulandıktan sonra motor klasöründe `python -m pytest <ilgili-test-dosyası>`; bazı dosyaların özel çalıştırıcısı olabilir |

`npm test` bu iki admin panelinin package.json dosyalarında tanımlı değil. Yeni test altyapısı eklemeden önce mevcut `node:test` örneklerini kullanın. Tüm .NET çözümünü veya Python testlerini koşulsuz çalıştırmak yerine dış bağımlılık gereksinimlerini inceleyin.

### Analiz doğruluğunun katmanları

- Birim/sözleşme testleri: yerel davranış ve servisler arası veri biçimleri.
- Teşhis örnekleri: soru çevirisinin beklenen parametre/sözlük kapsamına uygunluğu. `tools/run-diagnostic-tests.mjs` gateway ve platform admin token ister; ağ/model çağrıları ve uzak test koşusu kayıtları oluşturabilir. Çalıştırmadan önce hedef ortamı ve görev yetkisini doğrulayın.
- Sonuç benchmark'ı: hazır aday SQL sonucunun onaylanmış sonuçla karşılaştırılması. SQL üretme başarısını ölçmez. Ayrı sentetik, READ_ONLY test ortamı ve diğer korumalar için `docs/result-validation.md` bağlayıcı başlangıç kaynağıdır. Otomatik yayın yapmayın.

## Yeniden kullanım için mevcut örnekler

| İhtiyaç | Önce incelenecek örnek |
| --- | --- |
| Yeni analiz endpoint'i | `Features/Analysis/AnalysisEndpoints.cs` ve `Features/Agent/AgentQueryEndpoints.cs`: yetki, sahiplik, iptal ve hata dönüşü |
| Yeni veri erişimi | `IDataSourceFactory`, `Data/Access`, `Grafirio.QueryPolicy`: Bridge/doğrudan bağlantı ayrımını çağırana taşımama |
| Yarışan panel istekleri | `src/front/admins/grifirio.projectadmin/src/utils/diagnosticLoader.js` ve testi: eski yanıtların yeni durumu ezmesini önleme |
| Python sorgu davranışı | `query_spec.py` ve `test_query_spec.py`: toplulaştırma, sıralama, parametreleme ve join kuralları |
| Teşhis kaydı | `Infrastructure/Telemetry/QueryDiagnostics.cs` ve `docs/query-diagnostics.md` |

Bunlar kopyalanacak hazır şablonlar değildir. Bir örneği uyarlamadan önce mevcut görevle ortak davranışını ve farklı yetki/veri koşullarını belirleyin. Gerçek tekrar ortaya çıktığında küçük ortak yardımcı veya üretici ekleyin.

## Nasıl istemde bulunabilirim?

- “$grafirio-akis-anlat kullanarak SubmitQuery akışını, hata yollarını ve testlerini dosya bağlantılarıyla anlat.”
- “$grafirio-hata-coz kullanarak şu takip numarasındaki hatayı incele; nedenini doğrula, düzelt ve ilgili testi çalıştır.”
- “$grafirio-degisiklik-kontrol kullanarak bu değişikliğin Bridge, Python, firma yetkileri ve testlere etkisini incele.”

Skill adları mevcut oturumda listelenmiyorsa `.agents/skills/<skill-adı>/SKILL.md` yolunu belirterek aynı isteği verin. Yeni sohbetler proje rehberini okuyabilir; bütün kodun kalıcı olarak öğrenildiği varsayılmaz.
