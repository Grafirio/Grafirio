import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import BiChartNode from './nodes/BiChartNode';
import AnalysisExplanation from './nodes/AnalysisExplanation';
import AnalysisNotices from './nodes/AnalysisNotices';
import ForecastSummary from './nodes/ForecastSummary';
import formatReportNumber from '../../utils/formatReportNumber';
import { presentationEntries, presentationAvailability, presentationPages } from '../../utils/analysisPresentation';
import { IconFileTypePdf, IconArrowRight, IconPrinter, IconX } from '@tabler/icons-react';
import './AnalysisPresentation.css';

const cellText = value => value == null ? '—' : typeof value === 'number' ? formatReportNumber(value) : typeof value === 'object' ? JSON.stringify(value) : String(value);

export default function AnalysisPresentation({ nodes, database, onClose }) {
  const [entries, setEntries] = useState(() => presentationEntries(nodes));
  const availability = presentationAvailability(nodes);
  const [selected, setSelected] = useState(() => new Set(entries.map(e => e.id)));
  const [preview, setPreview] = useState(false);
  const [title, setTitle] = useState('Grafirio analiz raporu');
  const [includeHistory, setIncludeHistory] = useState(false);
  const [includeTechnical, setIncludeTechnical] = useState(false);
  const [exportedAt] = useState(() => new Date().toLocaleString('tr-TR'));
  const chosen = entries.filter(e => selected.has(e.id));
  const pages = presentationPages(chosen, { includeHistory, includeTechnical });
  const printReport = () => {
    const previousTitle = document.title;
    document.title = title || "Grafirio analiz raporu";
    window.addEventListener("afterprint", () => { document.title = previousTitle; }, { once: true });
    window.print();
  };
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
        {!preview && entries.length > 0 && <div className="presentation-options">
          <label><input type="checkbox" checked={includeHistory} onChange={e => setIncludeHistory(e.target.checked)} /> Geçmiş veri tablolarını ekle</label>
          <label><input type="checkbox" checked={includeTechnical} onChange={e => setIncludeTechnical(e.target.checked)} /> Teknik yöntem ekini ekle</label>
          <p>Özet, grafik, yöntem açıklaması ve tahmin tablosu ana raporda yer alır. {pages.length} sayfa hazırlanacak.</p>
        </div>}
        <div className="analysis-modal-actions">
          {entries.length > 0 && <button className="analysis-button-primary" disabled={!chosen.length || tooLarge} onClick={() => setPreview(!preview)}>{preview ? 'Seçime dön' : 'Sunuyu önizle'}<IconArrowRight size={16} /></button>}
          {preview && <button className="analysis-button-primary" onClick={printReport}><IconPrinter size={16} />PDF olarak kaydet / Yazdır</button>}
          <button onClick={onClose}><IconX size={16} />{entries.length ? 'Kapat' : 'Tuvale dön'}</button>
        </div>
        {tooLarge && <p role="alert">Seçilen sonuçlar 2.000 tablo satırını aşıyor. Daha az analiz seçin veya veri filtresini daraltın; hiçbir satır sessizce kesilmeyecek.</p>}
        {preview && <p>Yazdırma penceresinde “PDF olarak kaydet” seçeneğini kullanın. Başlık ve altbilgileri kapatın. Önizlemedeki sayfaları kontrol edin.</p>}
      </div>
      {preview && <article className="analysis-print-report">
        {pages.map((page, pageIndex) => <section className={`presentation-page ${page.kind === 'cover' ? 'presentation-cover' : ''}`} key={pageIndex}>
          <div className="presentation-page-content">
          {page.kind === 'cover' && <>
            <p className="presentation-brand">GRAFIRIO / ANALİZ RAPORU</p><h1>{title || 'Analiz raporu'}</h1>
            <p>{database || 'Analiz sonuçları'} · Hazırlanma tarihi: {exportedAt}</p>
            {chosen.length === 1 && <><h2>{chosen[0].report.title || chosen[0].chart.title}</h2><ForecastSummary report={chosen[0].report} />
              <p>Kullanılan geçmiş: {chosen[0].report.dataPeriod ? `${chosen[0].report.dataPeriod.from} – ${chosen[0].report.dataPeriod.to}` : 'Hesaplama açıklamasında belirtilmiştir.'}</p>
              {chosen[0].report.historyPolicy?.description && <p>{chosen[0].report.historyPolicy.description}</p>}
            </>}
            <h3>Rapor içeriği</h3><ol>{chosen.map(e => <li key={e.id}>{e.report.title || e.chart.title || e.question}</li>)}</ol>
            <p>Özet, hesaplama açıklaması, grafik ve veri tabloları aynı kaydedilmiş sonuçtan hazırlanmıştır.</p>
          </>}
          {page.kind === 'overview' && <><h2>{page.entry.report.title || page.entry.chart.title}</h2><ForecastSummary report={page.entry.report} /></>}
          {page.kind === 'chart' && <>
            <p className="presentation-eyebrow">Analiz {page.index + 1} · Grafik {page.ci + 1}</p><h2>{page.entry.report.title || page.chart.title}</h2>
            <BiChartNode data={{ ...page.chart, title: page.entry.report.title || page.chart.title, printMode: true, displayType: page.entry.report.forecastSummary ? 'line' : page.ci === 0 ? page.entry.chartType : page.chart.type }} />
            {page.chart.data?.datasets?.some(d => d.label?.includes('sınır')) && <p className="analysis-reference">Gölgeli alan yaklaşık belirsizlik bandını gösterir; doğrulanmış %95 tahmin aralığı değildir.</p>}
            <AnalysisNotices notices={page.entry.report.notices} />
            <p><strong>Nasıl hesaplandı? </strong>{page.entry.report.methodDescription || page.entry.report.method}</p>
            <p><strong>Kullanılan geçmiş: </strong>{page.entry.report.dataPeriod ? `${page.entry.report.dataPeriod.from} – ${page.entry.report.dataPeriod.to}` : 'Dönem kaydedilmemiş'} · {page.entry.report.unit === 'Belirtilmedi' ? 'Birim doğrulanmadı' : page.entry.report.unit}</p>
            {page.entry.report.historyPolicy?.description && <p>{page.entry.report.historyPolicy.description}</p>}
            <details className="presentation-limitations" open><summary>Sonucun sınırları</summary><ul>{(page.entry.report.warnings || []).map((w, wi) => <li key={wi}>{w}</li>)}</ul></details>
          </>}
          {page.kind === 'technical' && <>
            <h2>Teknik yöntem eki — {page.entry.report.title || page.entry.chart.title}</h2>
            <AnalysisExplanation report={{ ...page.entry.report, notices: [], warnings: [] }} insights={[]} queryId={page.entry.queryId} printMode />
            <p className="analysis-reference">Sonuç sürümü: {page.entry.report.version} · Kaynak kayıt özeti: {page.entry.report.snapshotHash}</p>
          </>}
          {page.kind === 'table' && <>
            <h2>{page.table.title || 'Veri tablosu'}</h2><p>{page.entry.report.title || page.entry.chart.title}</p>
            <p>{page.table.kind === 'forecast' ? 'Aşağıdaki değerler gerçekleşen sonuç değil, aylık öngörülerdir.' : 'Aşağıdaki değerler kaynak veriden hesaplanan sonuçlardır.'} · Birim: {page.entry.report.unit}</p>
            <p>Satırlar: {page.tablePage.range || 'Tümü'} · Boş hücreler (—) sıfır değildir.</p>
            <table><thead><tr>{page.tablePage.columns.map((c, i) => <th key={i}>{c}</th>)}</tr></thead>
              <tbody>{page.tablePage.rows.map((r, ri) => <tr key={ri}>{r.map((v, vi) => <td key={vi}>{cellText(v)}</td>)}</tr>)}</tbody></table>
            {page.table.kind === 'forecast' && page.entry.report.forecastSummary?.total != null && <p className="presentation-table-total"><strong>{page.entry.report.forecastSummary.totalLabel}: </strong>{formatReportNumber(page.entry.report.forecastSummary.total)} {page.entry.report.unit !== 'Belirtilmedi' ? page.entry.report.unit : '(birim doğrulanmadı)'}</p>}
            {page.table.kind === 'forecast' && <p>Alt ve üst sınırlar yaklaşık aylık bantlardır; bu sınırlar toplanarak yıllık güven aralığı hesaplanmaz.</p>}
          </>}
          </div>
          <footer className="presentation-footer"><span>GRAFIRIO · {exportedAt}</span><span>{pageIndex + 1} / {pages.length}</span></footer>
        </section>)}
      </article>}
    </section>
  </div>, document.body);
}
