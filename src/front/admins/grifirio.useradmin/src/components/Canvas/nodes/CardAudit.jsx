import React from 'react';

/* Denetim izi — "bu sayı doğru mu" sorusunun cevaplanabildiği tek yer.

   Eskiden yan paneldeki sohbet mesajının altındaydı; panel kalkınca kartın
   içine taşındı. Taşınmasaydı sessizce kaybolurdu ve grafiğin makul
   görünmesi doğruluğunu kanıtlamıyor: hangi tabloya gidildiği, hangi
   yoldan bağlanıldığı ve neyin filtrelendiği görülmeden sonuç
   değerlendirilemez. */

const FILTER_OPS = { gte: '≥', gt: '>', lte: '≤', lt: '<', eq: '=', ne: '≠' };

/**
 * Filtreleri okunabilir yazar.
 *
 * Analizör zaten okunabilir bir özet üretiyor (`appliedFilters`); varsa o
 * kullanılıyor. Yoksa ham `filters` biçimleniyor — bu nesne artık yalnızca
 * düz değer tutmuyor: tarih aralıkları `{gte, lt}`, çoklu seçim dizi olarak
 * geliyor. Şablon dizesiyle yazdırmak bunları `[object Object]` yapıyordu,
 * yani "bu yıl" diye sorulduğunda filtrenin ne olduğu okunamıyordu — ki
 * panelin varlık sebebi tam olarak o.
 */
const describeFilters = (audit) => {
  if (Array.isArray(audit?.appliedFilters) && audit.appliedFilters.length > 0) {
    return audit.appliedFilters.join(' · ');
  }

  const filters = audit?.filters;
  if (!filters || Object.keys(filters).length === 0) return '';

  return Object.entries(filters)
    .map(([column, value]) => {
      if (value === null || value === undefined) return `${column} boş`;
      if (Array.isArray(value)) return `${column} ∈ (${value.join(', ')})`;
      if (typeof value === 'object') {
        return Object.entries(value)
          .map(([op, operand]) =>
            `${column} ${FILTER_OPS[String(op).toLowerCase()] ?? op} ${operand}`)
          .join(' ve ');
      }
      return `${column} = ${value}`;
    })
    .join(' · ');
};

const Row = ({ label, children }) => (
  <div><dt>{label}</dt><dd>{children}</dd></div>
);

const Lines = ({ items }) => items.map((note, i) => <div key={i}>{note}</div>);

export default function CardAudit({ audit }) {
  if (!audit) return null;

  const filters = describeFilters(audit);

  return (
    <details className="bi-card-audit">
      <summary>Nasıl hesaplandı?</summary>
      <dl className="bi-card-audit-rows">
        {audit.targetTable && <Row label="Tablo">{audit.targetTable}</Row>}

        {/* Birleştirme, sonucun doğruluğunu en çok etkileyen ama en az
            görünen karar: hangi tablonun hangi yoldan bağlandığı, bağlantının
            ölçülmüş mü çıkarsanmış mı olduğu. Sekiz tablolu bir sorguda "bu
            sayı doğru mu" sorusunu başka türlü cevaplamak mümkün değil. */}
        {audit.joins?.length > 0 && (
          <Row label="Birleştirme"><Lines items={audit.joins} /></Row>
        )}

        {audit.groupBy?.length > 0 && <Row label="Gruplama">{audit.groupBy.join(', ')}</Row>}
        {audit.targetColumn && <Row label="Ölçüm">{audit.targetColumn}</Row>}
        {audit.aggregation && <Row label="İşlem">{audit.aggregation}</Row>}
        {filters && <Row label="Filtre">{filters}</Row>}

        {/* Eşik ayrı satır: filtreyle karıştırılması en kolay şey. Filtre
            satırları toplamadan önce eler, eşik grupları toplandıktan sonra —
            ikisi farklı soruları cevaplar. */}
        {audit.having && <Row label="Eşik">{audit.having}</Row>}

        {/* Kullanıcının kendi öğrettiği bilgi. Yalnızca kodlar izlenebiliyor:
            bir eş anlamlının kullanılıp kullanılmadığını modelin sessiz kararı
            belirliyor ve bize söylemiyor. Bilmediğimizi "kullanıldı" diye
            yazmak denetim izinin değerini bitirir. */}
        {audit.learnedCodes?.length > 0 && (
          <Row label="Sizin öğrettiğiniz"><Lines items={audit.learnedCodes} /></Row>
        )}

        {audit.window && <Row label="Kırılım üstü hesap">{audit.window}</Row>}

        {/* Birleşimde grafikteki her seri ayrı bir tablodan geliyor; hangisinin
            nereden geldiği söylenmezse iki seri tek veri sanılır. */}
        {audit.union?.length > 0 && (
          <Row label="Kaynaklar"><Lines items={audit.union} /></Row>
        )}

        {typeof audit.groupCount === 'number' && audit.groupCount > 0 && (
          <Row label="Toplam grup">{audit.groupCount}</Row>
        )}
        {typeof audit.rowsRead === 'number' && <Row label="Okunan satır">{audit.rowsRead}</Row>}

        {audit.executedSql && (
          <Row label="Çalışan SQL">
            <code className="bi-card-audit-sql">{audit.executedSql}</code>
          </Row>
        )}
      </dl>
    </details>
  );
}
