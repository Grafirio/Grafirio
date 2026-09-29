import client, { GATEWAY } from './authClient';

/*
 * Semantik zekâ verisinin iki kaynağı:
 *
 *   1. Benchmark dashboard (benchmarkt) — değerlendirme koşuları. Altın veriye
 *      karşı ölçülmüş, commit'e bağlı kanıt: "semantic.*" setleri.
 *   2. DataAnalysis — üretimdeki canlı sinyaller (yalnızca toplu, PLATFORM_ADMIN).
 *
 * Grafirio servisleri dashboard'a bağımlı değil; bu bağ yalnızca panelde.
 */

// Varsayılan: gateway'in /benchmark rotası (yalnızca PLATFORM_ADMIN; dashboard'un
// kendi kimlik doğrulaması yok, iç ağda duruyor). VITE_BENCHMARK_URL verilirse
// doğrudan o adres — yerelde `dotnet run` ile açılan dashboard (http://localhost:5080).
export const DIRECT_BENCHMARK_URL = (import.meta.env.VITE_BENCHMARK_URL || '').replace(/\/$/, '');
export const BENCHMARK_URL = DIRECT_BENCHMARK_URL || `${GATEWAY}/benchmark`;

async function benchmark(path) {
  try {
    return (await client.get(`${BENCHMARK_URL}${path}`)).data;
  } catch (error) {
    const status = error?.response?.status;
    throw new Error(`Benchmark dashboard: ${status ? `HTTP ${status}` : error.message} — ${path}`);
  }
}

/** Bir setin koşuları, en yeniden eskiye (koşu düzeyi metriklerle). */
export const getRuns = (suite, take = 30) =>
  benchmark(`/api/scenario-runs?suite=${encodeURIComponent(suite)}&take=${take}`).then((r) => r.items);

/** Tek koşu: metrikler, ortam, vakalar. */
export const getRun = (id) => benchmark(`/api/scenario-runs/${id}`);

/** Canlı sinyaller (üretim, son N gün). */
export const getSignals = async (days) =>
  (await client.get(`${GATEWAY}/data-analysis/api/admin/semantic-signals`, { params: { days } })).data;

/*
 * Altı yetenek. `headline` kartın ana rakamı; ölçülmemişse `fallback`.
 * `secondary` kartta headline'ın altında görünenler.
 */
export const CAPABILITIES = [
  {
    key: 'schema',
    suite: 'semantic.schema',
    title: 'Şema anlama',
    english: 'Schema Understanding',
    description: 'Kolonların rolünü (ölçüm, boyut, tarih, kimlik), tabloların amacını ve hassas kolonları doğru tanıyor mu.',
    headline: 'role.accuracy',
    fallback: 'sensitive.recall',
    secondary: ['sensitive.recall', 'table.concept_accuracy', 'table.opposite_confusions'],
  },
  {
    key: 'relationships',
    suite: 'semantic.relationships',
    title: 'İlişki keşfi',
    english: 'Relationship Discovery',
    description: 'Tablolar arası bağlantıları (FK, ad, veri örtüşmesi) buluyor ve yanlış bağlantı kurmuyor mu.',
    headline: 'f1',
    secondary: ['precision', 'recall', 'trap_hits'],
  },
  {
    key: 'mapping',
    suite: 'semantic.mapping',
    title: 'Anlamsal eşleme',
    english: 'Semantic Mapping',
    description: 'Kullanıcının kelimelerini doğru tablo, kolon ve değerlere bağlıyor; aynı soruyu farklı yazınca aynı cevabı veriyor mu.',
    headline: 'accuracy',
    secondary: ['table.accuracy', 'paraphrase.consistency', 'false_abstention.rate'],
  },
  {
    key: 'unknown',
    suite: 'semantic.unknown',
    title: 'Bilinmeyen veri',
    english: 'Unknown Data Handling',
    description: 'Cevabı şemada olmayan ya da belirsiz sorularda uydurmak yerine soruyor mu.',
    headline: 'abstention.recall',
    secondary: ['hallucination.rate', 'false_abstention.rate'],
  },
  {
    key: 'sql',
    suite: 'semantic.sql',
    title: 'SQL üretimi',
    english: 'SQL Generation',
    description: 'Üretilen sorgu, altın SQL ile aynı sayıları veriyor mu; join satırları çoğaltıyor mu.',
    headline: 'execution.accuracy',
    secondary: ['execution.success', 'fan_out.suspected'],
  },
  {
    key: 'visualization',
    suite: 'semantic.visualization',
    title: 'Görselleştirme',
    english: 'Visualization Accuracy',
    description: 'Grafik türü uygun mu; grafik sorgu sonucunu eksiksiz ve doğru sırayla gösteriyor mu.',
    headline: 'fidelity',
    secondary: ['type.accuracy', 'completeness', 'order.accuracy'],
  },
];

