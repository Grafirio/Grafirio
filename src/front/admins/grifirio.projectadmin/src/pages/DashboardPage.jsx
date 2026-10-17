import { useEffect, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { getDiagnosticSummary } from '../services/diagnosticApi';
import { getOpsStatus, getAuditLog } from '../services/opsApi';
import { CAPABILITIES, getRuns, metricOf, delta } from '../services/semanticApi';
import { describeError } from '../services/authClient';
import PriorityIssues from '../components/PriorityIssues';
import { formatDateTime } from '../utils/opsView';

export default function DashboardPage() {
  const navigate = useNavigate();
  const [revision, setRevision] = useState(0);
  const [state, setState] = useState({ loading: true, sources: {} });
  useEffect(() => {
    let active = true;
    const sources = {
      ops: () => getOpsStatus(), queries: () => getDiagnosticSummary({ days: 7 }), audit: () => getAuditLog(),
      benchmark: async () => {
        const results = await Promise.allSettled(CAPABILITIES.map(async (capability) => ({ capability, runs: await getRuns(capability.suite, 2) })));
        return results.map((result, index) => result.status === 'fulfilled' ? result.value : { capability: CAPABILITIES[index], error: describeError(result.reason) });
      },
    };
    setState({ loading: true, sources: {} });
    for (const [key, request] of Object.entries(sources)) {
      Promise.resolve().then(request).then((data) => {
        if (active) setState((current) => ({ ...current, sources: { ...current.sources, [key]: { data, receivedAt: new Date().toISOString() } } }));
      }).catch((error) => {
        if (active) setState((current) => ({ ...current, sources: { ...current.sources, [key]: { error: describeError(error) } } }));
      }).finally(() => {
        if (active) setState((current) => ({ ...current, loading: Object.keys(current.sources).length < Object.keys(sources).length }));
      });
    }
    return () => { active = false; };
  }, [revision]);
  const { ops, queries, audit, benchmark } = state.sources;
  const summary = queries?.data;
  const services = ops?.data?.services ?? [];
  const down = services.filter((service) => service.status === 'down');
  const checkedAt = new Date(ops?.data?.checkedAt).getTime();
  const stale = !ops?.data?.checkedAt || !Number.isFinite(checkedAt) || Date.now() - checkedAt > 30000;
  const unknown = stale || !services.length || services.some((service) => !['up', 'down'].includes(service.status));
  const health = down.length ? `${down.length} servis müdahale bekliyor` : unknown ? 'Sistem durumu doğrulanamadı' : 'İzlenen servisler çalışıyor';
  const open = summary?.visibility?.reviewCandidateCount;
  const rate = summary?.successRate == null ? '—' : `%${(summary.successRate * 100).toFixed(1)}`;
  return <div className="gf-page pa-dashboard">
    <header className="gf-page-header"><div><p className="pa-eyebrow">YÖNETİM MERKEZİ</p><h1 className="gf-page-title">Genel Bakış</h1>
      <p className="gf-page-subtitle">Sistemi izle, sorunu incele, müdahalenin sonucunu doğrula.</p></div>
      <button className="gf-btn" disabled={state.loading} onClick={() => setRevision((value) => value + 1)}>{state.loading ? 'Yükleniyor…' : 'Yenile'}</button></header>
    <section className={`gf-alert gf-alert--${down.length ? 'danger' : unknown ? 'warning' : 'success'}`} role="status">
      <div><strong>{health}</strong><p className="gf-text-sm">Son servis yoklaması: {formatDateTime(ops?.data?.checkedAt)}. Sorgu kapsamı: son 7 gün.</p></div>
      <Link className="gf-btn gf-btn--sm" to="/services">Servisleri incele</Link>
    </section>
    {Object.entries(state.sources).filter(([, source]) => source.error).map(([key, source]) => <p className="gf-alert gf-alert--warning" role="alert" key={key}>
      {{ ops: 'Servis durumu', queries: 'Sorgu kalitesi', audit: 'Yönetim geçmişi', benchmark: 'Benchmark' }[key]} alınamadı: {source.error}</p>)}
    <div className="pa-kpis">{[
      ['Sorunlu servis', ops?.data && !unknown ? `${down.length} / ${services.length}` : '—', '/services'],
      ['Başarısız sorgu', summary?.failed ?? '—', '/diagnostics?status=failed'],
      ['Hata görülen firma', summary?.visibility?.byCompany?.filter((row) => row.companyId && row.metrics.failed > 0).length ?? '—', '/diagnostics?status=failed'],
      ['Açık inceleme adayı', open ?? '—', '/diagnostics'],
    ].map(([label, value, to]) => <Link className="gf-card pa-kpi" to={to} key={label}><span className="gf-muted">{label}</span><strong>{value}</strong><small>İncele →</small></Link>)}</div>
    <p className="gf-muted gf-text-sm">Son 7 gün işlem başarı oranı: {rate}. Bu oran sonuç doğruluğunu ölçmez. Firma sayısı, firma kimliği bilinen başarısız sorgulardan hesaplanır. Eksik veri — gösterilir.</p>
    {down.length > 0 && <section className="gf-card dg-detail"><h2>Müdahale Gerektiren Servisler</h2><div className="pa-issue-list">{down.map((service) => <article className="pa-issue" key={service.id}>
      <span className="gf-badge gf-badge--danger">Kapalı</span><div><strong>{service.name}</strong><p>{service.impact}</p><small className="gf-muted">{service.autoHeal?.gaveUp ? 'Otomatik onarma denemeleri tükendi.' : service.manuallyStopped ? 'Yönetici tarafından durduruldu.' : 'Servis sağlığı kontrol edilmeli.'}</small></div>
      <Link className="gf-btn gf-btn--sm" to="/services">İncele</Link></article>)}</div></section>}
    {summary && <PriorityIssues summary={summary} onOpen={(id) => navigate(`/diagnostics?id=${encodeURIComponent(id)}`)}
      onFilter={(patch) => navigate(`/diagnostics?${new URLSearchParams({ days: '7', ...patch })}`)} />}
    <div className="pa-dashboard-grid"><section className="gf-card dg-detail"><div className="dg-toolbar"><h2>AI & Benchmark</h2><Link to="/semantic">Tüm ölçümler →</Link></div>
      <p className="gf-muted gf-text-sm">Son iki koşunun ana metriği. Farklı veri seti ve ortam koşuları doğrudan karşılaştırılabilir olmayabilir.</p>
      {(benchmark?.data ?? []).map(({ capability, runs, error }) => {
        const current = metricOf(runs?.[0], capability.headline);
        const previous = metricOf(runs?.[1], capability.headline);
        const change = current && previous ? delta(current.value, previous.value, current.unit, current.direction) : null;
        return <div className="pa-benchmark-row" key={capability.key}><Link to={`/semantic?c=${capability.key}`}>{capability.title}</Link>
          <span className={`gf-badge${change?.tone === 'bad' ? ' gf-badge--danger' : ''}`}>{error ? 'Veri alınamadı' : !current ? 'Ölçülmemiş' : change ? `${change.tone === 'bad' ? 'Gerileme · ' : ''}${change.text}` : 'Karşılaştırma yok'}</span></div>;
      })}</section>
      <section className="gf-card dg-detail"><div className="dg-toolbar"><h2>Son Yönetim İşlemleri</h2><Link to="/services">Yönetim geçmişi →</Link></div>
        <p className="gf-muted gf-text-sm">Ops işlemleri ve otomatik onarma kayıtları. Firma ve sorgu yönetimi geçmişleri kendi ekranlarındadır.</p>
        {(audit?.data ?? []).slice(0, 5).map((entry, index) => <div className="pa-benchmark-row" key={`${entry.at}:${index}`}><div><strong>{entry.action} · {entry.target}</strong><p className="gf-text-sm gf-muted">{formatDateTime(entry.at)} · {entry.actor?.id || '—'}</p></div>
          <span className={`gf-badge${entry.outcome === 'failed' ? ' gf-badge--danger' : ''}`}>{entry.outcome === 'succeeded' ? 'Tamamlandı' : entry.outcome === 'failed' ? 'Başarısız' : entry.outcome}</span></div>)}
        {audit?.data?.length === 0 && <p className="gf-muted">Henüz işlem kaydı yok.</p>}
      </section></div>
    <p className="gf-muted gf-text-sm">Bu ekran açılışta ve Yenile ile güncellenir. Servis ekranı canlı yoklamaları sürdürür. Sorgu özeti alındı: {formatDateTime(queries?.receivedAt)}.</p>
  </div>;
}
