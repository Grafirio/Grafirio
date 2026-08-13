import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { getSavedConnections, listAnalyses } from '../services/dataAnalysisService';
import '../styles/DashboardPage.css';

// Kapak isareti: marka isaretindeki gibi merkezden disa acilan dilimler, ama
// her analiz kendi siluetini alsin diye analizin kimligine bagli sabit bir
// hash'ten turetiliyor (rastgele degil — ayni analiz her acilista ayni
// gorunur). Sabit grafik paletini (--gf-c01.. ) sirayla tuketir; veriyle
// bir ilgisi yok, yalnizca izgarada kartlari birbirinden ayirt ettiren bir
// susleme.
const PALETTE = ['--gf-c01', '--gf-c02', '--gf-c03', '--gf-c04', '--gf-c05', '--gf-c06', '--gf-c07', '--gf-c08', '--gf-c09'];

function hashSeed(str) {
  let h = 0;
  for (let i = 0; i < str.length; i += 1) h = (Math.imul(h, 31) + str.charCodeAt(i)) >>> 0;
  return h;
}

function spokesFor(seedStr, tableCount) {
  const h = hashSeed(seedStr || 'x');
  const n = Math.min(9, Math.max(5, tableCount || 6));
  const step = 360 / n;
  return Array.from({ length: n }, (_, i) => {
    const len = 8 + ((h >> (i * 3)) % 12);
    const inner = 8 + (len % 3) * 3;
    const reach = 22 + (len - 11) * 1.1;
    return {
      a: `${(i * step - 90).toFixed(1)}deg`,
      x: `${(50 + inner).toFixed(1)}%`,
      l: `${Math.max(8, reach - inner).toFixed(1)}%`,
      t: `${(4.2 + (len % 4) * 0.9).toFixed(1)}%`,
      c: `var(${PALETTE[i % PALETTE.length]})`,
    };
  });
}

/**
 * Panel ana ekrani. Tasarim taslagindaki iki bolume ayrilmis: "Analizlerim"
 * (tamamlanmis analizler, kapak-isaretli izgara) ve "Veri kaynaklari"
 * (kayitli baglantilar, satir listesi). Onceki surumde bu ikisi ve ayrica
 * bir de baglanti tablosu ust uste uc bolume yayilmisti; ayni veriyi iki
 * kez gostermek yerine taslaktaki gibi ikiye indirildi.
 *
 * "Islenen satir" ve "Otomatik yorum" kartlari taslakta da "taslak"
 * etiketiyle duruyor — biz de ayni durustlugu koruyoruz: sayi uydurmuyoruz,
 * kart yerinde duruyor ve servis gelince dolacagi acikca yaziyor.
 *
 * Analizler sunucudan okunuyor. Onceden localStorage'daydilar: baska bir
 * makineden girildiginde ya da gecmis temizlendiginde panel "hic analiz yok"
 * diyordu, ustelik basarisiz bir analiz orada "hazir" olarak kalabiliyordu.
 */
