import React, { useCallback, useEffect, useState } from 'react';
import { listLearnedFacts, forgetLearnedFact } from '../../services/dataAnalysisService';
import DeclareLinkForm from './DeclareLinkForm';

/**
 * "Öğrendiklerim" — bu bağlantı için kullanıcının sisteme öğrettiği her şey.
 *
 * Bu ekranın varlığı pazarlık konusu değil. Öğrenilen bilgi kalıcı ve
 * sorgunun içinde görünmez: yanlış öğrenilmiş bir bağ, bundan sonraki her
 * cevabı sessizce bozar. Listelenemiyor ve silinemiyorsa kullanıcı yanlış
 * cevap alır ve sebebini bulabileceği hiçbir yer olmaz — o yüzden **yanlış
 * öğrenilmiş bir bilgi, hiç öğrenmemekten kötüdür.**
 *
 * Reddedilenler de listede. Onlar da bir karar: kullanıcı "bu eşleşme
 * yanlış" dediği için sistem o bağlantıyı bir daha kurmuyor. Fikri
 * değiştiyse görüp silebilmeli, yoksa o kapı sonsuza kadar kapalı kalır ve
 * neden kapalı olduğu hiçbir yerde yazmaz.
 */

const KIND_LABEL = {
  relationship: 'Bağlantı',
  synonym: 'Eş anlamlı',
  meaning: 'Tanım',
  codeMeaning: 'Kod anlamı',
  label: 'Etiket kolonu',
};

const formatDate = (value) => {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleDateString('tr-TR');
};

export default function LearnedFactsPanel({ connectionId, tables = [] }) {
  const [items, setItems] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [removing, setRemoving] = useState(null);
  const [declaring, setDeclaring] = useState(false);

  const load = useCallback(async () => {
    if (!connectionId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await listLearnedFacts(connectionId);
      setItems(data.items || []);
    } catch (e) {
      setError(e.response?.data?.error || e.message);
    } finally {
      setLoading(false);
    }
  }, [connectionId]);

  useEffect(() => { load(); }, [load]);

  const forget = async (key) => {
    setRemoving(key);
    try {
      await forgetLearnedFact(connectionId, key);
      setItems(prev => prev.filter(i => i.key !== key));
    } catch (e) {
      setError(e.response?.data?.error || e.message);
    } finally {
      setRemoving(null);
    }
  };

  return (
    <div className="analysis-section">
      <h3>
        <i className="ti ti-bulb"></i>
        Öğrendiklerim ({items.length})
      </h3>

      <p className="learned-facts-hint">
        Sorularınıza verdiğiniz cevaplar ve onayladığınız eşleştirmeler.
        Bunlar her “Analiz Et”te sözlüğe işleniyor. Yanlış bir şey
        öğrettiyseniz silin — silinen bilgi bir sonraki analizden itibaren
        kullanılmaz.
      </p>

      {tables.length > 0 && (
        <div className="declare-link-wrap">
          <button
            type="button"
            className="declare-link-toggle"
            onClick={() => setDeclaring(v => !v)}
          >
            {declaring ? '− Kapat' : '+ Kolonları elle eşleştir'}
          </button>
          {declaring && (
            <DeclareLinkForm
              connectionId={connectionId}
              tables={tables}
              onSaved={load}
            />
          )}
        </div>
      )}

      {loading && <div className="learned-facts-empty">Yükleniyor…</div>}

      {error && !loading && (
        <div className="learned-facts-error">
          Liste okunamadı: {error}
          <button type="button" className="learned-facts-retry" onClick={load}>
            Tekrar dene
          </button>
        </div>
      )}

      {!loading && !error && items.length === 0 && (
        <div className="learned-facts-empty">
          Bu bağlantı için henüz bir şey öğrenilmedi.
        </div>
      )}

      {!loading && items.length > 0 && (
        <ul className="learned-facts-list">
          {items.map(item => (
            <li
              key={item.key}
              className={`learned-fact${item.accepted ? '' : ' learned-fact--rejected'}`}
            >
              <div className="learned-fact-main">
                <span className="learned-fact-kind">
                  {KIND_LABEL[item.kind] || item.kind}
                </span>
                <span className="learned-fact-text">{item.description}</span>
              </div>
              <div className="learned-fact-meta">
                {/* Geçersizleşen beyan sessizce düşürülmüyor. Şema değişir,
                    kolon kaldırılır, anahtar çoğullaşır — kullanıcı bunu
                    görmezse kurduğu bağlantının hâlâ çalıştığını sanar. */}
                {item.problem && (
                  <span className="learned-fact-problem" title={item.problem}>
                    ⚠ {item.problem}
                  </span>
                )}
                {!item.accepted && (
                  <span className="learned-fact-flag">reddedildi — bu bağ kurulmuyor</span>
                )}
                <span className="learned-fact-date">{formatDate(item.createdAt)}</span>
                <button
                  type="button"
                  className="learned-fact-forget"
                  disabled={removing === item.key}
                  onClick={() => forget(item.key)}
                  title="Bu bilgiyi unut"
                >
                  <i className="ti ti-trash"></i>
                </button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
