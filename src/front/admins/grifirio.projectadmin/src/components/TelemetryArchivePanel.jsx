import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { getArchiveEvents, getArchiveStatus, saveArchiveSettings } from '../services/telemetryArchiveApi';
import { describeError } from '../services/authClient';
import ConfirmDialog from './ops/ConfirmDialog';

const localDate = (time) => new Date(time - new Date(time).getTimezoneOffset() * 60000).toISOString().slice(0, 16);
const date = (value) => new Date(value).toLocaleString('tr-TR', { timeZone: 'Europe/Istanbul' });
const names = { 'application.log': 'Uygulama kaydı', activity: 'Tamamlanmış işlem', metric: 'Metrik örneği', 'http.request': 'HTTP isteği',
  'bridge.connected': 'Bridge bağlandı', 'bridge.disconnected': 'Bridge bağlantısı koptu', 'bridge.query.started': 'Bridge sorgusu başladı',
  'bridge.query.completed': 'Bridge sorgusu tamamlandı', 'bridge.query.failed': 'Bridge sorgusu başarısız', 'bridge.dispatch': 'Bridge’e sorgu gönderildi',
  'query.correlation': 'Sorgu ilişkilendirmesi', 'python.job.started': 'Python işi başladı', 'python.job.completed': 'Python işi tamamlandı', 'python.job.failed': 'Python işi başarısız' };

