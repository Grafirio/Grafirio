/* ─────────────────────────────────────────────────────────────
   Tuval yerleşimi — tarayıcıda saklanan görünüm tercihi

   Sorular ve sonuçlar sunucuda (`QueryHistory`) duruyor; tuval her
   açılışta oradan yeniden kuruluyor. Ama sunucu iki şeyi bilmiyor:

   1. Kullanıcının düğümü nereye sürüklediği,
   2. Hangi düğümü sildiği.

   Bunlar veri değil, o veriye bakış biçimi. Kaybolduğunda kaybedilen tek şey
   yerleşim — sorular ve grafikler yerinde duruyor, yalnızca ilk hâllerine
   dönüyorlar.

   Üçüncü bir madde vardı: hangi sorunun hangi sorunun devamı olarak
   sorulduğu. O artık sunucuda (`QueryHistory.ParentQueryId`) — konuşmanın
   zinciri yerleşim tercihi değil, verinin kendisi. Buradaki `parents` kaydı
   yine de duruyor ve sunucudan gelen bağı EZİYOR: sunucu hangi TURUN devamı
   olduğunu biliyor, yerel kayıt hangi DÜĞÜMÜN altına yazıldığını — ikincisi
   daha ince ve kullanıcının gördüğü şey o.

   Düğüm kimlikleri sorgu kimliğinden türetildiği için (bkz.
   `buildCanvasNodes`) aynı soru her açılışta aynı kimliği alıyor;
   yerleşim de bu yüzden yeniden yüklemeden sağ çıkıyor.
───────────────────────────────────────────────────────────── */

import { queryIdOf } from './canvasGraph';

const KEY_PREFIX = 'grafirio.canvas.layout.';

/**
 * `positions`      — düğüm kimliği → sürüklenmiş konum
 * `hidden`         — silinmiş düğüm kimlikleri
 * `hiddenQueries`  — tuvalde kendi dalı çizilmeyen sorgular (düzeltmeler)
 * `parents`        — tuval üzerinden açılmış dalın bağlı olduğu düğüm
 * `replacements`   — düğüm kimliği → yerine geçen sorgunun kimliği
 */
export const emptyLayout = () => ({
  positions: {},
  hidden: [],
  hiddenQueries: [],
  parents: {},
  replacements: {},
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
      hiddenQueries: Array.isArray(parsed?.hiddenQueries) ? parsed.hiddenQueries : [],
      parents: parsed?.parents ?? {},
      replacements: parsed?.replacements ?? {},
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
 * Saf fonksiyon. Sırasıyla:
 *   1. Düzeltilmiş grafiklerin içeriğini yerine koyar,
 *   2. Silinmiş düğümleri ve düzeltme sorgularının kendi dallarını atar,
 *   3. Kaydedilmiş konumları yerleştirir,
 *   4. Tuval üzerinden sorulmuş takip sorularının bağlarını geri kurar.
 *
 * (1) önce geliyor: düzeltme sorgusunun grafiği okunduktan sonra o sorgunun
 * dalı gizleniyor. Ters sırada içerik okunacak düğüm çoktan atılmış olurdu.
 */
export const applyLayout = ({ nodes, edges }, layout = emptyLayout()) => {
  const hidden = new Set(layout.hidden ?? []);
  const hiddenQueries = new Set(layout.hiddenQueries ?? []);
  const positions = layout.positions ?? {};
  const replacements = layout.replacements ?? {};

  // 1. Düzeltmeler: hedef düğüm, düzeltme sorgusunun ilk grafiğini alıyor.
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const replaced = nodes.map((node) => {
    const queryId = replacements[node.id];
    if (!queryId) return node;
    const source = byId.get(`chart:${queryId}:0`);
    // Düzeltme sorgusu geçmişten düşmüşse (50 kayıt sınırı) grafik ilk
    // hâlinde kalıyor — boş kutu göstermektense eski sonuç doğru davranış.
    return source ? { ...node, data: source.data } : node;
  });

  const visible = replaced.filter(
    (n) => !hidden.has(n.id) && !hiddenQueries.has(queryIdOf(n.id)));
  const ids = new Set(visible.map((n) => n.id));

  const placed = visible.map((n) =>
    positions[n.id] ? { ...n, position: { ...positions[n.id] } } : n);

  // Sunucudan gelen zincir kenarları (`chain-…`), aynı soru için yerel bir
  // bağ varsa düşürülüyor. İkisi de doğru ama farklı inceliktedir: sunucu
  // hangi turun devamı olduğunu, yerel kayıt hangi düğümün altına yazıldığını
  // biliyor. İkisini birden çizmek aynı ilişkiyi iki ok olarak gösterirdi.
  const localChildren = new Set(Object.keys(layout.parents ?? {}));
  const kept = edges.filter((e) =>
    ids.has(e.source) && ids.has(e.target)
    && !(String(e.id).startsWith('chain-') && localChildren.has(e.target)));
  const seen = new Set(kept.map((e) => e.id));

  // Takip soruları: "bu grafiğin üzerinden sor" ile açılan dallar.
  const threads = Object.entries(layout.parents ?? {})
    .filter(([child, parent]) => ids.has(child) && ids.has(parent))
    .map(([child, parent]) => ({
      id: `e-${parent}-${child}`,
      source: parent,
      target: child,
      animated: false,
      style: { stroke: 'var(--accent)' },
    }))
    .filter((e) => !seen.has(e.id));

  return { nodes: placed, edges: [...kept, ...threads] };
};
