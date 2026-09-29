import { Fragment, useCallback, useEffect, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import Sparkline from '../components/semantic/Sparkline';
import TrendChart from '../components/semantic/TrendChart';
import { describeError } from '../services/authClient';
import {
  BENCHMARK_URL, CAPABILITIES, delta, formatValue, getRun, getRuns, getSignals, metricLabel, metricOf,
} from '../services/semanticApi';

const PERIODS = [7, 30, 90];

/** Kartın ana metriği: headline ölçülmemişse fallback. */
function headlineOf(capability, run) {
  return metricOf(run, capability.headline) ?? (capability.fallback ? metricOf(run, capability.fallback) : null);
}

function Delta({ current, previous }) {
  if (!current || !previous) return null;
  const d = delta(current.value, previous.value, current.unit, current.direction);
  if (!d) return null;
  return (
    <span className={`sm-delta sm-delta--${d.tone}`} title="Bir önceki koşuya göre">
      {d.arrow} {d.text}
    </span>
  );
}

/**
 * Uyarılar: panelin açılır açılmaz söylemesi gerekenler. Her biri bir ölçümden
 * türetiliyor; eşikler bilinçli olarak katı (kanıt olarak kullanılan bir
 * rakamda "genelde" yeterli değil).
 */
function buildAlerts(runs, signals) {
  const alerts = [];
  const latest = (key) => runs[key]?.[0];

  const sqlErrors = latest('relationships')?.environment?.sqlErrors;
  if (sqlErrors && sqlErrors !== 'yok') {
    alerts.push({
      tone: 'danger',
      title: 'İlişki keşfinde yutulan SQL hataları',
      text: `Keşif kodu bu hataları "aday elendi" diye sessizce geçiyor: ${sqlErrors}`,
    });
  }

  const inferred = signals?.report?.capabilities
    ?.find((c) => c.capability === 'relationships')?.signals?.find((s) => s.name === 'inferred.per_analysis');
  if (inferred && inferred.sampleSize > 0 && inferred.value === 0) {
    alerts.push({
      tone: 'warning',
      title: 'Üretimde hiç çıkarsanan ilişki yok',
      text: `Son ${signals.days} günde ${inferred.sampleSize} analizin hiçbirinde adlardan ya da veriden ilişki çıkarılmadı. `
        + 'Yalnızca bildirilmiş FK\'ler kullanılıyor; çıkarım çalışmıyor olabilir.',
    });
  }

  const sensitive = metricOf(latest('schema'), 'sensitive.recall');
  if (sensitive && sensitive.value < 1) {
    alerts.push({
      tone: 'danger',
      title: 'Korunmayan hassas kolon',
      text: `Hassas kolonların ${formatValue(1 - sensitive.value, 'ratio')}'i korunmuyor; değerleri modele gidebilir. `
        + 'Ayrıntı: Şema anlama → başarısız vakalar.',
    });
  }

  const hallucination = metricOf(latest('unknown'), 'hallucination.rate');
  if (hallucination && hallucination.value > 0) {
    alerts.push({
      tone: 'warning',
      title: 'Uydurma cevap',
      text: `Cevabı şemada olmayan soruların ${formatValue(hallucination.value, 'ratio')}'inde sistem soru sormadan cevap üretti.`,
    });
  }

  const notes = CAPABILITIES.map((c) => latest(c.key)?.environment?.notes).filter(Boolean);
  for (const note of new Set(notes)) alerts.push({ tone: 'info', title: 'Ölçüm notu', text: note });

  return alerts;
}

function CapabilityCard({ capability, runs, signals, selected, onSelect }) {
  const latest = runs?.[0];
  const previous = runs?.[1];
  const main = headlineOf(capability, latest);
  const previousMain = main && previous ? metricOf(previous, main.name) : null;
  const history = (runs ?? []).slice(0, 12).reverse().map((r) => metricOf(r, main?.name)?.value);
  const failed = latest ? latest.caseCount - latest.passedCount : 0;
  const live = signals?.report?.capabilities?.find((c) => c.capability === capability.key)?.signals ?? [];

  return (
    <button type="button" className={`gf-card sm-card${selected ? ' is-selected' : ''}`} onClick={onSelect}
      aria-pressed={selected}>
      <div className="sm-card__head">
        <div>
          <div className="sm-card__title">{capability.title}</div>
          <div className="gf-subtle gf-text-sm">{capability.english}</div>
        </div>
        {latest && failed > 0 && <span className="gf-badge gf-badge--danger">{failed} başarısız vaka</span>}
        {latest && failed === 0 && <span className="gf-badge gf-badge--success">tüm vakalar başarılı</span>}
      </div>

      {main ? (
        <>
          <div className="sm-card__value-row">
            <div>
              <div className="gf-subtle gf-text-sm">{metricLabel(main.name)}</div>
              <div className="sm-card__value">{formatValue(main.value, main.unit)}</div>
              <Delta current={main} previous={previousMain} />
            </div>
            <Sparkline values={history} domain={main.unit === 'ratio' ? [0, 1] : undefined}
              label={`${metricLabel(main.name)} son ${history.length} koşu`} />
          </div>
          <dl className="sm-card__metrics">
            {capability.secondary.filter((name) => name !== main.name).map((name) => {
              const metric = metricOf(latest, name);
              return metric && (
                <div key={name}>
                  <dt>{metricLabel(name)}</dt>
                  <dd>{formatValue(metric.value, metric.unit)}</dd>
                </div>
              );
            })}
          </dl>
        </>
      ) : (
        <p className="gf-muted gf-text-sm sm-card__empty">
          Henüz ölçülmedi. <code>{capability.suite}</code> setinin bir koşusu gerekiyor.
        </p>
      )}

      {live.length > 0 && (
        <div className="sm-card__live">
          <div className="gf-subtle gf-text-sm">Üretim · canlı</div>
          {live.slice(0, 2).map((s) => (
            <div key={s.name} className="sm-card__live-row">
              <span>{metricLabel(s.name)}</span>
              <span>{formatValue(s.value, s.unit)}</span>
            </div>
          ))}
        </div>
      )}
    </button>
  );
}

function RunMetricsTable({ run, previous }) {
  const metrics = [...(run?.metrics ?? [])].sort((a, b) => a.name.localeCompare(b.name));
  return (
    <table className="pa-table">
      <thead>
        <tr><th>Metrik</th><th>Bu koşu</th><th>Önceki</th><th>Fark</th></tr>
      </thead>
      <tbody>
        {metrics.map((m) => {
          const before = metricOf(previous, m.name);
          return (
            <tr key={m.name}>
              <td title={m.name}>{metricLabel(m.name)}</td>
              <td className="sm-num">{formatValue(m.value, m.unit)}</td>
              <td className="sm-num gf-muted">{before ? formatValue(before.value, before.unit) : '—'}</td>
              <td className="sm-num"><Delta current={m} previous={before} /></td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function CasesTable({ cases }) {
  const [onlyFailed, setOnlyFailed] = useState(true);
  const [open, setOpen] = useState(null);
  const shown = (cases ?? []).filter((c) => !onlyFailed || !c.success);

  return (
    <>
      <label className="gf-checkbox sm-filter">
        <input type="checkbox" checked={onlyFailed} onChange={(e) => setOnlyFailed(e.target.checked)} />
        Yalnızca başarısız vakalar ({(cases ?? []).filter((c) => !c.success).length}/{(cases ?? []).length})
      </label>
      {shown.length === 0 ? (
        <p className="gf-muted gf-text-sm">Gösterilecek vaka yok.</p>
      ) : (
        <table className="pa-table">
          <thead><tr><th>Vaka</th><th>Grup</th><th>Sonuç</th><th>Açıklama</th></tr></thead>
          <tbody>
            {shown.map((c) => (
              <Fragment key={c.id}>
                <tr className="sm-case" onClick={() => setOpen(open === c.id ? null : c.id)}>
                  <td>{c.name}</td>
                  <td className="gf-muted">{c.group}</td>
                  <td>
                    <span className={`gf-badge ${c.success ? 'gf-badge--success' : 'gf-badge--danger'}`}>
                      {c.success ? 'başarılı' : 'başarısız'}
                    </span>
                  </td>
                  <td className="sm-case__message">{c.message ?? ''}</td>
                </tr>
                {open === c.id && c.details && (
                  <tr className="sm-case__details">
                    <td colSpan={4}><pre>{JSON.stringify(c.details, null, 2)}</pre></td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      )}
    </>
  );
}

function CapabilityDetail({ capability, runs, signals }) {
  const [metricName, setMetricName] = useState(null);
  const [runId, setRunId] = useState(null);
  const [detail, setDetail] = useState(null);
  const [error, setError] = useState('');

  const latest = runs?.[0];
  const selectedRun = runs?.find((r) => r.id === runId) ?? latest;
  const selectedIndex = runs?.indexOf(selectedRun) ?? -1;
  const previousRun = selectedIndex >= 0 ? runs?.[selectedIndex + 1] : null;
  const metric = metricName ?? headlineOf(capability, latest)?.name;
  const unit = metricOf(latest, metric)?.unit;

  const names = useMemo(
    () => [...new Set((runs ?? []).flatMap((r) => r.metrics.map((m) => m.name)))].sort(),
    [runs],
  );

  useEffect(() => {
    if (!selectedRun) return undefined;
    let cancelled = false;
    getRun(selectedRun.id)
      .then((d) => { if (!cancelled) { setDetail(d); setError(''); } })
      .catch((e) => { if (!cancelled) setError(e.message); });
    return () => { cancelled = true; };
  }, [selectedRun]);

  const points = [...(runs ?? [])].reverse().map((r) => ({
    id: r.id, startedAt: r.startedAt, label: r.label, gitCommit: r.gitCommit, gitBranch: r.gitBranch,
    value: metricOf(r, metric)?.value,
  }));
  const live = signals?.report?.capabilities?.find((c) => c.capability === capability.key)?.signals ?? [];

  return (
    <section className="gf-card sm-detail" aria-label={`${capability.title} ayrıntısı`}>
      <div className="gf-card__header">
        <div>
          <h2 className="sm-detail__title">{capability.title}</h2>
          <p className="gf-muted gf-text-sm">{capability.description}</p>
        </div>
        {selectedRun && (
          <a className="gf-btn gf-btn--sm gf-btn--ghost" href={`${BENCHMARK_URL}/scenarios/${selectedRun.id}`}
            target="_blank" rel="noreferrer">
            Dashboard'da aç
          </a>
        )}
      </div>

      <div className="gf-card__body">
        {!latest ? (
          <div className="gf-empty">
            <h3>Henüz koşu yok</h3>
            <p>Ölçüm komutları için <code>docs/olcum/semantik.md</code> dosyasına bakın.</p>
          </div>
        ) : (
          <>
            <div className="sm-detail__toolbar">
              <label className="gf-label" htmlFor="sm-metric">Metrik</label>
              <select id="sm-metric" className="gf-select" value={metric ?? ''} onChange={(e) => setMetricName(e.target.value)}>
                {names.map((n) => <option key={n} value={n}>{metricLabel(n)}</option>)}
              </select>
              <span className="gf-subtle gf-text-sm">
                {metricOf(latest, metric)?.direction === 'higher' ? 'Yüksek olması iyi'
                  : metricOf(latest, metric)?.direction === 'lower' ? 'Düşük olması iyi' : ''}
              </span>
            </div>

            <TrendChart points={points} unit={unit} selectedId={selectedRun?.id} onSelect={setRunId}
              label={`${metricLabel(metric ?? '')} — koşular boyunca`} />

            <div className="sm-detail__grid">
              <div>
                <h3 className="sm-h3">
                  Seçili koşu: {selectedRun.label ?? new Date(selectedRun.startedAt).toLocaleString('tr-TR')}
                </h3>
                <p className="gf-subtle gf-text-sm">
                  {new Date(selectedRun.startedAt).toLocaleString('tr-TR')}
                  {selectedRun.gitCommit ? ` · ${selectedRun.gitBranch ?? ''}@${selectedRun.gitCommit}` : ''}
                  {` · ${selectedRun.passedCount}/${selectedRun.caseCount} vaka başarılı`}
                </p>
                <RunMetricsTable run={selectedRun} previous={previousRun} />
              </div>
              <div>
                <h3 className="sm-h3">Üretimden canlı sinyaller</h3>
                {live.length === 0 ? (
                  <p className="gf-muted gf-text-sm">Canlı sinyal yok (yetki, bağlantı ya da dönemde veri yok).</p>
                ) : (
                  <table className="pa-table">
                    <thead><tr><th>Sinyal</th><th>Değer</th><th>Örneklem</th></tr></thead>
                    <tbody>
                      {live.map((s) => (
                        <tr key={s.name} title={s.description}>
                          <td>{metricLabel(s.name)}</td>
                          <td className="sm-num">{formatValue(s.value, s.unit)}</td>
                          <td className="sm-num gf-muted">{s.sampleSize}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
                <p className="gf-subtle gf-text-sm sm-hint">
                  Canlı sinyaller altın veri olmadan, gerçek kullanımdan: dolaylıdır ama müşterinin kendi şemasında
                  ne olduğunu gösterir. Açıklama için satırın üzerine gelin.
                </p>
              </div>
            </div>

            <h3 className="sm-h3">Vakalar</h3>
            {error && <div className="gf-alert gf-alert--danger">{error}</div>}
            <CasesTable cases={detail?.run?.id === selectedRun.id ? detail.cases : []} />

            <h3 className="sm-h3">Koşular (tablo görünümü)</h3>
            <table className="pa-table">
              <thead><tr><th>Tarih</th><th>Etiket</th><th>Commit</th><th>{metricLabel(metric ?? '')}</th><th>Vaka</th></tr></thead>
              <tbody>
                {runs.map((r) => (
                  <tr key={r.id} className={r.id === selectedRun.id ? 'is-selected' : ''} onClick={() => setRunId(r.id)}>
                    <td>{new Date(r.startedAt).toLocaleString('tr-TR')}</td>
                    <td>{r.label ?? ''}</td>
                    <td className="pa-mono">{r.gitCommit ?? ''}</td>
                    <td className="sm-num">{formatValue(metricOf(r, metric)?.value, unit)}</td>
                    <td className="sm-num">{r.passedCount}/{r.caseCount}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>
    </section>
  );
}

export default function SemanticPage() {
  const [params, setParams] = useSearchParams();
  const [runs, setRuns] = useState({});
  const [signals, setSignals] = useState(null);
  const [days, setDays] = useState(30);
  const [loading, setLoading] = useState(true);
  const [errors, setErrors] = useState([]);

  const selectedKey = params.get('c') ?? CAPABILITIES[0].key;
  const selected = CAPABILITIES.find((c) => c.key === selectedKey) ?? CAPABILITIES[0];

  const load = useCallback(async () => {
    setLoading(true);
    const problems = [];
    const results = await Promise.allSettled(CAPABILITIES.map((c) => getRuns(c.suite)));
    const next = {};
    results.forEach((result, i) => {
      if (result.status === 'fulfilled') next[CAPABILITIES[i].key] = result.value;
      else problems.push(result.reason?.message);
    });
    setRuns(next);

    try {
      setSignals(await getSignals(days));
    } catch (error) {
      setSignals(null);
      problems.push(`Canlı sinyaller: ${describeError(error)}`);
    }

    setErrors([...new Set(problems.filter(Boolean))]);
    setLoading(false);
  }, [days]);

  useEffect(() => { load(); }, [load]);

  const alerts = useMemo(() => buildAlerts(runs, signals), [runs, signals]);

  return (
    <div className="gf-page">
      <div className="gf-page-header sm-page-header">
        <div>
          <h1 className="gf-page-title">Semantik zekâ</h1>
          <p className="gf-page-subtitle">
            Altı yetenek, doğru cevabı bilinen test veritabanlarına karşı katman katman ölçülüyor; altında
            üretimden gelen canlı sinyaller.
          </p>
        </div>
        <div className="sm-toolbar">
          <label className="gf-label" htmlFor="sm-days">Canlı dönem</label>
          <select id="sm-days" className="gf-select" value={days} onChange={(e) => setDays(Number(e.target.value))}>
            {PERIODS.map((p) => <option key={p} value={p}>Son {p} gün</option>)}
          </select>
          <button className="gf-btn gf-btn--sm" onClick={load} disabled={loading}>
            {loading ? <span className="gf-spinner" /> : 'Yenile'}
          </button>
        </div>
      </div>

      {errors.map((e) => <div key={e} className="gf-alert gf-alert--warning">{e}</div>)}
      {alerts.map((a) => (
        <div key={a.title + a.text} className={`gf-alert ${a.tone === 'info' ? '' : `gf-alert--${a.tone}`}`}>
          <div><strong>{a.title}.</strong> {a.text}</div>
        </div>
      ))}

      <div className="sm-grid">
        {CAPABILITIES.map((capability) => (
          <CapabilityCard key={capability.key} capability={capability} runs={runs[capability.key]} signals={signals}
            selected={capability.key === selected.key}
            onSelect={() => setParams({ c: capability.key })} />
        ))}
      </div>

      <CapabilityDetail key={selected.key} capability={selected} runs={runs[selected.key]} signals={signals} />
    </div>
  );
}
