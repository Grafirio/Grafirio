import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import BiChartNode from './nodes/BiChartNode';
import AnalysisExplanation from './nodes/AnalysisExplanation';
import { presentationEntries, presentationAvailability, tablePages } from '../../utils/analysisPresentation';
import { IconFileTypePdf, IconArrowRight, IconPrinter, IconX } from '@tabler/icons-react';
import './AnalysisPresentation.css';

const cellText = value => value == null ? '—' : typeof value === 'number' ? value.toLocaleString('tr-TR') : typeof value === 'object' ? JSON.stringify(value) : String(value);

export default function AnalysisPresentation({ nodes, database, onClose }) {
  const [entries, setEntries] = useState(() => presentationEntries(nodes));
  const availability = presentationAvailability(nodes);
  const [selected, setSelected] = useState(() => new Set(entries.map(e => e.id)));
  const [preview, setPreview] = useState(false);
  const [title, setTitle] = useState('Grafirio analiz sunusu');
  const [exportedAt] = useState(() => new Date().toLocaleString('tr-TR'));
  const chosen = entries.filter(e => selected.has(e.id));
  useEffect(() => {
    // An empty dialog can wait for the first result; an existing export stays frozen.
    if (entries.length === 0) {
      const ready = presentationEntries(nodes);
      if (ready.length) { setEntries(ready); setSelected(new Set(ready.map(e => e.id))); }
    }
  }, [nodes, entries.length]);
  const tooLarge = chosen.reduce((total, entry) => total + (entry.report.tables || []).reduce((sum, table) => sum + table.rows.length, 0), 0) > 2000;
  const move = (index, direction) => setEntries(current => {
    const next = [...current], target = index + direction;
    if (target < 0 || target >= next.length) return current;
    [next[index], next[target]] = [next[target], next[index]];
    return next;
  });
  return createPortal(<div className={`analysis-modal-backdrop presentation-backdrop ${preview ? 'is-preview' : ''}`}>
    <section className="analysis-modal presentation-dialog" role="dialog" aria-modal="true" aria-label="PDF sunusu">
      <div className="presentation-controls">
        <div className="presentation-heading"><span className="presentation-heading-icon"><IconFileTypePdf size={24} stroke={1.5} /></span><div><h2>PDF sunusu</h2><p>Analizlerini paylaşılabilir bir sunuya dönüştür.</p></div></div>
        {!preview && <><label>Sunu başlığı<input value={title} maxLength={160} onChange={e => setTitle(e.target.value)} /></label>
          <p>Analizleri seçip sıralayın. Grafikler, veri tabloları ve hesaplama açıklamaları aynı kaydedilmiş sonuçtan alınır.</p>
          {!entries.length && <div className="presentation-empty" role="status"><IconFileTypePdf size={34} stroke={1.3} /><h3>{availability.loading ? 'Sonucun hazır olmasını bekliyoruz' : availability.legacy ? 'Bu sonuçların açıklaması eksik' : 'Henüz sunuya eklenecek analiz yok'}</h3><p>{availability.message}</p></div>}
          {entries.length > 0 && availability.legacy > 0 && <p className="presentation-legacy-note">{availability.legacy} eski sonuç açıklaması bulunmadığı için listelenmedi. Eklemek için ilgili karttan analizi yeniden çalıştırın.</p>}
          {entries.map((entry, index) => <div className="presentation-selection" key={entry.id}>
            <label><input type="checkbox" checked={selected.has(entry.id)} onChange={e => setSelected(current => {
              const next = new Set(current); if (e.target.checked) next.add(entry.id); else next.delete(entry.id); return next;
            })} />{entry.chart.title || entry.question}</label>
            <button aria-label="Yukarı taşı" disabled={index === 0} onClick={() => move(index, -1)}>↑</button>
            <button aria-label="Aşağı taşı" disabled={index === entries.length - 1} onClick={() => move(index, 1)}>↓</button>
          </div>)}</>}
        <div className="analysis-modal-actions">
          {entries.length > 0 && <button className="analysis-button-primary" disabled={!chosen.length || tooLarge} onClick={() => setPreview(!preview)}>{preview ? 'Seçime dön' : 'Sunuyu önizle'}<IconArrowRight size={16} /></button>}
          {preview && <button className="analysis-button-primary" onClick={() => window.print()}><IconPrinter size={16} />PDF olarak kaydet / Yazdır</button>}
          <button onClick={onClose}><IconX size={16} />{entries.length ? 'Kapat' : 'Tuvale dön'}</button>
        </div>
        {tooLarge && <p role="alert">Seçilen sonuçlar 2.000 tablo satırını aşıyor. Daha az analiz seçin veya veri filtresini daraltın; hiçbir satır sessizce kesilmeyecek.</p>}
        {preview && <p>Yazdırma penceresinde “PDF olarak kaydet” seçeneğini kullanın. Başlık ve altbilgileri kapatın. Önizlemedeki sayfaları kontrol edin.</p>}
      </div>
      {preview && <article className="analysis-print-report">
        <section className="presentation-page presentation-cover"><p>GRAFIRIO</p><h1>{title || 'Analiz sunusu'}</h1>
          <p>{database || 'Analiz sonuçları'}</p><p>Sunu hazırlanma tarihi: {exportedAt}</p>
          <h2>İçerik</h2><ol>{chosen.map(e => <li key={e.id}>{e.chart.title || e.question}</li>)}</ol>
          <p>Gerçekleşen veriler ve öngörüler kendi açıklama ve sınırlamalarıyla sunulmuştur.</p>
        </section>
        {chosen.map((entry, index) => <React.Fragment key={entry.id}>
          {entry.charts.filter(c => c.type !== 'table').map((chart, ci) => <section className="presentation-page" key={ci}>
            <p className="presentation-eyebrow">Analiz {index + 1} · Grafik {ci + 1}</p><h2>{chart.title}</h2><p>{entry.question}</p>
            <BiChartNode data={{ ...chart, printMode: true, displayType: ci === 0 ? entry.chartType : chart.type }} />
            <p>{entry.report.interpretation}</p><p>{entry.report.method}</p>
            {entry.report.warnings.map((w, wi) => <p className="analysis-warnings" key={wi}>{w}</p>)}
          </section>)}
          <section className="presentation-page"><h2>{entry.chart.title} — hesaplama açıklaması</h2>
            <AnalysisExplanation report={entry.report} insights={entry.insights} queryId={entry.queryId} printMode />
            <p className="analysis-reference">Sonuç sürümü: {entry.report.version} · Kayıt özeti: {entry.report.snapshotHash}</p>
          </section>
          {(entry.report.tables || []).flatMap((table, ti) => tablePages(table).map((page, pi) => <section className="presentation-page" key={`${ti}-${pi}`}>
            <h2>{page.title || entry.chart.title} — veri tablosu</h2><p>{entry.report.interpretation}</p>
            <p>{entry.report.method} · Birim: {entry.report.unit}</p>
            <p>{entry.report.periodScope === 'displayed_result' ? 'Gösterilen dönem' : 'Veri dönemi'}: {entry.report.dataPeriod ? `${entry.report.dataPeriod.from} – ${entry.report.dataPeriod.to}` : 'Filtreler hesaplama açıklamasında'} · Satırlar: {page.range || 'Tümü'}</p>
            <table><thead><tr>{page.columns.map((c, i) => <th key={i}>{c}</th>)}</tr></thead>
              <tbody>{page.rows.map((r, ri) => <tr key={ri}>{r.map((v, vi) => <td key={vi}>{cellText(v)}</td>)}</tr>)}</tbody></table>
            <p className="analysis-reference">Analiz: {entry.queryId} · Boş hücreler (—) sıfır değildir.</p>
          </section>))}
        </React.Fragment>)}
      </article>}
    </section>
  </div>, document.body);
}
