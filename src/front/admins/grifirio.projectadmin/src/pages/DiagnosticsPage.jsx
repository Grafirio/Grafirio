import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { describeError } from '../services/authClient';
import { createDiagnosticLoader } from '../utils/diagnosticLoader';
import { comparisonRows, createDiagnosticTest, deleteDiagnosticTest, ERRORS, getDiagnostic, getDiagnosticEvents, getDiagnostics,
  getDiagnosticSettings, getDiagnosticTests, REVIEWS, runDiagnosticTest,
  saveDiagnosticSettings, saveReview, STAGES, STATUS, updateDiagnosticTest } from '../services/diagnosticApi';
import '../styles/diagnostics.css';
import DiagnosticsOverview from '../components/DiagnosticsOverview';
import PriorityIssues from '../components/PriorityIssues';
import ConfirmDialog from '../components/ops/ConfirmDialog';

const date = (value) => value ? new Date(value).toLocaleString('tr-TR', { timeZone: 'Europe/Istanbul' }) : '—';
const seconds = (value) => `${((value ?? 0) / 1000).toFixed(1)} sn`;
const json = (value) => JSON.stringify(value ?? {}, null, 2);
function JsonDetails({ title, value, open = false }) {
  return <details className="dg-json" open={open}><summary>{title}</summary><pre>{json(value)}</pre></details>;
}

function DiagnosticDetail({ row, onRefresh, onError, onTestCreated }) {
  const [review, setReview] = useState(row.reviewStatus);
  const [note, setNote] = useState(row.reviewNote);
  const [name, setName] = useState('');
  const [expected, setExpected] = useState(json(Object.fromEntries(Object.entries(row.parameters ?? {})
    .filter(([key]) => ['analysis_type', 'target_table', 'target_column', 'group_by', 'aggregation', 'filters'].includes(key)))));
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState('');
  async function act(operation, success) {
    setBusy(true); setMessage('');
    try { await operation(); setMessage(success); }
    catch (e) { onError(describeError(e)); }
    finally { setBusy(false); }
  }
  return <section className="gf-card dg-detail">
    <h2>Sorgu ayrıntısı</h2><p className="dg-question">{row.question}</p>
    <Link className="gf-btn" to={`/observability?source=archive&diagnosticId=${encodeURIComponent(row.id)}`}>İlişkili telemetri kayıtları →</Link>
    <dl className="dg-meta">
      <div><dt>Takip numarası</dt><dd>{row.id}</dd></div>
      <div><dt>Sorgu numarası</dt><dd>{row.queryId ?? 'Çalıştırma başlamadı'}</dd></div>
      <div><dt>Firma / bağlantı</dt><dd>{row.companyId} / {row.connectionId}</dd></div>
      <div><dt>Sonuç / aşama</dt><dd>{STATUS[row.status] ?? row.status} / {STAGES[row.stage] ?? row.stage}</dd></div>
      <div><dt>Model / prompt sürümü</dt><dd>{row.model} / {row.promptVersion || 'Kaydedilmemiş'}</dd></div>
      <div><dt>Uygulama sürümü</dt><dd>{row.buildVersion || 'Kaydedilmemiş'}</dd></div>
      <div><dt>Başlangıç / bitiş</dt><dd>{date(row.createdAt)} / {date(row.completedAt)}</dd></div>
      <div><dt>Toplam süre</dt><dd>{seconds(row.durationMs)}</dd></div>
      <div><dt>Kullanıcı geri bildirimi</dt><dd>{row.feedbackRating === -1 ? 'Olumsuz' : row.feedbackRating === 1 ? 'Olumlu' : 'Henüz yok'}</dd></div>
    </dl>
    {row.legacy && <p className="dg-notice">Eski kayıt: o tarihte tutulmayan bağlam ve sürüm bilgileri mevcut değil.</p>}
    {row.error && <p className="dg-error"><strong>{ERRORS[row.errorCode] ?? 'Hata'}: </strong>{row.error}</p>}
    <ol className="dg-timeline">{(row.events ?? []).map((event, i) => <li key={`${event.at}-${i}`}>
      <strong>{STAGES[event.stage] ?? event.stage}</strong> <span>{date(event.at)}</span></li>)}
      {row.queryId && <li><strong>{STATUS[row.status] ?? row.status}</strong> <span>{date(row.completedAt)}</span></li>}</ol>
    <JsonDetails title="Üretilen sorgu parametreleri" value={row.parameters} open />
    <JsonDetails title="SQL, filtreler ve hesaplama izi" value={row.audit} />
    <JsonDetails title="Konuşma bağlamı ve sözlük" value={row.context} />
    {!row.legacy && <>
      <h3>Sorun takibi</h3><div className="dg-form">
        <label>İnceleme durumu<select value={review} onChange={(e) => setReview(e.target.value)}>
          {Object.entries(REVIEWS).map(([key, label]) => <option value={key} key={key}>{label}</option>)}</select></label>
        <label>Çözüm / inceleme notu<textarea value={note} maxLength={2000} onChange={(e) => setNote(e.target.value)} /></label>
        <button className="gf-btn gf-btn--primary" disabled={busy} onClick={() => act(async () => {
          await saveReview(row.id, { status: review, note }); await onRefresh();
        }, 'İnceleme kaydedildi.')}>İncelemeyi kaydet</button>
      </div>
      {row.context?.dictionary && <details className="dg-json"><summary>Doğrulama örneği oluştur</summary>
        <p>Beklenen alanları kontrol edip düzeltin. Sonraki çalıştırmalar bu beklentilerle karşılaştırılır. Testte kaydedilen ve maskelenmiş bağlam kullanılır.</p>
        <div className="dg-form"><label>Test adı<input value={name} maxLength={150} onChange={(e) => setName(e.target.value)} /></label>
          <label>Beklenen parametreler<textarea className="dg-code" rows={10} value={expected} onChange={(e) => setExpected(e.target.value)} /></label>
          <button disabled={busy || !name.trim()} className="gf-btn gf-btn--primary" onClick={() => act(async () => {
            await createDiagnosticTest({ sourceId: row.id, name, expected: JSON.parse(expected) }); onTestCreated();
          }, 'Doğrulama örneği oluşturuldu.')}>Test örneğini kaydet</button></div>
      </details>}
    </>}
    {message && <p role="status" className="dg-notice">{message}</p>}
  </section>;
}

