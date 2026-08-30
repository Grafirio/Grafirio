/* ─────────────────────────────────────────────────────────────
   Grafik türleri — tek liste

   Sağ tık menüsü, kart başlığındaki seçici ve grafiğin kendisi aynı
   listeden okuyor. İkiye bölünürse menüde çıkan ama çizilemeyen bir tür
   olur; kullanıcı seçer, hiçbir şey olmaz ve sebebi görünmez.
───────────────────────────────────────────────────────────── */

export const CHART_TYPES = [
  { id: 'bar', label: 'Çubuk', hint: 'Kategorileri karşılaştırır' },
  { id: 'line', label: 'Çizgi', hint: 'Zaman içindeki değişim' },
  { id: 'area', label: 'Alan', hint: 'Zaman içindeki değişim, dolgulu' },
  { id: 'pie', label: 'Pasta', hint: 'Bütünün parçaları' },
  { id: 'doughnut', label: 'Halka', hint: 'Bütünün parçaları, ortası boş' },
  { id: 'radar', label: 'Radar', hint: 'Çok eksende profil' },
  { id: 'scatter', label: 'Dağılım', hint: 'İki ölçü arasındaki ilişki' },
  { id: 'pareto', label: 'Pareto', hint: 'Sıralı çubuk + birikimli yüzde' },
];

export const DEFAULT_CHART_TYPE = 'bar';

export const chartTypeLabel = (id) =>
  CHART_TYPES.find(t => t.id === id)?.label ?? id;
