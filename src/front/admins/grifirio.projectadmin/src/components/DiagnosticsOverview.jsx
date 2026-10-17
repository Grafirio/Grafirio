import { ERRORS, STATUS } from '../services/diagnosticApi';

const seconds = (value) => value == null ? '—' : `${(value / 1000).toFixed(1)} sn`;
const rate = (value) => value == null ? '—' : `%${(value * 100).toFixed(1)}`;
const date = (value) => new Date(value).toLocaleString('tr-TR', { timeZone: 'Europe/Istanbul' });

export default function DiagnosticsOverview({ summary, onFilter, onOpen }) {
  const view = summary.visibility;
  if (!view) return null;
  const comparison = summary.comparison;
  const breakdown = (rows, connections) => <div className="dg-table-wrap"><table className="pa-table">
    <thead><tr><th>Firma{connections ? ' / bağlantı' : ''}</th><th>Toplam</th><th>Başarılı</th><th>Başarısız</th><th>Boş sonuç</th><th>Başarı oranı</th><th>Ortalama / P95</th></tr></thead>
    <tbody>{rows.map((row) => <tr key={`${row.companyId}:${row.connectionId || ''}`}>
      <td><button className="dg-link" onClick={() => onFilter({ companyId: row.companyId, ...(connections ? { connectionId: row.connectionId } : {}) })}>
        {row.companyId || 'Firma kaydedilmemiş'}{connections && <small className="dg-id">{row.connectionId}</small>}</button></td>
      <td>{row.metrics.total}</td><td>{row.metrics.completed}</td><td>{row.metrics.failed}</td><td>{row.metrics.emptyResults}</td>
      <td>{rate(row.metrics.successRate)}</td><td>{seconds(row.metrics.latency.averageMs)} / {seconds(row.metrics.latency.p95Ms)}<small className="dg-id">{row.metrics.latency.sampleCount} süre örneği</small></td>
    </tr>)}</tbody></table>{rows.length === 0 && <p>Bu filtrelerde kayıt yok.</p>}</div>;
  return <>
    <section className="gf-card dg-detail"><h2>Durum dağılımı</h2>
      <p className="gf-muted">Bir istek yalnızca bir durumda sayılır. Boş sonuçlar başarısızların alt kümesidir. {view.executionJobsCreated} istek için yürütme işi oluşturulmuş. Bu, SQL'in gerçekten çalıştığını kanıtlamaz; SQL çalıştırma sayısı mevcut kayıtlarla güvenilir biçimde ölçülemiyor.</p>
      <div className="dg-toolbar">{view.statusDistribution.map((row) => <button className="gf-btn" key={row.status}
        disabled={!row.status} onClick={() => onFilter({ status: row.status, errorCode: '' })}>{STATUS[row.status] || row.status || 'Bilinmeyen'}: {row.count}</button>)}</div>
      {view.statusDistribution.length === 0 && <p>Bu filtrelerde kayıt yok.</p>}
      <p className="gf-muted">Süreler: tamamlanma zamanı bulunan terminal kayıtlar; {view.latency.sampleCount} örnek, {view.latency.missingCount} eksik. Ortalama ve P95 aynı örneklemden hesaplanır.</p>
    </section>
    <section className="gf-card dg-detail"><h2>Başarısızlık nedenleri</h2>
      <div className="dg-table-wrap"><table className="pa-table"><thead><tr><th>Hata</th><th>Tekrar</th></tr></thead>
        <tbody>{summary.errors.map((row) => <tr key={row.code}><td><button className="dg-link" onClick={() => onFilter({ status: 'failed', errorCode: row.code })}>{ERRORS[row.code] || row.code}</button></td><td>{row.count}</td></tr>)}</tbody></table></div>
      {!summary.errors.length && <p>Bu filtrelerde başarısızlık kaydı yok.</p>}
    </section>
    <section className="gf-card dg-detail"><h2>Firma bazında görünüm</h2>{breakdown(view.byCompany, false)}</section>
    <section className="gf-card dg-detail"><h2>Bağlantı bazında görünüm</h2>{breakdown(view.byConnection, true)}</section>
    <section className="gf-card dg-detail"><h2>Önceki dönem karşılaştırması</h2>
      {comparison?.available ? <>
        <p className="gf-muted">Bu dönem: {date(comparison.currentFrom)} – {date(comparison.currentUntil)}<br />Önceki: {date(comparison.previousFrom)} – {date(comparison.previousUntil)}. Aynı filtreler uygulanır; dönemler istek oluşturulma zamanına göredir.</p>
        <div className="dg-table-wrap"><table className="pa-table"><thead><tr><th>Metrik</th><th>Bu dönem</th><th>Önceki dönem</th><th>Değişim</th></tr></thead><tbody>
          <tr><td>Toplam istek</td><td>{comparison.current.total}</td><td>{comparison.previous.total}</td><td>—</td></tr>
          <tr><td>Başarılı / başarısız</td><td>{comparison.current.completed} / {comparison.current.failed}</td><td>{comparison.previous.completed} / {comparison.previous.failed}</td><td>—</td></tr>
          <tr><td>Boş sonuç</td><td>{comparison.current.emptyResults}</td><td>{comparison.previous.emptyResults}</td><td>—</td></tr>
          <tr><td>İşlem başarı oranı</td><td>{rate(comparison.current.successRate)}</td><td>{rate(comparison.previous.successRate)}</td><td>{comparison.successRateChangePoints == null ? '—' : `${comparison.successRateChangePoints > 0 ? '+' : ''}${comparison.successRateChangePoints.toFixed(1)} puan`}</td></tr>
          <tr><td>Ortalama süre</td><td>{seconds(comparison.current.latency.averageMs)}</td><td>{seconds(comparison.previous.latency.averageMs)}</td><td>{seconds(comparison.averageChangeMs)}</td></tr>
          <tr><td>P95</td><td>{seconds(comparison.current.latency.p95Ms)}</td><td>{seconds(comparison.previous.latency.p95Ms)}</td><td>{seconds(comparison.p95ChangeMs)}</td></tr>
          <tr><td>Süre örneği / eksik</td><td>{comparison.current.latency.sampleCount} / {comparison.current.latency.missingCount}</td><td>{comparison.previous.latency.sampleCount} / {comparison.previous.latency.missingCount}</td><td>—</td></tr>
        </tbody></table></div>
        <p className="gf-muted">Başarı oranında artış, sürelerde düşüş iyileşme yönündedir. Veri veya payda yoksa değer — gösterilir. Bunlar sonuç doğruluğunu kanıtlamaz; kayıtların şu an bilinen son durumları karşılaştırılır.</p>
      </> : <p>Takip numarası araması dönem sınırını aşar; dönem karşılaştırması uygulanmaz.</p>}
      {comparison?.retentionMayLimitHistory && <p className="dg-notice">Karşılaştırma aralığı teşhis saklama süresini aşıyor; eski dönem eksik olabilir.</p>}
      {(comparison?.legacyCount > 0 || view.legacyCount > 0) && <p className="dg-notice">Eski kayıtlar içeriliyor; tarihsel bağlam, inceleme ve hata ayrıntıları eksik olabilir.</p>}
    </section>
    <section className="gf-card dg-detail"><h2>İnceleme adayları</h2>
      <p>{view.reviewCandidateCount} aday · en fazla {view.reviewCandidateLimit} kayıt. Düzeltildi/doğrulandı olarak işaretlenenler dışarıdadır; eski kayıtlarda inceleme durumu bilinmez.</p>
      {view.reviewCandidates.map((row) => <p key={row.id}><button className="dg-link" onClick={() => onOpen(row.id)}>{row.question || row.id}</button> · {row.reasons.map((reason) => ({ failed: 'Başarısız', negative_feedback: 'Olumsuz oy', clarification: 'Açıklama gerekiyor', slow: 'Yavaş' })[reason]).join(', ')}<small className="dg-id">{row.companyId} / {row.connectionId}</small></p>)}
      {!view.reviewCandidateCount && <p>Bu filtrelerde inceleme adayı yok.</p>}
    </section>
  </>;
}
