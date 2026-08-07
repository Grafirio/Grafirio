/**
 * Grafik teması — marka paleti ve mark ayarları.
 *
 * Chart.js canvas'a çizdiği için CSS değişkenlerini okuyamıyor; renkler
 * burada değişmez değer olarak duruyor. Palet keyfî seçilmedi: markanın
 * `--gf-c01…c12` rampasının tamamı kategorik palet olarak kullanılamıyor —
 * ardışık tonlar birbirine çok yakın (klasik gökkuşağı sorunu) ve renk körü
 * ayrımı ile gri-okunurluk kontrollerinden geçmiyor.
 *
 * Aşağıdaki iki set, rampadan seçilip doğrulayıcıdan geçirilmiş hâlleri:
 * açıklık bandı, doygunluk tabanı, komşu çift renk körü ayrımı (ΔE),
 * normal görü tabanı ve yüzeyle kontrast — altı kontrol de geçiyor.
 * Karanlık set otomatik ters çevirme değil; mavi ve mor basamakları koyu
 * zeminde 3:1'in altında kaldığı için ayrıca seçildi.
 */

/** Kimlik taşıyan seriler / dilimler için sabit sıralı kategorik palet. */
export const CATEGORICAL_LIGHT = [
  '#00a09a', // teal
  '#c9295f', // kırmızı-pembe
  '#3163b5', // mavi
  '#e4633c', // turuncu
  '#7a3d9e', // mor
  '#9c7a1e', // hardal
];

export const CATEGORICAL_DARK = [
  '#00a09a',
  '#c9295f',
  '#4a7cc9', // koyu zeminde açıldı
  '#e4633c',
  '#9152c4', // koyu zeminde açıldı
  '#9c7a1e',
];

/**
 * Tek serili büyüklük grafikleri tek renk kullanır: çubuğun uzunluğu değeri
 * zaten anlatıyor, her çubuğu farklı renge boyamak bilgi taşımayan bir süs
 * olur ve renk-sıralama ilişkisi kurulduğu izlenimini verir. Seçilen ton
 * logonun kendi çubuk rengi (navy); koyu zeminde açık karşılığı kullanılıyor.
 */
export const PRIMARY_LIGHT = '#1c3f7c';
export const PRIMARY_DARK = '#5285d1';

const LIGHT = {
  categorical: CATEGORICAL_LIGHT,
  primary: PRIMARY_LIGHT,
  ink: '#101828',
  muted: '#5a6474',
  grid: 'rgba(16,24,40,0.08)',
  surface: '#ffffff',
  tooltipBg: 'rgba(16,24,40,0.94)',
  tooltipInk: '#ffffff',
};

const DARK = {
  categorical: CATEGORICAL_DARK,
  primary: PRIMARY_DARK,
  ink: '#e7ecf2',
  muted: '#9aa7b5',
  grid: 'rgba(231,236,242,0.10)',
  surface: '#1a2026',
  tooltipBg: 'rgba(231,236,242,0.94)',
  tooltipInk: '#101828',
};

/**
 * Zemin gerçekten koyu mu?
 *
 * Kanvasın koyu görünmesi tek bir yerden gelmiyor olabilir (tema, tarayıcı
 * eklentisi, işletim sistemi ayarı). Bu yüzden tema adına değil, elemanın
 * hesaplanmış arka planının parlaklığına bakılıyor: nereden gelirse gelsin
 * doğru palet seçilir.
 */
export function resolveTheme(element) {
  if (typeof window === 'undefined' || !element) return LIGHT;

  let node = element;
  while (node && node !== document.documentElement) {
    const bg = getComputedStyle(node).backgroundColor;
    const match = bg && bg.match(/rgba?\(([^)]+)\)/);
    if (match) {
      const [r, g, b, a = '1'] = match[1].split(',').map((v) => parseFloat(v));
      // Saydam katmanlar bir şey söylemez; opak ilk zemine kadar yukarı çık.
      if (a > 0.5) {
        const luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255;
        return luminance < 0.5 ? DARK : LIGHT;
      }
    }
    node = node.parentElement;
  }

  return LIGHT;
}

/** Çubuk uçları logodaki yuvarlatılmış dikdörtgenlerle aynı karakterde. */
export const BAR_RADIUS = 4;

/** Bitişik çubuklar arasında zemin boşluğu — bloklar birbirine yapışmasın. */
export const BAR_PERCENTAGE = 0.72;
export const CATEGORY_PERCENTAGE = 0.78;