const DashboardPage = () => {
  const navigate = useNavigate();
  const { keycloak } = useKeycloak();

  const [connections, setConnections] = useState([]);
  const [loadingConns, setLoadingConns] = useState(true);
  const [connError, setConnError] = useState('');

  const [activeAnalyses, setActiveAnalyses] = useState([]);
  const [completedAnalyses, setCompletedAnalyses] = useState([]);

  const loadConnections = useCallback(async () => {
    setLoadingConns(true);
    setConnError('');
    try {
      const result = await getSavedConnections();
      setConnections(result?.connections ?? result?.data ?? []);
    } catch (err) {
      setConnError(
        err?.response?.status === 401
          ? 'Oturumunuz doğrulanamadı. Sayfayı yenileyip tekrar deneyin.'
          : 'Bağlantılar okunamadı.'
      );
    } finally {
      setLoadingConns(false);
    }
  }, []);

  const loadAnalyses = useCallback(async () => {
    try {
      const result = await listAnalyses();
      const all = result?.analyses ?? [];
      // "Isleniyor" = analiz surerken ya da sorular bekliyorken; kanvasa
      // ancak `ready` olanlarla girilebilir.
      setActiveAnalyses(all.filter((a) => a.status === 'analyzing' || a.status === 'awaiting_answers'));
      setCompletedAnalyses(all.filter((a) => a.status === 'ready' || a.status === 'failed'));
    } catch {
      // Panelin geri kalani calismaya devam etsin; baglanti listesi kendi
      // hatasini zaten gosteriyor.
    }
  }, []);

  useEffect(() => {
    loadConnections();
    loadAnalyses();
    const interval = setInterval(loadAnalyses, 8000);
    return () => clearInterval(interval);
  }, [loadConnections, loadAnalyses]);

  const firstName = (keycloak.tokenParsed?.name || keycloak.tokenParsed?.preferred_username || '').split(/\s+/)[0];

  const fmtDate = (str) =>
    str
      ? new Date(str).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
      : '—';

  return (
    <div className="db">
      <div className="db-head">
        <div>
          <p className="db-eyebrow">Panel · Çalışma Alanınız</p>
          <h1>{firstName ? `Merhaba ${firstName}.` : 'Analiz kanvasları'}</h1>
          <p className="db-lead">
            {loadingConns
              ? 'Veri kaynaklarınız yükleniyor…'
              : `${connections.length} veri kaynağın bağlı, ${completedAnalyses.length} analiz edilmiş.`}{' '}
            Bir analizi açmak için üzerine <strong>çift tıklayın</strong>.
          </p>
        </div>
        <div className="db-head-actions">
          <button className="db-btn db-btn--ghost" onClick={() => navigate('/data?tab=upload')}>
            Veri yükle
          </button>
          <button className="db-btn" onClick={() => navigate('/data?tab=connections')}>
            Yeni analiz
          </button>
        </div>
      </div>

      <div className="db-kpis">
        <div className="db-kpi" style={{ '--accent': 'var(--gf-navy)' }}>
          <span className="db-kpi-label">Aktif analiz</span>
          <strong>{completedAnalyses.length}</strong>
          <span className="db-kpi-note">
            {activeAnalyses.length > 0
              ? `${activeAnalyses.length} tanesi işleniyor`
              : 'tamamlanmış analiz'}
          </span>
        </div>
        <div className="db-kpi" style={{ '--accent': 'var(--gf-teal)' }}>
          <span className="db-kpi-label">Bağlı veritabanı</span>
          <strong>{loadingConns ? '—' : connections.length}</strong>
          <span className="db-kpi-note">
            {loadingConns ? '—' : `${connections.length} doğrudan bağlantı`}
          </span>
        </div>
        <div className="db-kpi db-kpi--draft">
          <span className="db-kpi-label">
            İşlenen satır <span className="db-kpi-tag">taslak</span>
          </span>
          <strong>—</strong>
          <span className="db-kpi-note">servis bekleniyor</span>
        </div>
        <div className="db-kpi db-kpi--draft">
          <span className="db-kpi-label">
            Otomatik yorum <span className="db-kpi-tag">taslak</span>
          </span>
          <strong>—</strong>
          <span className="db-kpi-note">servis bekleniyor</span>
        </div>
      </div>

      {activeAnalyses.length > 0 && (
        <div className="db-processing">
          {activeAnalyses.map((a) => (
            <span key={a.connectionId} className="db-processing-item">
              <span className="db-spinner" />
              {a.database || 'Analiz'}{' '}
              {a.status === 'awaiting_answers' ? 'sorularınızı bekliyor' : 'işleniyor'}
            </span>
          ))}
        </div>
      )}

      <section>
        <div className="db-section-head">
          <h2>Analizlerim</h2>
          <span className="db-hint">çift tık → aç</span>
          {completedAnalyses.length > 0 && (
            <button className="db-link" style={{ marginLeft: 'auto' }} onClick={() => navigate('/data?tab=connections')}>
              Tümü →
            </button>
          )}
        </div>

        <div className="db-canvas-grid">
          {completedAnalyses.map((a) => (
            <div
              key={a.connectionId}
              className="db-canvas-tile"
              onDoubleClick={() => navigate(`/canvas?connectionId=${a.connectionId}`)}
              title="Kanvası açmak için çift tıklayın"
            >
              <div className="db-canvas-cover">
                {spokesFor(a.connectionId || a.database, a.tableCount).map((s, i) => (
                  <span key={i} className="db-spoke" style={{ transform: `rotate(${s.a})` }}>
                    <span style={{ left: s.x, width: s.l, height: s.t, background: s.c }} />
                  </span>
                ))}
                <span className="db-canvas-center" />
              </div>
              <div className="db-canvas-info">
                <div className="db-canvas-info-top">
                  <strong>{a.database}</strong>
                  <span className={`db-badge ${a.status === 'failed' ? 'db-badge--err' : 'db-badge--ok'}`}>
                    <i />
                    {a.status === 'failed' ? 'Başarısız' : 'Hazır'}
                  </span>
                </div>
                <div className="db-canvas-info-meta">
                  <span>{a.tableCount ?? 0} tablo</span>
                  <span>·</span>
                  <span>{fmtDate(a.updatedAt || a.createdAt)}</span>
                </div>
              </div>
            </div>
          ))}

          <button className="db-canvas-tile db-canvas-tile--add" onClick={() => navigate('/data?tab=connections')}>
            <span className="db-plus">+</span>
            <span>Veri kaynağı bağla</span>
          </button>
        </div>

        {completedAnalyses.length === 0 && !loadingConns && (
          <p className="db-empty">
            Henüz analiz yok. Veri kaynakları sayfasından bir bağlantı seçip “Analiz Et”e tıklayın.
          </p>
        )}
      </section>

      <section className="db-card">
        <div className="db-card-head">
          <h2>Veri kaynakları</h2>
          <button className="db-link" onClick={() => navigate('/data?tab=connections')}>
            Yönet →
          </button>
        </div>

        {connError && <div className="db-alert">{connError}</div>}

        {loadingConns && <p className="db-empty">Yükleniyor…</p>}

        {!loadingConns && connections.length === 0 && !connError && (
          <p className="db-empty">
            Henüz kayıtlı bağlantı yok. “Veri kaynağı bağla” ile ilk veri kaynağınızı tanımlayın.
          </p>
        )}

        {!loadingConns && connections.length > 0 && (
          <div className="db-source-list">
            {connections.map((c) => (
              <div key={c.id} className="db-source-row">
                <span className="db-source-icon" />
                <span className="db-source-text">
                  <strong>{c.name}</strong>
                  <span className="db-mono">
                    {c.host}
                    {c.port ? `:${c.port}` : ''} · {c.database}
                  </span>
                </span>
                <span className="db-mono db-source-date">{fmtDate(c.lastConnectedAt)}</span>
                <span className="db-badge db-badge--ok">
                  <i />
                  {c.lastConnectedAt ? 'Bağlandı' : 'Kayıtlı'}
                </span>
              </div>
            ))}
          </div>
        )}
      </section>
    </div>
  );
};

export default DashboardPage;