const LABELS = {
  'role.accuracy': 'Rol doğruluğu',
  'role.recall.measure': 'Ölçüm kolonu yakalama',
  'role.recall.date': 'Tarih kolonu yakalama',
  'role.recall.dimension': 'Boyut kolonu yakalama',
  'role.recall.identifier': 'Kimlik kolonu yakalama',
  'meaning.coverage': 'Anlam kapsamı',
  'sensitive.recall': 'Hassas kolon koruması',
  'sensitive.over_protection': 'Gereksiz koruma',
  'table.concept_accuracy': 'Tablo amacı',
  'table.opposite_confusions': 'Karşıt tablo karışıklığı',
  setup_questions: 'Kurulum sorusu',
  precision: 'Kesinlik',
  recall: 'Duyarlılık',
  f1: 'F1',
  trap_hits: 'Tuzağa düşme',
  'precision.high_confidence': 'Yüksek güvenli kesinlik',
  accuracy: 'Doğruluk',
  'table.accuracy': 'Tablo doğruluğu',
  'pass.rate': 'Başarılı soru',
  'paraphrase.consistency': 'İfade tutarlılığı',
  'false_abstention.rate': 'Gereksiz soru',
  'abstention.recall': 'Doğru soru sorma',
  'hallucination.rate': 'Uydurma',
  'execution.accuracy': 'Çalıştırma doğruluğu',
  'execution.success': 'Tamamlanma',
  'fan_out.suspected': 'Fan-out şüphesi',
  fidelity: 'Veri sadakati',
  'type.accuracy': 'Tür uygunluğu',
  'structure.validity': 'Yapı geçerliliği',
  completeness: 'Eksiksizlik',
  'order.accuracy': 'Sıra doğruluğu',
  questions: 'Soru',
  charts: 'Grafik',
  // Canlı sinyaller
  'setup_questions.mean': 'Kurulum sorusu / analiz',
  'corrections.per_analysis': 'Kullanıcı düzeltmesi / analiz',
  'analysis.success.rate': 'Analiz başarısı',
  'found.per_analysis': 'Bulunan ilişki / analiz',
  'inferred.per_analysis': 'Çıkarsanan ilişki / analiz',
  'confirmation.acceptance.rate': 'Önerilen eşleşme kabulü',
  'reask.rate': 'Tekrar sorma',
  'feedback.negative.rate': 'Olumsuz oy',
  'completed.rate': 'Tamamlanma',
  'clarification.rate': 'Netleştirme',
  'clarification.resolved.rate': 'Netleştirme çözümü',
  'failure.rate': 'Başarısız sorgu',
  'failure.sql.rate': 'SQL hatası',
  'failure.policy.rate': 'Politika reddi',
  'failure.timeout.rate': 'Zaman aşımı',
  'failure.bridge.rate': 'Bridge hatası',
  'failure.other.rate': 'Diğer hata',
  'chart.override.rate': 'Grafik türü değiştirme',
  'feedback.positive.rate': 'Olumlu oy',
};

/** "recall.name-role" → "Duyarlılık · name-role"; "f1@lojistik" → "F1 · lojistik". */
export function metricLabel(name) {
  if (LABELS[name]) return LABELS[name];
  const at = name.indexOf('@');
  if (at > 0) return `${metricLabel(name.slice(0, at))} · ${name.slice(at + 1)}`;
  const parts = name.split('.');
  for (let i = parts.length - 1; i > 0; i -= 1) {
    const head = parts.slice(0, i).join('.');
    if (LABELS[head]) return `${LABELS[head]} · ${parts.slice(i).join('.')}`;
  }
  return name;
}

const number = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 1 });

export function formatValue(value, unit) {
  if (value === null || value === undefined || !Number.isFinite(value)) return '—';
  if (unit === 'ratio') return `%${number.format(value * 100)}`;
  if (unit === 'ms') return value >= 10000 ? `${number.format(value / 1000)} sn` : `${number.format(value)} ms`;
  if (unit === 'USD') return `$${new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 4 }).format(value)}`;
  return number.format(value);
}

/**
 * İki değer arasındaki fark ve yönü. Yön metriğin kendi `direction`'ından
 * geliyor: "lower" metriklerde düşüş iyileşmedir.
 */
export function delta(current, previous, unit, direction) {
  if (current === null || current === undefined || previous === null || previous === undefined) return null;
  const diff = current - previous;
  if (Math.abs(diff) < 1e-9) return { text: 'değişmedi', tone: 'neutral', arrow: '' };
  const better = direction === 'higher' ? diff > 0 : direction === 'lower' ? diff < 0 : null;
  const sign = diff > 0 ? '+' : '−';
  const magnitude = unit === 'ratio'
    ? `${number.format(Math.abs(diff) * 100)} puan`
    : formatValue(Math.abs(diff), unit);
  return {
    text: `${sign}${magnitude}`,
    arrow: diff > 0 ? '▲' : '▼',
    tone: better === null ? 'neutral' : better ? 'good' : 'bad',
  };
}

export const metricOf = (run, name) => run?.metrics?.find((m) => m.name === name) ?? null;
