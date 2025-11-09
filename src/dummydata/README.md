# Grafirio E-Commerce Test Database

Gelişmiş bir e-ticaret veritabanı yapısı ve dummy data generator projesi.

## 📦 Proje Yapısı

```
src/dummydata/
├── Grafirio.DummyData.Generator/     # .NET Console App
│   ├── Program.cs                     # Ana program
│   ├── DatabaseSchema.sql             # Tablo tanımları
│   └── Grafirio.DummyData.Generator.csproj
```

## 🗄️ Veritabanı Tabloları

### Core Tables (15 Adet)
1. **Categories** - Ürün kategorileri (hiyerarşik)
2. **Products** - Ürünler
3. **ProductImages** - Ürün görselleri (1:N)
4. **Customers** - Müşteriler
5. **Addresses** - Müşteri adresleri (1:N)
6. **Carts** - Sepetler
7. **CartItems** - Sepet kalemleri
8. **Orders** - Siparişler
9. **OrderItems** - Sipariş detayları
10. **Discounts** - İndirim/Kupon tanımları
11. **ProductDiscounts** - Ürün indirimleri (M:N)
12. **OrderDiscounts** - Sipariş indirimleri (M:N)
13. **Reviews** - Ürün yorumları
14. **Wishlists** - İstek listeleri
15. **Inventory** - Stok hareketleri (log)

## 🚀 Kurulum

### 1. Docker SQL Server Başlatma

```powershell
# Test SQL Server container'ını başlat
docker-compose -f docker-compose.test.yml up -d

# Container durumunu kontrol et
docker ps | findstr grafirio-sqlserver-test
```

**Connection String:**
```
Server=localhost,1434;Database=GrafirioECommerce;User Id=sa;Password=Test123!@#;TrustServerCertificate=True;
```

### 2. Database Schema Oluşturma

SQL Server Management Studio (SSMS) veya Azure Data Studio ile bağlanın:
- **Server:** localhost,1434
- **User:** sa
- **Password:** Test123!@#

`DatabaseSchema.sql` dosyasını çalıştırın.

### 3. Dummy Data Generator Çalıştırma

```powershell
cd src/dummydata/Grafirio.DummyData.Generator
dotnet run
```

## 📊 Veri Miktarları (Planlanan)

| Tablo | Kayıt Sayısı | Açıklama |
|-------|--------------|----------|
| Categories | ~20 | Ana + alt kategoriler |
| Products | ~200 | Çeşitli kategorilerde ürünler |
| ProductImages | ~500 | Her ürün 2-3 görsel |
| Customers | ~500 | Son 2 yıl kayıtları |
| Addresses | ~800 | Müşteri başına 1-2 adres |
| Orders | ~2000 | Son 12 ay siparişleri |
| OrderItems | ~5000 | Sipariş başına 1-5 ürün |
| Reviews | ~800 | Ürünlerin %40'ı yorumlu |
| Discounts | ~30 | Aktif/pasif kampanyalar |
| ProductDiscounts | ~100 | İndirimli ürünler |
| Wishlists | ~300 | Favori ürünler |
| Inventory | ~1000 | Stok giriş/çıkış logları |

**Toplam:** ~11.000+ kayıt

## 🔧 Teknolojiler

- **.NET 9** - Console Application
- **Bogus 35.6.4** - Faker library (gerçekçi veri)
- **Dapper 2.1.66** - Micro ORM (performans)
- **Microsoft.Data.SqlClient 6.1.2** - SQL Server driver
- **SQL Server 2022** - Docker container

## 📝 Özellikler

### Gelişmiş E-Ticaret Yapısı
- ✅ **Hiyerarşik kategoriler** (parent-child)
- ✅ **Multiple product images** (ana+alternatif)
- ✅ **Customer segmentation** (Regular/Premium/VIP)
- ✅ **Shopping cart** with abandoned tracking
- ✅ **Order lifecycle** (Pending→Processing→Shipped→Delivered)
- ✅ **Discount system** (product + order level)
- ✅ **Review system** with verification
- ✅ **Wishlist** functionality
- ✅ **Inventory tracking** (audit log)

### Realistic Data Patterns
- Zamana dayalı trendler (büyüme)
- Mevsimsel paternler (Aralık yoğun)
- Stok yönetimi (bazı ürünler tükendi)
- VIP müşteriler (yüksek harcama)
- Popüler ürünler (yüksek rating)

## 🎯 Test Senaryoları

Bu veritabanı şu analizler için uygundur:

1. **Satış Trendleri**
   - Aylık/günlük satış grafikleri
   - Kategori performansı
   - Best-seller ürünler

2. **Müşteri Analizi**
   - Segmentasyon (Regular/Premium/VIP)
   - Retention rate
   - Customer lifetime value

3. **Ürün Performansı**
   - Rating dağılımı
   - Review sentiment
   - Stok devir hızı

4. **Kampanya Etkinliği**
   - İndirim kullanım oranları
   - Kupon ROI
   - Abandoned cart recovery

## 🛠️ Bakım

### Container Yönetimi
```powershell
# Durdur
docker-compose -f docker-compose.test.yml stop

# Başlat
docker-compose -f docker-compose.test.yml start

# Tamamen kaldır (VERİLER SİLİNİR!)
docker-compose -f docker-compose.test.yml down -v

# Logları görüntüle
docker logs grafirio-sqlserver-test
```

### Veritabanını Sıfırlama
```sql
USE master;
DROP DATABASE GrafirioECommerce;
-- DatabaseSchema.sql'i tekrar çalıştır
```

## 📈 Sonraki Adımlar

1. ✅ Docker Compose hazır
2. ✅ .NET Proje oluşturuldu
3. ✅ Database schema hazır
4. ⏳ Dummy data logic (konuşulacak)
5. ⏳ Data generation (implementasyon)
6. ⏳ Test & validation

## 🤝 Katkı

Bu proje Grafirio AI Data Analysis platformunun test altyapısıdır.

---

**Not:** Bu test veritabanıdır. Production kullanımı için uygun değildir.
