/* ─────────────────────────────────────────────────────────────
   Tuval yerleşimi — tarayıcıda saklanan görünüm tercihi

   Sorular ve sonuçlar sunucuda (`QueryHistory`) duruyor; tuval her
   açılışta oradan yeniden kuruluyor. Ama sunucu iki şeyi bilmiyor:

   1. Kullanıcının kartı nereye sürüklediği,
   2. Hangi kartı sildiği.

   Bunlar veri değil, o veriye bakış biçimi. Kaybolduğunda kaybedilen tek
   şey yerleşim — konuşmalar ve grafikler yerinde duruyor, yalnızca ilk
   hâllerine dönüyorlar.

   Kart kimlikleri konuşmanın kök sorgusundan türetildiği için (bkz.
   `restoreFromHistory`) aynı konuşma her açılışta aynı kimliği alıyor;
   yerleşim de bu yüzden yeniden yüklemeden sağ çıkıyor.

   Eskiden üç alan daha vardı — `parents`, `replacements`, `hiddenQueries`.
   Üçü de her turun tuvale ayrı düğümler bıraktığı modele aitti: hangi
   düğümün altına soru yazıldığı, hangi grafiğin hangi düzeltmeyle
   değiştiği, hangi sorgunun kendi dalını çizmediği. Konuşma artık kendi
   kartının içinde; bu soruların hiçbirinin karşılığı kalmadı.
───────────────────────────────────────────────────────────── */

const KEY_PREFIX = 'grafirio.canvas.layout.';

/**
 * `positions` — kart kimliği → sürüklenmiş konum
 * `hidden`    — silinmiş kart kimlikleri
 */
export const emptyLayout = () => ({
  positions: {},
  hidden: [],
});

const keyFor = (connectionId) => `${KEY_PREFIX}${connectionId}`;

export const loadLayout = (connectionId) => {
  if (!connectionId) return emptyLayout();
  try {
    const raw = window.localStorage.getItem(keyFor(connectionId));
    if (!raw) return emptyLayout();
    const parsed = JSON.parse(raw);
    return {
      positions: parsed?.positions ?? {},
      hidden: Array.isArray(parsed?.hidden) ? parsed.hidden : [],
    };
  } catch {
    // Bozuk ya da erişilemeyen depo yerleşimi sıfırlar, tuvali kilitlemez.
    return emptyLayout();
  }
};

export const saveLayout = (connectionId, layout) => {
  if (!connectionId) return;
  try {
    window.localStorage.setItem(keyFor(connectionId), JSON.stringify(layout));
  } catch {
    // Kota dolmuş ya da depo kapalı: yerleşim bu oturumda çalışır,
    // yeniden yüklemede sıfırlanır. Sessiz geçmek doğru — kullanıcının
    // sorduğu soruyla ilgisi olmayan bir hata.
  }
};

/**
 * Sunucudan kurulan tuvale kayıtlı yerleşimi uygular.
 *
 * Saf fonksiyon: silinmiş kartları atar, kaydedilmiş konumları yerleştirir.
 */
export const applyLayout = ({ nodes }, layout = emptyLayout()) => {
  const hidden = new Set(layout.hidden ?? []);
  const positions = layout.positions ?? {};

  const placed = nodes
    .filter((n) => !hidden.has(n.id))
    .map((n) => (positions[n.id] ? { ...n, position: { ...positions[n.id] } } : n));

  return { nodes: placed };
};
