import { useEffect, useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { getObservability, OBSERVABILITY_SERVICES } from '../services/observabilityApi';
import '../styles/observability.css';

const format = (value) => Number.isFinite(value)
  ? new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 4 }).format(value) : '—';
const time = (value) => value ? new Date(value).toLocaleString('tr-TR') : 'Henüz toplanmadı';
const tagsText = (tags) => Object.entries(tags || {}).map(([key, value]) => key + '=' + value).join(' · ');
const TABS = [{ id: 'metrics', label: 'Metrikler' }, { id: 'logs', label: 'Loglar' }, { id: 'traces', label: 'Dağıtık izler' }];

function ServiceCard({ result }) {
  const data = result.data;
  return (
    <article className="gf-card ob-service">
      <div className="ob-heading"><h2>{result.label}</h2>
        <span className={data ? 'ob-ok' : 'ob-error'}>{data ? 'Erişilebilir' : 'Veri alınamadı'}</span>
      </div>
      {result.error && <p className="ob-error" role="alert">{result.status === 401 || result.status === 403
        ? 'Oturum veya PLATFORM_ADMIN yetkisi gerekli. ' : ''}{result.error}</p>}
      {data && <>
        <dl className="ob-stats">
          <div><dt>Çalışma süresi</dt><dd>{format(data.runtime.uptimeSeconds / 60)} dk</dd></div>
          <div><dt>Bellek (RSS)</dt><dd>{format(data.runtime.workingSetBytes / 1048576)} MiB</dd></div>
          <div><dt>Yönetilen heap</dt><dd>{format(data.runtime.managedHeapBytes / 1048576)} MiB</dd></div>
          <div><dt>CPU (birikimli)</dt><dd>{format(data.runtime.cpuSeconds)} sn</dd></div>
          <div><dt>İş parçacığı</dt><dd>{format(data.runtime.threadCount)}</dd></div>
          <div><dt>Metrik serisi</dt><dd>{data.metrics.length}</dd></div>
        </dl>
        <p className="gf-muted gf-text-sm">{data.environment} · Örnek: {data.instanceId.slice(0, 8)}</p>
        <p className="gf-muted gf-text-sm">Yanıt: {time(data.capturedAt)}<br />Metrikler: {time(data.metricsCollectedAt)}</p>
        {(!data.metricsCollectedAt || Date.now() - Date.parse(data.metricsCollectedAt) > 30000) &&
          <p className="ob-warning">Metrik toplayıcısı bekleniyor veya veri güncel değil.</p>}
        {(data.droppedLogs > 0 || data.droppedTraces > 0 || data.omittedMetrics > 0) &&
          <p className="ob-warning">Kapasite nedeniyle çıkarılan: {data.droppedLogs} log, {data.droppedTraces} iz, {data.omittedMetrics} metrik serisi.</p>}
      </>}
    </article>
  );
}