export default function TelemetryArchivePanel() {
  const [params] = useSearchParams();
  const snapshot = useRef(null);
  const [filters, setFilters] = useState(() => ({ from: localDate(Date.now() - 86400000), until: localDate(Date.now() + 60000),
    service: '', instanceId: '', environment: '', version: '', companyId: '', bridgeId: '', kind: '', level: '', traceId: params.get('traceId') || '', diagnosticId: params.get('diagnosticId') || '', queryId: params.get('queryId') || '' }));
  const [page, setPage] = useState(1), [revision, setRevision] = useState(0);
  const [data, setData] = useState(null), [status, setStatus] = useState(null);
  const [loading, setLoading] = useState(false), [error, setError] = useState('');
  const [statusError, setStatusError] = useState('');
  const [draft, setDraft] = useState(null), [confirming, setConfirming] = useState(false), [saving, setSaving] = useState(false);
  const [message, setMessage] = useState('');
  const update = (key, value) => { snapshot.current = null; setFilters((current) => ({ ...current, [key]: value })); setPage(1); };
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true); setError(''); setData(null);
    const timer = setTimeout(async () => {
      try {
        if (!filters.from || !filters.until) throw new Error('Başlangıç ve bitiş zamanlarını seçin.');
        const response = await getArchiveEvents({ ...Object.fromEntries(Object.entries(filters).filter(([, value]) => value)),
          from: new Date(filters.from).toISOString(), until: new Date(filters.until).toISOString(), page, limit: 50, snapshot: snapshot.current ?? undefined }, controller.signal);
        if (!controller.signal.aborted) { setData(response); snapshot.current = response.snapshot; }
      } catch (failure) { if (!controller.signal.aborted) setError(describeError(failure)); }
      finally { if (!controller.signal.aborted) setLoading(false); }
    }, 250);
    return () => { clearTimeout(timer); controller.abort(); };
  }, [filters, page, revision]);
  useEffect(() => {
    const controller = new AbortController();
    getArchiveStatus(controller.signal).then((value) => { if (!controller.signal.aborted) { setStatus(value); setDraft(value.settings); setStatusError(''); } })
      .catch((failure) => { if (!controller.signal.aborted) { setStatus(null); setStatusError(describeError(failure)); } });
    return () => controller.abort();
  }, [revision]);
  async function save() {
    setConfirming(false); setSaving(true); setMessage('');
    try { await saveArchiveSettings(draft); setMessage('Arşiv politikası kaydedildi. Süre ve hacim dışındaki kayıtlar temizlikte kaldırılır.'); setRevision((value) => value + 1); }
    catch (failure) { setError(describeError(failure)); }
    finally { setSaving(false); }
  }
  return <div className="dg-page">
    <div className="dg-toolbar"><div><h2>Kalıcı Telemetri Arşivi</h2><p className="gf-muted">Servis yeniden başlatmalarından bağımsız geçmiş · saatler Europe/Istanbul</p></div>
      <button className="gf-btn" disabled={loading} onClick={() => { snapshot.current = null; setPage(1); setRevision((value) => value + 1); }}>Arşivi yenile</button></div>
    <p className="dg-notice">Yalnızca güvenli olay metadatası saklanır; mesaj gövdesi, SQL ve müşteri sonuçları gösterilmez. Metrikler her replica için birikimli örneklerdir; farklı zamanları toplayarak işlem sayısı hesaplamayın. Yeni kayıtlar için bitiş zamanını ileri alın.</p>
    {error && <p role="alert" className="dg-error">Arşiv verisi alınamadı: {error}</p>}
    {statusError && <p role="alert" className="dg-error">Arşiv durumu alınamadı: {statusError}</p>}
    {message && <p role="status" className="dg-notice">{message}</p>}
    {status && <section className="gf-card dg-detail"><div className="dg-toolbar"><strong>{status.count} kayıt · {(status.bytes / 1048576).toFixed(1)} MiB</strong>
      <span>Silinen: {status.loss.expired} süre · {status.loss.evicted} kapasite</span></div>
      <details><summary>Kaynakların son görülme ve kayıp bilgileri</summary><div className="dg-table-wrap"><table className="pa-table"><thead><tr><th>Kaynak / replica</th><th>Son alınma</th><th>Kaynakta düşen</th><th>Veri akışı</th></tr></thead>
        <tbody>{status.producers.map((source) => <tr key={`${source.producer}:${source.service}:${source.instanceId}`}><td>{source.service}<small className="dg-id">{source.instanceId}</small></td>
          <td>{date(source.receivedAt)}</td><td>{source.dropped}</td><td>{Date.now() - Date.parse(source.receivedAt) > 120000 ? 'Yeni veri yok; servis sağlığı ayrıca kontrol edilmeli' : 'Yakın zamanda veri alındı'}</td></tr>)}</tbody></table></div>
        {status.producers.length === 0 && <p>Henüz kayıt gönderen kaynak yok. Bu, servislerin sağlıklı olduğunu göstermez.</p>}</details>
      <details><summary>Saklama ve hacim politikası</summary>{draft && <form className="dg-form" onSubmit={(event) => { event.preventDefault(); setConfirming(true); }}>
        {[['retentionDays', 'Saklama süresi (gün)', 1, 30], ['maxRows', 'En fazla kayıt', 100, 100000], ['maxBytes', 'En fazla veri (bayt)', 1048576, 67108864]].map(([key, label, min, max]) =>
          <label key={key}>{label}<input type="number" min={min} max={max} required value={draft[key]} onChange={(event) => setDraft({ ...draft, [key]: Number(event.target.value) })} /></label>)}
        <button className="gf-btn" disabled={saving}>Arşiv politikasını kaydet</button></form>}</details>
    </section>}
    <div className="dg-filters">
      <label>Başlangıç<input type="datetime-local" value={filters.from} onChange={(event) => update('from', event.target.value)} /></label>
      <label>Bitiş<input type="datetime-local" value={filters.until} onChange={(event) => update('until', event.target.value)} /></label>
      <label>Arşiv servisi<select value={filters.service} onChange={(event) => update('service', event.target.value)}><option value="">Tüm servisler</option>
        {['gateway', 'identity', 'commerce', 'data-analysis', 'python', 'bridge'].map((service) => <option key={service}>{service}</option>)}</select></label>
      <label>Replica / instance<input value={filters.instanceId} onChange={(event) => update('instanceId', event.target.value)} /></label>
      {[['environment', 'Ortam'], ['version', 'Servis sürümü'], ['companyId', 'Arşiv firma kimliği'], ['bridgeId', 'Bridge kimliği']].map(([key, label]) => <label key={key}>{label}<input value={filters[key]} onChange={(event) => update(key, event.target.value)} /></label>)}
      <label>Kayıt türü<select value={filters.kind} onChange={(event) => update('kind', event.target.value)}><option value="">Tümü</option><option value="log">Log</option><option value="trace">Trace</option><option value="metric">Metrik</option></select></label>
      <label>Arşiv log seviyesi<select value={filters.level} onChange={(event) => update('level', event.target.value)}><option value="">Tümü</option>{['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'].map((level) => <option key={level}>{level}</option>)}</select></label>
      {[['traceId', 'Arşiv Trace ID'], ['diagnosticId', 'Takip numarası'], ['queryId', 'Sorgu numarası']].map(([key, label]) => <label key={key}>{label}<input value={filters[key]} onChange={(event) => update(key, event.target.value)} /></label>)}
    </div>
    <p className="gf-muted gf-text-sm">Tarih filtrelerinin saat dilimi: {Intl.DateTimeFormat().resolvedOptions().timeZone}. Listede saatler İstanbul saatidir.</p>
    {loading && <p role="status">Arşiv yükleniyor…</p>}
    {data && <><div className="gf-card dg-table-wrap"><table className="pa-table"><thead><tr><th>Zaman / kaynak</th><th>Olay</th><th>Durum / süre</th><th>İlişkiler</th></tr></thead>
      <tbody>{data.items.map((item) => <tr key={item.sequence}><td>{date(item.timestamp)}<small className="dg-id">{item.service} · {item.instanceId}</small><small>{item.environment} · {item.version}</small></td>
        <td>{names[item.name] || item.name}{item.category && <small className="dg-id">{item.category}</small>}{item.metric && <pre>{JSON.stringify(item.metric, null, 2)}</pre>}{item.exceptionType && <small className="dg-id">{item.exceptionType}</small>}</td>
        <td><span className={`gf-badge${item.level === 'Error' || item.status === 'failed' ? ' gf-badge--danger' : ''}`}>{item.level} · {item.status || '—'}</span><p>{item.durationMs == null ? '' : `${item.durationMs.toFixed(1)} ms`}</p></td>
        <td>{item.traceId && <button className="dg-link" onClick={() => update('traceId', item.traceId)}>{item.traceId}</button>}
          {item.diagnosticId && <p><Link to={`/diagnostics?id=${item.diagnosticId}`}>Sorgu ayrıntısı →</Link></p>}
          {!item.diagnosticId && item.queryId && <p><Link to={`/diagnostics?search=${item.queryId}`}>İlgili sorguyu bul →</Link></p>}
          {item.requestId && <small className="dg-id">Bridge isteği: {item.requestId}</small>}{item.companyId && <small className="dg-id">Firma: {item.companyId}</small>}</td></tr>)}</tbody></table>
        {data.total === 0 && <p className="gf-empty">Bu filtrelerde arşiv kaydı yok.</p>}</div>
      <div className="dg-toolbar"><span>{data.total} kayıt · Sayfa {page}</span><div><button className="gf-btn" disabled={page === 1 || loading} onClick={() => setPage(page - 1)}>Önceki arşiv sayfası</button>{' '}
        <button className="gf-btn" disabled={page * 50 >= data.total || loading} onClick={() => setPage(page + 1)}>Sonraki arşiv sayfası</button></div></div></>}
    {confirming && <ConfirmDialog title="Arşiv politikasını değiştir" message={`Saklama: ${status.settings.retentionDays} → ${draft.retentionDays} gün. Kayıt sınırı: ${status.settings.maxRows} → ${draft.maxRows}. Veri sınırı: ${(status.settings.maxBytes / 1048576).toFixed(0)} → ${(draft.maxBytes / 1048576).toFixed(0)} MiB. Sınır dışındaki telemetri kayıtları temizlikte kalıcı olarak silinebilir. Değişiklik denetim kaydına yazılır.`}
      confirmLabel="Politikayı uygula" onCancel={() => setConfirming(false)} onConfirm={save} />}
  </div>;
}
