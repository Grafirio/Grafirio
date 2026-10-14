import React, { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { getMetricRules, saveMetricRules } from '../../services/dataAnalysisService';

export default function MetricRulesDialog({ connectionId, onClose }) {
  const [snapshot, setSnapshot] = useState(null);
  const [rules, setRules] = useState([]);
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let live = true;
    getMetricRules(connectionId).then(data => { if (live) { setSnapshot(data); setRules(data.rules); } })
      .catch(() => { if (live) setError('Metrik kuralları okunamadı. Bağlantıyı ve yetkinizi kontrol edin.'); });
    return () => { live = false; };
  }, [connectionId]);
  const patch = (i, update) => setRules(current => current.map((r, n) => n === i ? { ...r, ...update } : r));
  const available = (snapshot?.columns || []).filter(c => c.role !== 'identifier');
  const save = async () => {
    setBusy(true); setError('');
    try { await saveMetricRules(connectionId, { configId: snapshot.configId, dictionaryHash: snapshot.dictionaryHash, rules }); onClose(); }
    catch (e) { setError(e.response?.data?.error || 'Kurallar kaydedilemedi. Yetkinizi kontrol edin.'); }
    finally { setBusy(false); }
  };
  return createPortal(<div className="analysis-modal-backdrop"><section className="analysis-modal" role="dialog" aria-modal="true" aria-label="Metrik kuralları">
    <h2>Metrik kuralları</h2><p>Kurallar yeni analizlere uygulanır. Eski sonuçların açıklamaları değişmez. Sıfır doldurmayı yalnızca kayıtsız dönemin gerçekten sıfır olduğu durumda seçin.</p>
    {error && <p role="alert">{error}</p>}
    {snapshot?.warnings?.map((warning, i) => <p role="alert" key={i}>{warning}</p>)}
    {!snapshot && !error && <p>Yükleniyor…</p>}
    {rules.map((rule, i) => <fieldset key={i} disabled={busy}>
      <legend>{rule.label || `Metrik ${i + 1}`}</legend>
      <label>Alan<select value={`${rule.table}|${rule.column}`} onChange={e => {
        const c = available.find(c => `${c.table}|${c.column}` === e.target.value);
        if (c) patch(i, { table: c.table, column: c.column, dateColumn: null, forecastEnabled: false });
      }}>{available.map(c => <option key={`${c.table}|${c.column}`} value={`${c.table}|${c.column}`}>{c.table}.{c.column}</option>)}</select></label>
      <label>Metrik adı<input maxLength={120} value={rule.label} onChange={e => patch(i, { label: e.target.value })} /></label>
      <label>Birim<input maxLength={40} placeholder="TL, adet, yüzde…" value={rule.unit} onChange={e => patch(i, { unit: e.target.value })} /></label>
      <div>İzinli işlemler: {['sum', 'count', 'avg', 'min', 'max'].map((op, n) => <label key={op} className="inline-choice"><input type="checkbox" checked={rule.allowedAggregations.includes(op)} onChange={e => patch(i, { allowedAggregations: e.target.checked ? [...rule.allowedAggregations, op] : rule.allowedAggregations.filter(a => a !== op) })} />{['Toplam', 'Adet', 'Ortalama', 'En küçük', 'En büyük'][n]}</label>)}</div>
      <label>Tarih alanı<select value={rule.dateColumn || ''} onChange={e => patch(i, { dateColumn: e.target.value || null })}><option value="">Seçilmedi</option>{(snapshot.columns || []).filter(c => c.table === rule.table && (c.role === 'date' || /date|time/i.test(c.dataType || ''))).map(c => <option key={c.column}>{c.column}</option>)}</select></label>
      <label className="inline-choice"><input type="checkbox" checked={rule.forecastEnabled} onChange={e => patch(i, { forecastEnabled: e.target.checked })} />Tahmine izin ver</label>
      <label>Eksik dönem<select value={rule.missingPeriodPolicy} onChange={e => patch(i, { missingPeriodPolicy: e.target.value })}><option value="reject">Tahmini durdur</option><option value="zero">Gerçek sıfır kabul et</option></select></label>
      <label>En az geçmiş dönemi<input type="number" min="4" max="120" value={rule.minHistoryPeriods} onChange={e => patch(i, { minHistoryPeriods: Number(e.target.value) })} /></label>
      <label>En fazla tahmin ayı<input type="number" min="1" max="36" value={rule.maxHorizon} onChange={e => patch(i, { maxHorizon: Number(e.target.value) })} /></label>
      <button onClick={() => setRules(current => current.filter((_, n) => n !== i))}>Kuralı kaldır</button>
    </fieldset>)}
    <div className="analysis-modal-actions"><button disabled={!snapshot || busy || !available.length || rules.length >= 100} onClick={() => {
      const c = available.find(c => !rules.some(r => r.table === c.table && r.column === c.column));
      if (c) setRules([...rules, { table: c.table, column: c.column, label: c.column, unit: '', allowedAggregations: ['sum'], dateColumn: null, forecastEnabled: false, missingPeriodPolicy: 'reject', minHistoryPeriods: 12, maxHorizon: 6 }]);
    }}>Metrik ekle</button><button className="analysis-button-primary" disabled={!snapshot || busy} onClick={save}>{busy ? 'Kaydediliyor…' : 'Kaydet'}</button><button disabled={busy} onClick={onClose}>Kapat</button></div>
  </section></div>, document.body);
}