function Compare({ rows }) {
  if (rows.length !== 2) return <p className="gf-muted">Karşılaştırmak için listeden iki sorgu seçin.</p>;
  const [a, b] = rows;
  const values = (row) => ({ status: row.status, stage: row.stage, model: row.model, promptVersion: row.promptVersion,
    buildVersion: row.buildVersion, dictionaryHash: row.dictionaryHash, contextHash: row.contextHash,
    referenceDate: row.context?.date, conversation: row.context?.conversation, parameters: row.parameters, audit: row.audit });
  return <section className="gf-card dg-detail"><h2>İki sorguyu karşılaştır</h2>
    <p>{a.question}</p><p>{b.question}</p>
    <p className="dg-notice">{a.contextHash && a.contextHash === b.contextHash ? 'Soru, bağlam ve sürüm bilgileri eşleşiyor.' : 'Bağlamlar farklı veya eski kayıtta bağlam eksik. Sonuç farkı tek başına tutarsızlık kanıtı değildir.'}</p>
    <div className="dg-table-wrap"><table className="pa-table"><thead><tr><th>Alan</th><th>Birinci sorgu</th><th>İkinci sorgu</th></tr></thead>
      <tbody>{comparisonRows(values(a), values(b)).map((r) => <tr key={r.key} className={r.changed ? 'dg-changed' : ''}>
        <td>{r.key}{r.changed && <strong> · Farklı</strong>}</td><td><pre>{r.left}</pre></td><td><pre>{r.right}</pre></td></tr>)}</tbody></table></div>
  </section>;
}

