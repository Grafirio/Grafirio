/* ─────────────────────────────────────────────────────────────
   Tuval çizgesi — düğüm kimlikleri

   Kimlik sorgu kimliğinden türetiliyor: aynı konuşma her açılışta aynı
   kartı üretiyor. Sürüklenen konum ve silinen kart bu sayede yeniden
   yüklemeden sağ çıkabiliyor — ikisi de kimliğe göre saklanıyor
   (bkz. `canvasLayout`).

   Biçim `tür:sorguKimliği`. İki nokta ile ayrılması bilerek: bir kartın
   hangi konuşmadan geldiği tek bakışta okunabiliyor.
───────────────────────────────────────────────────────────── */

export const nodeIds = {
  /** Bir konuşma zincirinin tamamı: konuşma + grafik tek kart. */
  card: (rootQueryId) => `card:${rootQueryId}`,
};

/** Düğüm kimliğinin ikinci parçası sorgu kimliği: `card:<queryId>`. */
export const queryIdOf = (nodeId) => String(nodeId).split(':')[1] || null;
