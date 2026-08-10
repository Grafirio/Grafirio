/* ─────────────────────────────────────────────────────────────
   Tuval çizgesi — düğüm kimlikleri ve dal gezinmesi

   Kimlik sorgu kimliğinden türetiliyor: aynı soru her açılışta aynı
   düğümleri üretiyor. Bu sayede sürüklenen konum, silinen düğüm ve
   düzeltilen grafik yeniden yüklemeden sağ çıkabiliyor — hepsi kimliğe
   göre saklanıyor (bkz. `canvasLayout`).

   Biçim `tür:sorguKimliği[:sıra]`. İki nokta ile ayrılması bilerek: bir
   düğümün hangi sorgudan geldiği tek bakışta okunabiliyor, silme ve
   değiştirme bunun üzerinden çalışıyor.
───────────────────────────────────────────────────────────── */

export const nodeIds = {
  question: (queryId) => `q:${queryId}`,
  answer:   (queryId) => `ans:${queryId}`,
  chart:    (queryId, i) => `chart:${queryId}:${i}`,
  insight:  (queryId, i) => `ins:${queryId}:${i}`,
};

/** Düğüm kimliğinin ikinci parçası sorgu kimliği: `chart:<queryId>:0`. */
export const queryIdOf = (nodeId) => String(nodeId).split(':')[1] || null;

/**
 * Bir düğümden çıkan bütün dalları toplar (kök dahil).
 *
 * Silme ve yerleştirme bunu kullanıyor: bir soru silinirken cevabının ve
 * grafiklerinin tuvalde kalması, neyin sorulduğu bilinmeyen kutular demek;
 * dalın altına yeni soru konurken de dalın tamamının bittiği yer gerekiyor.
 */
export const collectSubtree = (nodes, edges, rootId) => {
  const found = new Set([rootId]);
  const queue = [rootId];
  while (queue.length > 0) {
    const current = queue.shift();
    for (const edge of edges) {
      if (edge.source === current && !found.has(edge.target)) {
        found.add(edge.target);
        queue.push(edge.target);
      }
    }
  }
  return found;
};
