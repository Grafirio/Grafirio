# Grafirio veritabanınıza nasıl bağlanır?

Bu sayfa BT ve güvenlik ekipleri için yazıldı. Amacı tek bir soruyu net
cevaplamak: **Grafirio verimize nasıl erişecek ve bu ne kadar güvenli?**

Dört seçenek var. Çoğu kurumda doğru cevap birincisi.

---

## 1. Grafirio Bridge (önerilen)

Kendi sunucunuzda çalışan küçük bir Windows servisi. Veritabanına **o**
bağlanır, biz değil.

| Soru | Cevap |
| --- | --- |
| Firewall'da hangi portu açacağız? | **Hiçbirini.** Yalnızca giden 443 gerekiyor — zaten açık. |
| Bağlantıyı kim başlatır? | Sizin sunucunuz. Grafirio sizin ağınıza hiç bağlanmaz, bağlanamaz. |
| Veritabanı şifresi nerede duruyor? | Sizin diskinizde, Windows DPAPI ile ve **makineye bağlı** olarak. Dosya başka bir makineye kopyalansa çözülemez. |
| Buluttan gelen bir sorgu verimizi değiştirebilir mi? | Hayır. Bridge yalnızca `SELECT`/`WITH` kabul eder ve bu kontrolü **sizin makinenizde** yapar. `DROP`, `DELETE`, `UPDATE`, hatta `SELECT ... INTO` reddedilir. |
| Hangi tablolara erişilir? | İzin listesi tanımlarsanız yalnızca onlara. Listede olmayan bir tabloya giden sorgu, bulut ne gönderirse göndersin reddedilir. |
| Ne yapıldığını nereden görürüz? | `audit.tsv` — her sorgu, zamanı, dönen satır sayısı. Sizin diskinizde, bizim erişimimiz olmadan. |
| Ne kadar veri çıkıyor? | Yalnızca sorgunun sonucu. Satır tavanı sizin tarafınızda da uygulanıyor. |
| Servis ne kadar yer kaplar? | Tek bir `.exe`. .NET runtime kurmanız gerekmez. |

**Kurulum:** servis açılışta ekranda kısa bir kod gösterir, kuran kişi onu
tarayıcıda kendi hesabıyla onaylar (OAuth device flow — bir TV'ye hesap
tanıtmak gibi). Taşınacak bir token yok.
Ayrıntı: [`src/bridge/README.md`](../src/bridge/README.md).

Aynı yaklaşımı Postman (Desktop Agent), Microsoft (Power BI On-premises Data
Gateway, Azure Self-hosted Integration Runtime) ve Cloudflare (Tunnel)
kullanıyor.

---

## 2. Doğrudan bağlantı + IP izin listesi

Veritabanınız zaten dışarıdan erişilebiliyorsa (örneğin Azure SQL) ve
firewall'da bizim IP'lerimize izin vermeyi tercih ediyorsanız.

- Açılacak port: **1433, gelen yönde.**
- Şifre bizde şifreli olarak saklanır (AES-256, anahtar sizin ortamınıza özel).
- Kurulum gerekmez, en hızlı seçenek.

**Dikkat:** bulut altyapımız Azure Container Apps üzerinde ve çıkış IP'miz
varsayılan olarak sabit değil. Bu seçenek, bizim tarafımızda VNet entegrasyonu
ve NAT Gateway kurulmasını gerektirir — yani bir hazırlık süresi var. Ayrıca
güvenlik ekiplerinin çoğu 1433'ü dışarı açmayı onaylamıyor.

---

## 3. Site-to-site VPN

İki ağ arasında IPsec tüneli.

- Her iki tarafın ağ ekibi gerekir.
- IP çakışması, tünel bakımı ve kesinti yönetimi ayrı bir iş.
- Kurumsal müşteriler için kabul edilebilir, ama her müşteri için ayrı tünel
  operasyonel olarak ölçeklenmiyor.

Bridge'in yapmadığı bir şeyi VPN de yapmıyor; ikisi arasındaki fark, VPN'in
**ağ seviyesinde** güven kurması, Bridge'in ise yalnızca tek bir uygulamaya ve
yalnızca okuma yetkisi vermesi. Güvenlik açısından Bridge daha dar bir kapı.

---

## 4. Tam yerinde (on-premises) kurulum

Grafirio'nun tamamı sizin sunucularınızda çalışır. Veri hiçbir zaman ağınızdan
çıkmaz.

- `docker-compose.yml` ile kurulur.
- Yapay zekâ özellikleri bir LLM sağlayıcısına erişim gerektirir; tamamen
  kapalı bir ağda bu kısım devre dışı kalır.
- Güncellemeler sizin planınıza göre yapılır.

En büyük ve en muhafazakâr kurumlar için.

---

## Karşılaştırma

| | Bridge | Doğrudan | VPN | Yerinde |
| --- | --- | --- | --- | --- |
| Firewall'da port açılır mı | Hayır | Evet (1433) | Evet (IPsec) | Hayır |
| Şifre nerede | Sizde | Bizde (şifreli) | Bizde (şifreli) | Sizde |
| Veri nereye gider | Sorgu sonucu buluta | Sorgu sonucu buluta | Sorgu sonucu buluta | Hiçbir yere |
| Kurulum yükü | Tek `.exe` | Yok | Ağ ekibi | Tam kurulum |
| Okuma zorlaması sizde mi | **Evet** | Hayır | Hayır | — |

---

## Sık sorulanlar

**Bridge sunucumuza uzaktan erişim mi sağlıyor?**
Hayır. Bridge yalnızca kendisine gönderilen okuma sorgularını çalıştırır ve
sonucu döndürür. Uzaktan komut çalıştırma, dosya erişimi ya da başka bir
yetenek yoktur.

**Şifreyi panele girersek buluta gitmiş olmuyor mu?**
Panelden girilen şifre kurulu TLS kanalından bridge'e iner ve orada kalıcı
olarak saklanır; bulutta kalıcı bir kopyası tutulmaz. Bu bile fazla geliyorsa
şifreyi hiç panele girmeden, doğrudan bridge'in yerel yapılandırmasına
yazabilirsiniz — panel yalnızca bağlantının adını görür.

**Bridge çökerse ne olur?**
Analizler durur ve panelde bağlantı "Çevrimdışı" görünür; sessizce yanlış
sonuç üretilmez. Aynı şirkete birden fazla bridge tanımlayarak yedeklilik
sağlayabilirsiniz.

**Sorguları önceden görebilir miyiz?**
Evet. Çalışan her sorgu `audit.tsv` dosyasına yazılır. İsterseniz izin
listesiyle hangi tablolara erişilebileceğini de önceden sınırlarsınız.

**Bridge kendi kendine güncelleniyor mu?**
Hayır. Güncellemeyi siz yaparsınız. Sunucu, uyumsuz bir sürümle karşılaşırsa
açık bir mesaj verir; sessizce yanlış çalışmaz.