function TelemetryTable({ rows, tab, onTrace }) {
  const [page, setPage] = useState(0);
  const lastPage = Math.max(0, Math.ceil(rows.length / 50) - 1);
  const currentPage = Math.min(page, lastPage);
  const visible = rows.slice(currentPage * 50, (currentPage + 1) * 50);
  const traceButton = (traceId) => traceId
    ? <button className="ob-trace" onClick={() => onTrace(traceId)} title="Bu izin log ve span kayıtlarını filtrele">{traceId}</button> : '—';
  return <>
    <div className="ob-table-wrap">
      <table className="ob-table">
        <caption>{TABS.find((item) => item.id === tab).label} · {rows.length} kayıt</caption>
        <thead><tr>{(tab === 'metrics'
          ? ['Servis / kaynak', 'Metrik / etiketler', 'Tür / birim', 'Değer / toplam', 'Örnek sayısı', 'Ortalama', 'Min / Maks']
          : tab === 'logs' ? ['Zaman / servis', 'Seviye', 'Kategori / şablon', 'Hata türü', 'Trace ID']
            : ['Zaman / servis', 'İşlem / etiketler', 'Tür / durum', 'Süre (ms)', 'Trace / Span / Parent'])
          .map((heading) => <th scope="col" key={heading}>{heading}</th>)}</tr></thead>
        <tbody>{visible.map((row, index) => <tr key={row.serviceId + ':' + index}>
          {tab === 'metrics' ? <>
            <td>{row.serviceLabel}<small>{row.meter}</small></td>
            <td><code>{row.name}</code><small>{tagsText(row.tags)}</small></td>
            <td>{row.kind}<small>{row.unit || 'birimsiz'}</small></td>
            <td>{format(row.value ?? row.sum)}</td><td>{format(row.count)}</td>
            <td>{format(row.count > 0 && row.sum != null ? row.sum / row.count : null)}</td>
            <td>{format(row.min)} / {format(row.max)}</td>
          </> : tab === 'logs' ? <>
            <td>{time(row.timestamp)}<small>{row.serviceLabel}</small></td>
            <td className={['Error', 'Critical'].includes(row.level) ? 'ob-error' : ''}>{row.level}</td>
            <td><small>{row.category}</small><code>{row.message}</code></td>
            <td>{row.exceptionType || '—'}</td><td>{traceButton(row.traceId)}</td>
          </> : <>
            <td>{time(row.timestamp)}<small>{row.serviceLabel}</small></td>
            <td><code>{row.name}</code><small>{tagsText(row.tags)}</small></td>
            <td className={row.status === 'Error' ? 'ob-error' : ''}>{row.kind}<small>{row.status}</small></td>
            <td>{format(row.durationMs)}</td><td>{traceButton(row.traceId)}<small>Span: {row.spanId}<br />Parent: {row.parentSpanId || '—'}</small></td>
          </>}
        </tr>)}</tbody>
      </table>
      {rows.length === 0 && <p className="gf-empty">Seçili filtrelerde kayıt yok. Servis erişimini, zaman aralığını ve henüz trafik oluşup oluşmadığını kontrol edin.</p>}
    </div>
    {lastPage > 0 && <div className="ob-pagination">
      <button className="gf-btn gf-btn--sm" disabled={currentPage === 0} onClick={() => setPage(currentPage - 1)}>Önceki</button>
      <span>{currentPage + 1} / {lastPage + 1}</span>
      <button className="gf-btn gf-btn--sm" disabled={currentPage === lastPage} onClick={() => setPage(currentPage + 1)}>Sonraki</button>
    </div>}
  </>;
}

