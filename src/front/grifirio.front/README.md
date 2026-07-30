# Grafirio — vitrin

`grafirio.com` adresinde yayınlanan tanıtım sayfası. Tek sayfa, sırayla: hero ve
kanvas temsili, kullananlar şeridi, üç adım, sohbetle analiz, özellikler,
fiyatlandırma, S.S.S. ve kapanış çağrısı.

Ürünün kendisi burada değil. Müşterinin kullandığı uygulama
`src/front/admins/grifirio.useradmin` altında; vitrin oraya yalnızca bağlantı
verir.

## Marka

Renkler, tipografi ve logo marka kılavuzundan geliyor; hepsi
`src/styles/vitrin.css` başındaki değişkenlerde toplu duruyor.

- Arayüz lacivert `#1C3F7C`, mürekkep `#101828`, zemin `#FBFAF8`, çizgi `#E6E3DE`
- Vurgular teal `#0E8F8C` ve sarı `#F8C630`
- Grafiklerde 12 basamaklı palet (`--c01`…`--c12`) sırayla tüketilir
- Poppins başlık ve kelime markası, Manrope gövde, IBM Plex Mono etiket ve sayı

Logo `components/GMark.jsx` içinde. Altı radyal bar eşit değil — dilim renkleri
ve uzunlukları kılavuzda "değiştirilmez" işaretli, o yüzden sabit. 64 px altında
`compact` sürüm devreye girer: beş eşit dilim, daha geniş ağız, daha kalın G
gövdesi. Küçükte altı dilimin kesme boşlukları kapanıp işaret lekeye dönüyordu.

## Neden bu kadar az bağımlılık var

Vitrin hiçbir API çağırmıyor, oturum açmıyor ve yönlendirme yapmıyor. React ve
React DOM dışında bir şeye ihtiyacı yok: logo ve grafikler inline SVG, geri
kalan her şey CSS. Bir tanıtım sayfasının açılış süresi satış argümanının
parçası, o yüzden paket listesi bilinçli olarak kısa tutuldu.

## Çalıştırma

```bash
npm install
npm run dev
```

Geliştirme sunucusu `http://localhost:5173` adresinde açılır. Ürün uygulaması
59264'ü kullandığı için ikisi aynı anda çalışabilir.

## Yapılandırma

| Değişken       | Varsayılan                   | Ne işe yarar                                    |
| -------------- | ---------------------------- | ----------------------------------------------- |
| `VITE_APP_URL` | `https://admin.grafirio.com` | "Giriş yap" ve "Ücretsiz başla" bağlantı hedefi |

Vite değişkenleri derleme anında gömülür; değeri Dockerfile'daki `ARG` üzerinden
ya da yerel `.env` dosyasından verin.

## Paketler

Vitrindeki paket adları (Deneme, Takım, Kurumsal) Identity'deki
`SubscriptionPlans` kodlarına karşılık gelir: `TRIAL`, `STANDARD`,
`ENTERPRISE`. Yeni bir paket eklenirse iki tarafın da güncellenmesi gerekir,
yoksa müşteri aldığını sandığı şeyi almamış olur.

Fiyatlar (`components/Pricing.jsx`) şu an yer tutucu.

## Yayına çıkmadan önce

`components/LogoStrip.jsx` içindeki şirket adları tasarım taslağından geldi ve
gerçek müşteri değil. "Kullananlar" başlığı altında uydurma referans yayınlamak
ziyaretçiye yanlış beyan olur: ya gerçek müşterilerle değiştirin ya da
`App.jsx`'ten `<LogoStrip />` satırını silin.
