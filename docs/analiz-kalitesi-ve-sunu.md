# Genel analiz kuralları ve PDF sunusu

Analiz tuvalinin üst çubuğunda **Analiz kuralları** ve **PDF sunusu** bulunur.
Kullanıcıdan veritabanı alanlarını öğrenmesi veya tek tek metrik tanımlaması istenmez.
Analiz kuralları ekranı bilgi verir; tablo/kolon listesi indirmez ve ayar formu içermez.

## Ortak kurallar

- Tahmin aylık serilerde çalışır ve en az 12 aylık geçmiş gerektirir.
- İleri tahmin en fazla 12 aydır. Kullanıcı 5 yıl isterse 12 aylık tahmin üretilir;
  istenen ve uygulanan süre yeşil çerçeveli bir notla sonucun altında açıklanır.
- Not sonuçla birlikte saklanır; geçmişten açılınca ve PDF sunusunda korunur.
- Süre belirtilmezse 6 ay kullanılır. 12 ay ve daha kısa istekler için sınır notu yoktur.
- Eksik aylar sıfır kabul edilmez; tahmin durdurulur ve çözüm yolu gösterilir.
- Kimlik alanları tutar/miktar olarak toplanmaz. Birim bilinmiyorsa varsayılmaz.
- Tahminler keşif amaçlıdır; yaklaşık bantlar doğrulanmış %95 kapsama olarak gösterilmez.
  Geçmiş dönemlerde başarı ölçümü henüz eklenmemiştir.
- Alan bazlı özelleştirme şu aşamada kapalıdır. Eski metrik ayarları yeni genel
  kuralları değiştiremez; eski sonuçların hesaplama kayıtları değişmez.

Ortak kural tanımı `src/ai/ai-service-pycaret-engine/analysis_policy.json` içindedir.
Python motoru bu dosyayı okur; API aynı dosyayı gömülü kaynak olarak sunar.
Kural değiştiğinde CI hem motoru hem API'yi yeniden derler.

## Kullanım

1. Bağlantının seçili tablolarını analiz edip sözlüğü hazır hale getirin.
2. Tuvalde görmek istediğiniz sonucu kendi cümlelerinizle sorun.
3. Grafik veya tablonun altındaki açıklama kaynak, filtreler, yöntem, sütun
   anlamları, belirsizlikler ve uygulanan süre sınırını gösterir.
4. PDF sunusundan tamamlanmış analizleri seçip sıralayın ve önizleyin.
5. **PDF olarak kaydet / Yazdır** tarayıcının yazdırma penceresini açar.
   PDF hedefini seçin; başlık/altbilgileri kapatın. Çıktı yatay A4 hazırlanır.

PDF düğmesi her zaman açılır. Hazır analiz varsa düğmedeki sayı bunu gösterir;
yoksa ekran eski sonuç, çalışan analiz veya boş tuval için ne yapılması gerektiğini
açıklar. Boş sunu ekranı açıkken ilk sonuç tamamlanırsa listeye eklenir. Eski kayıtta
rapor sözleşmesi yoksa açıklama uydurulmaz; yeniden analiz gerekir.

## Sonuç ve çıktı güvenceleri

- Açıklama hesaplama izinden hazırlanır. Sonuç ve politika sürümü, SHA-256 özeti
  saklanır. İstatistiksel ilişki nedensellik sayılmaz.
- Motor aşamaları süreleriyle kaydeder. Ham sonuç tabloları teşhis günlüklerine
  kopyalanmaz; sorgu geçmişinde kalır.
- Sunu açılırken sonuçların kopyası alınır; hesap yeniden çalıştırılmaz.
  Başarısız takip sorusu önceki grafiğin sonuç kimliğini değiştirmez.
- Uzun tablolar satır ve sütun gruplarına bölünür. Grafikler yazdırma sırasında
  eksenlerinin kesilmemesi için görüntüye alınır.
- Ekrandaki tablo ilk 100 satırı açıkça belirtilerek gösterir. Sunu toplam 2.000
  tablo satırıyla sınırlıdır; büyük seçimler sessizce kesilmez.
- PowerPoint dosyası ve sunucu PDF üretimi yoktur; PDF tarayıcıdan alınır.

## Kontrol

API, Python ve ön yüz testleri; genel politika, uzun ufkun sınırlandırılması,
notların saklanması, eksik geçmiş/ay kontrolleri, takip kaydı ve sunu kopyasını
kapsar. `tests/fixtures/presentation.html` geliştirme sunucusunda grafik, Türkçe
karakterler, uzun tablo, yeşil süre notu ve bilgi ekranını gösterir.

Yeni SQL kolonları eklenmediği için veritabanı migration gerekmez.