function TestsPanel({ onError, revision }) {
  const [tests, setTests] = useState([]); const [running, setRunning] = useState(null);
  const [editing, setEditing] = useState(null);
  const load = useCallback(() => getDiagnosticTests().then(setTests).catch((e) => onError(describeError(e))), [onError]);
  useEffect(() => { load(); }, [load, revision]);
  async function run(id) {
    setRunning(id);
    try { await runDiagnosticTest(id); await load(); } catch (e) { onError(describeError(e)); } finally { setRunning(null); }
  }
  async function runAll() {
    setRunning('all');
    try { for (const test of tests) await runDiagnosticTest(test.id); await load(); }
    catch (e) { onError(describeError(e)); await load(); } finally { setRunning(null); }
  }
  async function saveTest() {
    setRunning(editing.id);
    try { await updateDiagnosticTest(editing.id, { name: editing.name, expected: JSON.parse(editing.expected) }); setEditing(null); await load(); }
    catch (e) { onError(describeError(e)); } finally { setRunning(null); }
  }
  async function removeTest(test) {
    if (!window.confirm(`“${test.name}” test örneği ve kontrol geçmişi silinsin mi?`)) return;
    setRunning(test.id);
    try { await deleteDiagnosticTest(test.id); setEditing(null); await load(); }
    catch (e) { onError(describeError(e)); } finally { setRunning(null); }
  }
  return <section className="gf-card dg-detail"><h2>Doğrulama örnekleri</h2>
    <p>Kaydedilen soru ve tarih bağlamıyla güncel modelin çevirisi kontrol edilir. Bu kontrol SQL çalıştırmaz; model kullanımı ücret oluşturabilir. Veri ve tahmin doğruluğu ayrıca değerlendirilmelidir.</p>
    {tests.length > 0 && <button className="gf-btn" disabled={!!running} onClick={runAll}>{running === 'all' ? 'Örnekler sırayla kontrol ediliyor…' : 'Tüm örnekleri kontrol et'}</button>}
    {tests.length === 0 && <p className="gf-muted">Henüz örnek yok. Sorgu ayrıntısından beklenen parametreleri tanımlayarak oluşturabilirsiniz.</p>}
    {tests.map((test) => {
      const runs = JSON.parse(test.runsJson || '[]'); const last = runs[0];
      const changedExpectation = last && JSON.stringify(last.expected) !== JSON.stringify(JSON.parse(test.expectedJson));
      return <article className="dg-test" key={test.id}><div className="dg-toolbar"><h3>{test.name}</h3>
        <span>{changedExpectation ? 'Beklenti değişti; yeniden kontrol edin' : last ? last.passed ? 'Başarılı' : 'Beklenti karşılanmadı' : 'Henüz çalıştırılmadı'}</span>
        <button className="gf-btn" disabled={!!running} onClick={() => run(test.id)}>{running === test.id ? 'Kontrol ediliyor…' : 'Yeniden kontrol et'}</button></div>
        <div className="dg-toolbar"><button className="gf-btn" disabled={!!running} onClick={() => setEditing({ id: test.id, name: test.name, expected: json(JSON.parse(test.expectedJson)) })}>Beklentiyi düzenle</button>
          <button className="gf-btn" disabled={!!running} onClick={() => removeTest(test)}>Örneği sil</button></div>
        {editing?.id === test.id && <div className="dg-form"><label>Test adı<input value={editing.name} maxLength={150} onChange={(e) => setEditing({ ...editing, name: e.target.value })} /></label>
          <label>Beklenen parametreler<textarea className="dg-code" rows={8} value={editing.expected} onChange={(e) => setEditing({ ...editing, expected: e.target.value })} /></label>
          <div><button className="gf-btn gf-btn--primary" disabled={!!running} onClick={saveTest}>Kaydet</button>{' '}
            <button className="gf-btn" disabled={!!running} onClick={() => setEditing(null)}>Vazgeç</button></div></div>}
        <JsonDetails title="Beklenen parametreler" value={JSON.parse(test.expectedJson)} />
        {last && <JsonDetails title={`Son kontrol · ${date(test.lastRunAt)}`} value={last} open={!last.passed} />}
        {runs.length > 1 && <JsonDetails title="Önceki kontroller" value={runs.slice(1)} />}</article>;
    })}
  </section>;
}

