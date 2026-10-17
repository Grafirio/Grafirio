# Repo araçları — 17 Ekim 2026

## Eklenenler

| Araç | Sürüm / kapsam | Yerel sonuç |
| --- | --- | --- |
| Dependabot | npm, pip, Docker, GitHub Actions | Haftalık, sınırlı PR sayısı; otomatik merge yok; NuGet botu kullanıcı tercihiyle ertelendi |
| Gitleaks CLI | 8.30.1, SHA-256 sabit | Gerçek scanner ile 4 sentetik depo testi başarılı |
| actionlint | 1.7.12, SHA-256 sabit | Bütün workflow dosyaları başarılı |
| jscpd | 5.4.1, npm lock sabit | 23 tekrar adayı; satırların yaklaşık %1,10'u; kapıyı engellemeyen rapor |
| Hypothesis | 6.170.1 | 3 özellik, 200'er örnek; tüm Python koşusunda 167 test başarılı, ayrıca script/alt kontroller başarılı |
| FsCheck.Xunit | 3.4.0, mevcut xUnit 2 entegrasyonu | 4 özellik, 200'er örnek; QueryPolicy projesinde toplam 143 test başarılı |
| Playwright | Mevcut 1.64.1 | 6 sentetik tarayıcı senaryosu başarılı |
| Kontrol seçimi | Yerel Node script'i | 7 test başarılı |

Mevcut uygulama testleri ve kullanıcı değişiklikleri korunmuştur. Bu çalışma üretim kodunda davranış değişikliği yapmaz; yeni testler ve bakım altyapısı ekler. Yerel test sonuçları çalışma ağacını kapsar; GitHub üzerinde koşulmuş sonuç değildir.

## Geçmiş gizli bilgi bulguları

Bütün Git geçmişi taramasında **25 aday bulgu** çıktı: 11 JWT, 13 genel anahtar ve 1 GCP anahtarı kalıbı. Geçmiş `.env` kayıtları da kapsamdadır. Bunlar aktiflik doğrulaması veya 25 farklı güncel anahtar bulunduğu anlamına gelmez; aynı değer farklı commit'lerde sayılabilir, örnek/test değerleri de eşleşebilir. Bu çalışma hiçbir anahtarı dış serviste denemedi, döndürmedi veya silmedi.

İncelenecek metadata, `artifacts/quality/secrets-history.json` içindedir. Raporlarda yalnızca kural/dosya/satır/commit/parmak izi saklanır; anahtar/eşleşme metni çıkarılır. Gerçek ve halen geçerli bir kimlik bilgisi olduğu ayrıca doğrulanırsa ilgili sağlayıcıda iptal/yenileme yapılmalıdır; yalnızca dosyadan silmek Git geçmişini temizlemez.

Yeni PR'lar yalnızca kendi commit aralıkları için taranır. Haftalık geçmiş taraması eski bulguları da görünür kılar ve bulgu varsa başarısız sonuç üretir. Geçmiş bulguları otomatik olarak allowlist'e alınmadı.

## GitHub'da etkinleşme koşulları

- Değişikliklerin GitHub'a gönderilmesi ve zamanlanmış işler/Dependabot için default branch'e ulaşması gerekir. Bu çalışma push veya dağıtım yapmaz.
- NuGet botu kullanıcı tercihiyle ertelendi; eksik secret gerektiren yapılandırma bırakılmadı. .NET CI ve FsCheck testleri bu ertelemeden etkilenmez.
- CI testleri ortak paketleri kaynaktan üretmeye devam eder; CI için bu token eklenmez. `Grafirio.*` paket sürümleri ve `shared-packages.ref` otomatik güncellenmez.
- PR birleştirme kuralı için GitHub ruleset içinde Quality gate ayrıca zorunlu seçilmelidir; mevcut dağıtım işleri kalite kapısına bağlıdır.

Kullanım komutları ve kapsam sınırları: [çalışma ortamı rehberi](calisma-ortami.md).
