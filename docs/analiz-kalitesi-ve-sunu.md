# Analiz takibi, metrik kuralları ve PDF sunusu

Analiz tuvalinin üst çubuğunda **Metrik kuralları** ve **PDF sunusu** bulunur.
PDF sunusu düğmesi her zaman açılır. Hazır analiz varsa düğmedeki sayı bunu
gösterir; yoksa sunu ekranı eksik açıklama, çalışan analiz veya boş tuval için
ne yapılması gerektiğini söyler. Eski sonuçların açıklamaları için analiz yeniden
çalıştırılmalıdır. Boş sunu ekranı açıkken ilk sonuç tamamlanırsa listeye eklenir.

## Kullanım

1. Bağlantıyı analiz edip sözlüğü hazır hale getirin.
2. Metrik kurallarından alanı, iş adını, birimini ve izinli işlemleri seçin.
3. Tahmin gerekiyorsa tarih alanını, minimum geçmişi ve maksimum ufku tanımlayın.
   Varsayılan yeni kural 12 dönem geçmiş ve 6 ay ufuktur; tahmin izni ayrıca açılır.
4. Eksik dönemleri varsayılan olarak reddedin. Kayıt olmamasının gerçek sıfır olduğu
   doğrulanırsa sayım/toplam için sıfır doldurma seçilebilir. Ortalama boşlukları
   sıfırla doldurulmaz ve zaman aralığı sıkıştırılarak tahmin edilmez.
5. Analizi çalıştırın. Grafik veya tablonun altındaki açıklama kaynak, filtreler,
   yöntem, sütun anlamları ve sınırlamaları içerir.
6. PDF sunusundan tamamlanmış analizleri seçip sıralayın ve önizleyin.
   **PDF olarak kaydet / Yazdır** tarayıcının yazdırma penceresini açar.
   PDF hedefini seçin; başlık/altbilgileri kapatın. Çıktı yatay A4 olarak hazırlanır.

## Davranış ve güvence

- Kurallar şirket/bağlantı kapsamında saklanır; değiştirmek veri kaynağı güncelleme
  yetkisi gerektirir. Sözlük değişmişse eski düzenleme kaydedilmez.
- Yeniden şema analizinde geçerli kurallar taşınır. Artık geçerli olmayan kurallar
  kaldırılır ve metrik ekranında yeniden tanımlama uyarısı gösterilir.
- Kuralları tanımlanmamış ölçülerde mevcut hesaplamalar açıklamalı uyarı taşır.
  Tahmin için kayıtlı ve tahmine izin verilen bir metrik zorunludur.
- Açıklama çalıştırılan hesaplamanın izinden hazırlanır; LLM açıklaması yöntemin
  kanıtı sayılmaz. Sonuç sürümü ve SHA-256 özeti sonuçla birlikte saklanır.
- Motor doğrulama, veri okuma, sonuç ve hata aşamalarını süreleriyle kaydeder.
  API takip kaydı tamamlanma/iptal/hata durumuna güncellenir. Ham sonuç tabloları
  teşhis günlüklerine kopyalanmaz; asıl sonuç mevcut sorgu geçmişinde kalır.
- Sunu açılırken sonuçların kopyası alınır. Hesap tekrar çalıştırılmaz.
  Başarısız takip sorusu önceki grafiğin sonuç kimliğini değiştirmez.
- Uzun tablolar satır ve sütun gruplarına bölünür; ilk tanımlayıcı sütun tekrarlanır.
  Grafikler yazdırma sırasında boyut değişiminden kesilmemesi için görüntüye alınır.
- Ekrandaki ayrıntı tablosu ilk 100 satırı açıkça belirtilerek gösterir. Sunu seçimi
  toplam 2.000 tablo satırıyla sınırlıdır; daha büyük çıktı sessizce kesilmez,
  kullanıcıdan analizi filtrelemesi veya daha az sonuç seçmesi istenir.
- Eski kayıtlarda yeni rapor sözleşmesi yoksa açıklama uydurulmaz; yeniden analiz
  gerekir. Bu kayıtlar yeni sunu seçimine alınmaz.

## Mevcut sınırlar

Tahmin başarısını geçmiş dönemlerde ölçen doğrulama bu değişikliğin kapsamında
değildir. Tahminler açıkça keşif amaçlı gösterilir. Yaklaşık bantlar doğrulanmış
%95 tahmin aralığı olarak etiketlenmez. İstatistiksel ilişkiler nedensellik sayılmaz.
PowerPoint dosyası ve otomatik sunucu PDF üretimi eklenmemiştir; PDF tarayıcı
yazdırma akışıyla alınır.

## Kontrol

- `tests/Grafirio.DataAnalysis.Tests`: kural doğrulaması, mevcut API akışları ve
  takip kaydının tamamlanması; ham tabloların teşhis kaydına taşınmaması.
- Python motor testleri: eksik ay, ufuk/geçmiş sınırı, birden fazla ölçü ve
  nitelenmiş alanlarla kural atlanmaması, açıklamaların kaynağı.
- Ön yüz testleri: raporun sonuç/geçmiş boyunca korunması, sunu kopyasının
  değişmemesi, geniş ve uzun tabloların hücre kaybetmeden bölünmesi.
- `tests/fixtures/presentation.html`: geliştirme sunucusunda örnek sunu;
  grafik, Türkçe karakterler, 40 satır/8 sütun tablo ve yazdırma görünümünün kontrolü.

Yeni SQL kolonları eklenmediği için bu değişiklik veritabanı migration gerektirmez.
