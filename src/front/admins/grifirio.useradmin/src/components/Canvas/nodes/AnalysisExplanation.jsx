import React from 'react';

export default function AnalysisExplanation({ report, insights = [], queryId, printMode = false }) {
  return <section className="analysis-explanation">
    <h3>Analiz açıklaması</h3>
    {report ? <>
      <dl>
        <dt>Ölçü / birim</dt><dd>{report.metric} / {report.unit}</dd>
        <dt>Veri kaynağı</dt><dd>{report.source || 'Belirtilmedi'}</dd>
        {report.joins?.length > 0 && <><dt>Birleştirmeler</dt><dd>{report.joins.join(' · ')}</dd></>}
        <dt>{report.periodScope === 'displayed_result' ? 'Gösterilen dönem' : 'Veri dönemi'}</dt><dd>{report.dataPeriod ? `${report.dataPeriod.from} – ${report.dataPeriod.to}` : 'Dönem sınırları kaydedilmedi; filtreleri kontrol edin.'}</dd>
        {report.timeAxis && <><dt>Zaman alanı</dt><dd>{report.timeAxis.column} ({report.timeAxis.granularity})</dd></>}
        <dt>Filtreler</dt><dd>{report.filters?.length ? report.filters.join(' · ') : 'Ek filtre yok'}</dd>
        <dt>Hesaplama yöntemi</dt><dd>{report.method}</dd>
        <dt>Nasıl okunmalı?</dt><dd>{report.interpretation}</dd>
      </dl>
      {report.warnings?.length > 0 && <ul className="analysis-warnings">{report.warnings.map((w, i) => <li key={i}>{w}</li>)}</ul>}
      <p className="analysis-reference">Sonuç tarihi: {new Date(report.generatedAt).toLocaleString('tr-TR')} · Analiz: {queryId || '—'}</p>
      {(report.tables || []).map((table, i) => <details key={i} open={printMode || undefined}><summary>{table.title || 'Tablo'} — sütunların anlamı</summary>
        <dl>{table.columns.map((column, ci) => <React.Fragment key={ci}><dt>{column}</dt><dd>{table.columnExplanations?.[ci] || 'Alan açıklaması kaydedilmedi.'}</dd></React.Fragment>)}</dl>
      </details>)}
      {!printMode && (report.tables || []).map((table, i) => <details key={`data-${i}`}><summary>{table.title || 'Sonuç'} — veri tablosu ({table.rows.length} satır)</summary>
        {table.rows.length > 100 && <p>Ekranda ilk 100 satır gösteriliyor. Tam çıktı için sunuyu kullanın veya filtreyle daraltın.</p>}
        <div className="analysis-result-table"><table><thead><tr>{table.columns.map((column, ci) => <th key={ci}>{column}</th>)}</tr></thead>
          <tbody>{table.rows.slice(0, 100).map((row, ri) => <tr key={ri}>{row.map((value, ci) => <td key={ci}>{value == null ? '—' : typeof value === 'number' ? value.toLocaleString('tr-TR') : typeof value === 'object' ? JSON.stringify(value) : String(value)}</td>)}</tr>)}</tbody>
        </table></div>
      </details>)}
    </> : <p>Bu eski sonuçta yöntem açıklaması kaydedilmemiş. Açıklamalı bir sonuç için analizi yeniden çalıştırın.</p>}
    {insights.filter(i => !report || i.type !== 'warning').map((i, index) => <p key={index}><strong>{i.title}: </strong>{i.description}</p>)}
  </section>;
}