function SettingsPanel({ onError }) {
  const [settings, setSettings] = useState(null); const [events, setEvents] = useState([]);
  const [busy, setBusy] = useState(false); const [message, setMessage] = useState('');
  const [confirming, setConfirming] = useState(false);
  useEffect(() => { let active = true;
    Promise.all([getDiagnosticSettings(), getDiagnosticEvents()]).then(([s, e]) => { if (active) { setSettings(s); setEvents(e); } })
      .catch((e) => onError(describeError(e))); return () => { active = false; };
  }, [onError]);
  async function save() {
    setBusy(true); setMessage('');
    try { setSettings(await saveDiagnosticSettings(settings)); setEvents(await getDiagnosticEvents()); setMessage('Ayarlar kaydedildi.'); }
    catch (e) { onError(describeError(e)); } finally { setBusy(false); }
  }
  return <section className="gf-card dg-detail"><h2>Saklama ve uyarı ayarları</h2>
    {settings && <div className="dg-form">{[
      ['retentionDays', 'Teşhis kayıtlarını saklama (gün)', 7, 365], ['repeatedErrorThreshold', 'Tekrarlanan hata eşiği', 2, 100],
      ['slowQuerySeconds', 'Yavaş sorgu eşiği (saniye)', 1, 3600],
    ].map(([key, label, min, max]) => <label key={key}>{label}<input type="number" min={min} max={max} value={settings[key]}
      onChange={(e) => setSettings({ ...settings, [key]: Number(e.target.value) })} /></label>)}
      <p>Süreyi geçen teşhis kayıtları altı saatte bir temizlenir. Kaydedilmiş test örnekleri ve mevcut kullanıcı sorgu geçmişi korunur. E-posta ve 11 haneli numaralar maskelenir; ayrıntılar platform yöneticilerine açıktır.</p>
      <button className="gf-btn gf-btn--primary" disabled={busy} onClick={() => setConfirming(true)}>Ayarları kaydet</button></div>}
    {confirming && <ConfirmDialog title="Saklama ve uyarı ayarlarını değiştir" message={`Saklama süresi ${settings.retentionDays} gün olacak. Süre dışında kalan teşhis kayıtları sonraki temizlikte silinebilir. Uyarı eşikleri de güncellenecek.`}
      confirmLabel="Ayarları uygula" onCancel={() => setConfirming(false)} onConfirm={() => { setConfirming(false); save(); }} />}
    {message && <p role="status">{message}</p>}
    <h3>Son yönetim işlemleri</h3><div className="dg-table-wrap"><table className="pa-table"><thead><tr><th>Tarih</th><th>İşlem</th><th>Yönetici</th><th>Ayrıntı</th></tr></thead>
      <tbody>{events.map((e) => <tr key={e.id}><td>{date(e.createdAt)}</td><td>{e.action}</td><td>{e.actor}</td><td>{e.detailJson}</td></tr>)}</tbody></table></div>
  </section>;
}