export default function ObservabilityPage() {
  const [results, setResults] = useState([]);
  const [loading, setLoading] = useState(true);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [revision, setRevision] = useState(0);
  const [minutes, setMinutes] = useState(15);
  const [level, setLevel] = useState('');
  const [traceId, setTraceId] = useState('');
  const [traceInput, setTraceInput] = useState('');
  const [service, setService] = useState('');
  const [search, setSearch] = useState('');
  const [tab, setTab] = useState('metrics');

  useEffect(() => {
    const controller = new AbortController();
    let timer;
    async function refresh() {
      setLoading(true);
      const response = await getObservability({ minutes, level: level || undefined, traceId: traceId || undefined, limit: 200 }, controller.signal);
      if (controller.signal.aborted) return;
      setResults(response);
      setLoading(false);
      if (autoRefresh) timer = setTimeout(refresh, 10000);
    }
    refresh();
    return () => { controller.abort(); clearTimeout(timer); };
  }, [minutes, level, traceId, autoRefresh, revision]);

  const selected = results.filter((result) => !service || result.id === service);
  const rows = useMemo(() => {
    const needle = search.toLocaleLowerCase('tr-TR');
    const collected = results.filter((result) => result.data && (!service || result.id === service))
      .flatMap((result) => result.data[tab].map((row) => ({ ...row, serviceId: result.id, serviceLabel: result.label })))
      .filter((row) => JSON.stringify(row).toLocaleLowerCase('tr-TR').includes(needle));
    return collected.sort((left, right) => tab === 'metrics'
      ? left.name.localeCompare(right.name) : Date.parse(right.timestamp) - Date.parse(left.timestamp));
  }, [results, service, search, tab]);

  const selectTrace = (value) => { setTraceId(value); setTraceInput(value); setTab('traces'); };
  const matches = selected.reduce((total, result) => total + (result.data?.[tab === 'logs' ? 'matchingLogs' : 'matchingTraces'] || 0), 0);

  return <div className="gf-page ob-page">
    <header className="gf-page-header">
      <div><h1>Sistem gözlemlenebilirliği</h1><p className="gf-muted">Metrikler, uygulama logları ve dağıtık izler · yalnızca platform yönetimi</p></div>
      <div className="ob-actions">
        <label><input type="checkbox" checked={autoRefresh} onChange={(event) => setAutoRefresh(event.target.checked)} /> 10 sn yenile</label>
        <button className="gf-btn" disabled={loading} onClick={() => setRevision((value) => value + 1)}>Yenile</button>
      </div>
    </header>
    <aside className="gf-card ob-notice">
      <strong>Canlı, süreç içi görünüm — kalıcı arşiv değil.</strong>
      <p>Gateway, Identity, Commerce ve Data Analysis servislerinin mevcut OpenTelemetry verileri gösterilir.
        Her yanıtta tek servis örneği vardır; çoklu replikaların toplamı değildir. Yeniden başlatmada kayıtlar sıfırlanır.</p>
      <p>Log ve izler en fazla 30 dakika / 1.000 kayıt tutulur, servis başına en yeni 200 eşleşme gösterilir.
        Metrikler 10 saniyede toplanır; sayaç ve histogramlar süreç başlangıcından itibaren birikimlidir, zaman filtresinden etkilenmez.</p>
      <p>Log parametreleri, SQL, istek gövdeleri ve hata ayrıntıları aktarılmaz; yapılandırılmış mesaj şablonu gösterilir.
        Harici altyapı/konteyner logları bu kapsamda değildir. <Link to="/services">Servis kontrolleri</Link> · <Link to="/semantic">Semantik ve değerlendirme metrikleri</Link></p>
    </aside>
    <div className="ob-filters">
      <label>Servis<select className="gf-select" value={service} onChange={(event) => setService(event.target.value)}>
        <option value="">Tüm servisler</option>{OBSERVABILITY_SERVICES.map((item) => <option value={item.id} key={item.id}>{item.label}</option>)}
      </select></label>
      <label>Log / iz aralığı<select className="gf-select" value={minutes} onChange={(event) => setMinutes(Number(event.target.value))}>
        {[5, 15, 30].map((value) => <option value={value} key={value}>Son {value} dakika</option>)}
      </select></label>
      <label>Log seviyesi<select className="gf-select" value={level} onChange={(event) => setLevel(event.target.value)}>
        <option value="">Tüm seviyeler</option>{['Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical'].map((value) => <option key={value}>{value}</option>)}
      </select></label>
      <label>Görünen kayıtlarda ara<input className="gf-input" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Metrik adı, etiket, kategori…" /></label>
    </div>
    <form className="ob-trace-filter" onSubmit={(event) => { event.preventDefault(); setTraceId(traceInput.trim().toLowerCase()); }}>
      <label>Trace ID<input className="gf-input" value={traceInput} onChange={(event) => setTraceInput(event.target.value)} placeholder="32 karakterlik trace kimliği" pattern="[a-fA-F0-9]{32}" maxLength={32} /></label>
      <button className="gf-btn" type="submit">Log ve izleri eşleştir</button>
      {(traceId || traceInput) && <button className="gf-btn" type="button" onClick={() => { setTraceId(''); setTraceInput(''); }}>Temizle</button>}
    </form>
    <div className="ob-status" role="status">{loading ? 'Veriler yükleniyor; varsa önceki görüntü gösteriliyor…' : selected.filter((result) => result.data).length + ' / ' + selected.length + ' servisten veri alındı.'}</div>
    <div className="ob-grid">{selected.map((result) => <ServiceCard result={result} key={result.id} />)}</div>
    <div className="ob-tabs" aria-label="Telemetri türü">{TABS.map((item) => <button className="gf-btn" aria-pressed={tab === item.id}
      key={item.id} onClick={() => setTab(item.id)}>{item.label}</button>)}</div>
    {tab !== 'metrics' && <p className="gf-muted">Tamponda {matches} eşleşme. Servis başına en yeni 200 kayıt üzerinde arama yapılır.{traceId && ' Trace filtresi: ' + traceId}</p>}
    {tab === 'traces' && <p className="gf-muted">Yalnızca tamamlanmış ve örneklenmiş span kayıtları; Unset, açık bir başarı/hata durumu işaretlenmediği anlamına gelir.</p>}
    <TelemetryTable key={[tab, service, search, minutes, level, traceId].join(':')} rows={rows} tab={tab} onTrace={selectTrace} />
  </div>;
}