export default function DiagnosticsPage() {
  const [params, setParams] = useSearchParams();
  const [tab, setTab] = useState('queries'); const [days, setDays] = useState(() => [7, 30, 90, 365].includes(Number(params.get('days'))) ? Number(params.get('days')) : 30);
  const [status, setStatus] = useState(params.get('status') || ''); const [errorCode, setErrorCode] = useState(params.get('errorCode') || '');
  const [search, setSearch] = useState(params.get('search') || ''); const [companyId, setCompanyId] = useState(params.get('companyId') || ''); const [connectionId, setConnectionId] = useState(params.get('connectionId') || '');
  const [page, setPage] = useState(1); const [data, setData] = useState(null); const [summary, setSummary] = useState(null);
  const [error, setError] = useState(''); const [loading, setLoading] = useState(false);
  const [detail, setDetail] = useState(null); const [compare, setCompare] = useState([]); const [testRevision, setTestRevision] = useState(0);
  const loader = useRef(null); const detailSequence = useRef(0);
  if (!loader.current) loader.current = createDiagnosticLoader({
    request: getDiagnostics,
    onData: (list) => { setData(list); setSummary(list.summary); },
    onError: (failure) => setError(failure ? describeError(failure) : ''),
    onLoading: setLoading,
  });
  const selectedId = params.get('id');
  const openDiagnostic = (id) => {
    setTab('queries');
    setParams((current) => { current.set('id', id); return current; });
  };
  const load = useCallback(() => loader.current.load({
    days, status, errorCode, search, companyId, connectionId: connectionId || undefined, page,
  }), [days, status, errorCode, search, companyId, connectionId, page]);
  useEffect(() => {
    const timer = setTimeout(load, 300);
    return () => { clearTimeout(timer); loader.current.invalidate(); };
  }, [load]);
  const loadDetail = useCallback(async () => {
    const sequence = ++detailSequence.current;
    if (!selectedId) { setDetail(null); return; }
    try { const row = await getDiagnostic(selectedId); if (sequence === detailSequence.current) setDetail(row); }
    catch (e) { if (sequence === detailSequence.current) { setDetail(null); setError(describeError(e)); } }
  }, [selectedId]);
  useEffect(() => { loadDetail(); }, [loadDetail]);
  async function selectCompare(id) {
    if (compare.some((r) => r.id === id)) { setCompare(compare.filter((r) => r.id !== id)); return; }
    if (compare.length >= 2) return;
    try { const row = await getDiagnostic(id); setCompare((current) => current.length < 2 ? [...current, row] : current); }
    catch (e) { setError(describeError(e)); }
  }
  return <div className="gf-page dg-page">
    <div className="dg-toolbar"><div><h1 className="gf-page-title">Sorgu Kalitesi</h1><p className="gf-muted">Hataları inceleyin, sorguları karşılaştırın ve düzeltmeleri doğrulayın.</p></div>
      <button className="gf-btn" disabled={loading} onClick={() => { load(); loadDetail(); }}>Yenile</button></div>
    <nav className="dg-tabs" aria-label="Sorgu izleme bölümleri">{[['queries', 'Sorgular'], ['analysis', 'Analiz'], ['compare', `Karşılaştır (${compare.length}/2)`], ['tests', 'Doğrulama örnekleri'], ['settings', 'Ayarlar ve yönetim izi']]
      .map(([key, label]) => <button key={key} className={`gf-btn${tab === key ? ' gf-btn--primary' : ''}`} onClick={() => setTab(key)}>{label}</button>)}</nav>
    {error && <p className="dg-error" role="alert">{error}</p>}
    {(tab === 'queries' || tab === 'analysis') && <div className="dg-toolbar"><label>Dönem<select value={days} onChange={(e) => { setDays(Number(e.target.value)); setPage(1); }}>
        {[7, 30, 90, 365].map((d) => <option key={d} value={d}>Son {d} gün</option>)}</select></label><span className="gf-muted gf-text-sm">Özet ve analiz mevcut filtreleri kullanır.</span></div>}
    {tab === 'queries' && <>
      {summary && <>
        <div className="dg-stats">{[['Toplam istek', summary.total], ['Başarısız', summary.failed],
          ['Başarı oranı', summary.successRate == null ? '—' : `%${(summary.successRate * 100).toFixed(1)}`],
          ['Yanıt süresi · %95', summary.visibility?.latency.p95Ms == null ? '—' : seconds(summary.visibility.latency.p95Ms)]].map(([label, value]) => <div className="gf-card dg-stat" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>
        <p className="gf-muted">Başarı oranı = tamamlandı / (tamamlandı + başarısız). Boş sonuçlar başarısızların alt kümesidir; bekleyen, iptal ve açıklama isteyen istekler orana dahil değildir. Bu oran sonuç doğruluğunu ölçmez. P95, tamamlanan, başarısız, iptal edilen ve açıklama isteyen kayıtların sürelerinden hesaplanır.</p>
        <PriorityIssues summary={summary} onOpen={openDiagnostic} onFilter={(patch) => {
          if ('status' in patch) setStatus(patch.status);
          if ('errorCode' in patch) setErrorCode(patch.errorCode);
          setPage(1);
        }} />
      </>}
      <div className="dg-filters">
        <label>Soru veya takip numarası<input value={search} onChange={(e) => { setSearch(e.target.value); setPage(1); }} placeholder="Ara…" /></label>
        <label>Durum<select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}><option value="">Tümü</option>{status && !STATUS[status] && <option value={status}>{status || 'Bilinmeyen'}</option>}{Object.entries(STATUS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Hata türü<select value={errorCode} onChange={(e) => { setErrorCode(e.target.value); setPage(1); }}><option value="">Tümü</option>{errorCode && !ERRORS[errorCode] && <option value={errorCode}>{errorCode}</option>}{Object.entries(ERRORS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
        <label>Firma numarası<input value={companyId} onChange={(e) => { setCompanyId(e.target.value); setPage(1); }} /></label>
        <label>Bağlantı numarası<input value={connectionId} onChange={(e) => { setConnectionId(e.target.value); setPage(1); }} /></label>
      </div>
      {data?.sampled && <p className="dg-notice">Bu dönem için en yeni {data.sampleLimit} kayıt gösteriliyor. Daha kısa dönem seçerek incelemeyi daraltın.</p>}
      {loading && <p role="status">Kayıtlar yükleniyor…</p>}
      <div className="gf-card dg-table-wrap"><table className="pa-table"><thead><tr><th>Karşılaştır</th><th>Tarih / takip</th><th>Soru</th><th>Sonuç</th><th>İnceleme</th><th>Süre</th></tr></thead>
        <tbody>{data?.items?.map((row) => <tr key={row.id}><td><input type="checkbox" aria-label={`${row.question} sorgusunu karşılaştır`} checked={compare.some((r) => r.id === row.id)} disabled={compare.length === 2 && !compare.some((r) => r.id === row.id)} onChange={() => selectCompare(row.id)} /></td>
          <td>{date(row.createdAt)}<small className="dg-id">{row.id}</small></td>
          <td><button className="dg-link" onClick={() => openDiagnostic(row.id)}>{row.question}</button>{row.legacy && <small className="dg-id">Eski kayıt</small>}</td>
          <td>{STATUS[row.status] ?? row.status}{row.errorCode && <small className="dg-id">{ERRORS[row.errorCode] ?? row.errorCode}</small>}</td>
          <td>{row.legacy ? '—' : REVIEWS[row.reviewStatus]}</td><td>{seconds(row.durationMs)}</td></tr>)}</tbody></table>
        {data && data.total === 0 && <p className="dg-detail">Bu filtrelere uygun kayıt yok.</p>}
      </div>
      <div className="dg-toolbar"><span>{data?.total ?? 0} kayıt · Sayfa {page}</span><div>
        <button className="gf-btn" disabled={page <= 1 || loading} onClick={() => setPage(page - 1)}>Önceki</button>{' '}
        <button className="gf-btn" disabled={page * 50 >= (data?.total ?? 0) || loading} onClick={() => setPage(page + 1)}>Sonraki</button></div></div>
      {detail && <DiagnosticDetail key={detail.id} row={detail} onRefresh={async () => { await loadDetail(); await load(); }} onError={setError} onTestCreated={() => setTestRevision((r) => r + 1)} />}
    </>}
    {tab === 'analysis' && summary && <>
        <div className="dg-stats">{[['Tamamlandı', summary.completed], ['Boş sonuç · başarısızların içinde', summary.emptyResults],
          ['Olumsuz geri bildirim', `${summary.negativeFeedback ?? 0} / ${summary.ratedCount ?? 0} oy`],
          ['Ortalama yanıt', summary.visibility?.latency.averageMs == null ? '—' : seconds(summary.visibility.latency.averageMs)]].map(([label, value]) => <div className="gf-card dg-stat" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>
        <DiagnosticsOverview summary={summary} onOpen={openDiagnostic} onFilter={(patch) => {
          if ('companyId' in patch) setCompanyId(patch.companyId);
          if ('connectionId' in patch) setConnectionId(patch.connectionId);
          if ('status' in patch) setStatus(patch.status);
          if ('errorCode' in patch) setErrorCode(patch.errorCode);
          setPage(1);
          setTab('queries');
        }} />
        <section className="gf-card dg-detail"><h2>Tekrarlanan sorunlar ve sorgular</h2>
          {summary.repeatedErrors?.map((group) => <p className="dg-notice" key={group.code}>
            <button className="dg-link" onClick={() => { setErrorCode(group.code); setPage(1); setTab('queries'); }}>{ERRORS[group.code] ?? group.code}: {group.count} tekrar</button></p>)}
          {summary.inconsistent?.map((group, i) => <p className="dg-notice" key={i}>Aynı bağlamda farklı sonuç: {group.question} · {group.count} istek{' '}
            <button className="dg-link" onClick={async () => { try { setCompare(await Promise.all(group.ids.slice(0, 2).map(getDiagnostic))); setTab('compare'); } catch (e) { setError(describeError(e)); } }}>Karşılaştır</button></p>)}
          {summary.slowQueries?.map((row) => <p key={row.id}><button className="dg-link" onClick={() => openDiagnostic(row.id)}>{row.question}</button> · {seconds(row.durationMs)}</p>)}
          {summary.negativeQueries?.map((row) => <p key={`feedback-${row.id}`}>Olumsuz geri bildirim: <button className="dg-link" onClick={() => openDiagnostic(row.id)}>{row.question}</button></p>)}
          {summary.repeatedQuestions?.map((group, i) => <p key={`retry-${i}`}>Tekrarlanan soru · {group.count} istek: <button className="dg-link" onClick={() => { setSearch(group.question); setPage(1); setTab('queries'); }}>{group.question}</button></p>)}
          {!summary.repeatedErrors?.length && !summary.inconsistent?.length && !summary.slowQueries?.length && !summary.negativeQueries?.length && !summary.repeatedQuestions?.length && <p className="gf-muted">Seçilen dönemde uyarı eşiğini aşan bir durum yok.</p>}
        </section></>}
    {tab === 'compare' && <Compare rows={compare} />}
    {tab === 'tests' && <TestsPanel onError={setError} revision={testRevision} />}
    {tab === 'settings' && <SettingsPanel onError={setError} />}
  </div>;
}
