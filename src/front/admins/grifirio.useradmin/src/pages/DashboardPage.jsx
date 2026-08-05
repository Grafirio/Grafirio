import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useKeycloak } from '@react-keycloak/web';
import { getSavedConnections } from '../services/dataAnalysisService';
import '../styles/DashboardPage.css';

/**
 * Panel ana ekrani. Tasarim taslagindaki yerlesim: baslik, sayaclar, veri
 * kaynagi izgarasi, son analizler ve baglanti tablosu.
 *
 * Taslakta bunlarin yaninda "islenen satir", "otomatik yorum", "Grafirio
 * yorumu", "bekleyen isler" ve "depolama" kartlari da var. Onlari almadim:
 * hicbirini besleyen bir uc yok ve sabit "2,41M / 5M" yazmak, veriymis gibi
 * gorunen bir dekordan ibaret olurdu. Servisleri yazildiginda yerleri hazir.
 *
 * Analizler hala localStorage'da; sunucu tarafinda kanvas kaydi yok. Onu
 * degistirmedim, yalnizca yerlesimi tasidim.
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

  const loadAnalyses = useCallback(() => {
    const stored = localStorage.getItem('activeAnalyses');
    if (!stored) return;
    try {
      const all = JSON.parse(stored);
      setActiveAnalyses(all.filter((a) => a.status !== 'completed' && a.status !== 'failed'));
      setCompletedAnalyses(all.filter((a) => a.status === 'completed' || a.status === 'failed'));
    } catch {
      // Bozuk bir kayit tum paneli goturmesin.
    }
  }, []);

  useEffect(() => {
    loadConnections();
    loadAnalyses();
    const interval = setInterval(loadAnalyses, 8000);
    return () => clearInterval(interval);
  }, [loadConnections, loadAnalyses]);

  const companyName = keycloak.tokenParsed?.company_name || 'Çalışma alanınız';

  const fmtDate = (str) =>
    str
      ? new Date(str).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' })
      : '—';

  return (
    <div className="db">
      <div className="db-head">
        <div>
          <p className="db-eyebrow">Panel · {companyName}</p>
          <h1>Analiz kanvasları</h1>
          <p className="db-lead">
            Kayıtlı bir analize <strong>çift tıklayarak</strong> kanvası açın; yeni bir veri
            kaynağı bağlamak için sağdaki butonu kullanın.
          </p>
        </div>
        <div className="db-head-actions">
          <button className="db-btn db-btn--ghost" onClick={() => navigate('/settings/data-input')}>
            Veri yükle
          </button>
          <button className="db-btn" onClick={() => navigate('/settings/sql-connection')}>
            + Yeni Analiz
          </button>
        </div>
      </div>

      <div className="db-kpis">
        <div className="db-kpi" style={{ '--accent': 'var(--gf-navy)' }}>
          <span className="db-kpi-label">Aktif kanvas</span>
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
          <span className="db-kpi-note">kayıtlı bağlantı</span>
        </div>
      </div>

      {activeAnalyses.length > 0 && (
        <div className="db-processing">
          {activeAnalyses.map((a) => (
            <span key={a.requestId} className="db-processing-item">
              <span className="db-spinner" />
              {a.database || 'Analiz'} işleniyor · %{a.progress ?? 0}
            </span>
          ))}
        </div>
      )}

      <section className="db-card">
        <div className="db-card-head">
          <div>
            <h2>Veri kaynağı ızgarası</h2>
            <p className="db-card-sub">
              Bir bağlantıya <strong>çift tıklayın</strong> — solda yapay zekâ sohbeti,
              sağda grafik kanvası açılır.
            </p>
          </div>
          <span className="db-hint">firma geneli</span>
        </div>

        {connError && <div className="db-alert">{connError}</div>}

        <div className="db-grid-wrap">
          <div className="db-grid">
            {loadingConns && <div className="db-tile db-tile--muted">Yükleniyor…</div>}

            {!loadingConns &&
              connections.map((c) => (
                // Tasarim taslagindaki davranis: baglanti kartina cift tik,
                // solda sohbet sagda kanvas. Kanvas ana konusma ekrani oldugu
                // icin buradan dogrudan aciliyor.
                <div
                  className="db-tile"
                  key={c.id}
                  onDoubleClick={() => navigate(`/canvas?connectionId=${c.id}`)}
                  title="Kanvası açmak için çift tıklayın"
                  style={{ cursor: 'pointer', userSelect: 'none' }}
                >
                  <div className="db-tile-head">
                    <span className="db-tile-icon" />
                    <span className="db-tile-name">
                      <strong>{c.name}</strong>
                      <span className="db-tile-sub">
                        {c.host}
                        {c.port ? `:${c.port}` : ''}
                      </span>
                    </span>
                  </div>
                  <div className="db-tile-foot">
                    <span className="db-badge db-badge--ok">
                      <i />
                      {c.lastConnectedAt ? 'Bağlandı' : 'Kayıtlı'}
                    </span>
                    <span className="db-tile-db">{c.database}</span>
                  </div>
                </div>
              ))}

            <button
              className="db-tile db-tile--add"
              onClick={() => navigate('/settings/sql-connection')}
            >
              <span className="db-plus">+</span>
              <span>Bağlantı ekle</span>
            </button>
          </div>
        </div>
      </section>

      <section className="db-card">
        <div className="db-card-head">
          <h2>Son analizler</h2>
          <span className="db-hint">{completedAnalyses.length} kanvas</span>
        </div>

        {completedAnalyses.length === 0 ? (
          <p className="db-empty">
            Henüz analiz yok. SQL Bağlantı Ayarları sayfasından bir bağlantı seçip “Analiz Et”e
            tıklayın.
          </p>
        ) : (
          <div className="db-analyses">
            {completedAnalyses.map((a) => (
              <div
                key={a.requestId}
                className={`db-analysis ${a.status === 'failed' ? 'is-failed' : ''}`}
                onDoubleClick={() => navigate(`/canvas/${a.requestId}`)}
                title="Kanvası açmak için çift tıklayın"
              >
                <div className="db-analysis-top">
                  <span className="db-analysis-name">{a.database}</span>
                  <span
                    className={`db-badge ${a.status === 'failed' ? 'db-badge--err' : 'db-badge--ok'}`}
                  >
                    <i />
                    {a.status === 'failed' ? 'Başarısız' : 'Hazır'}
                  </span>
                </div>
                <div className="db-analysis-meta">
                  <span>{a.tables?.length ?? 0} tablo</span>
                  <span>{fmtDate(a.completedAt || a.updatedAt)}</span>
                </div>
              </div>
            ))}
          </div>
        )}
      </section>

      <section className="db-card">
        <div className="db-card-head">
          <h2>Veritabanı bağlantıları</h2>
          <button className="db-link" onClick={() => navigate('/settings/sql-connection')}>
            Tümünü yönet →
          </button>
        </div>

        {!loadingConns && connections.length === 0 && !connError && (
          <p className="db-empty">
            Henüz kayıtlı bağlantı yok. “Bağlantı ekle” ile ilk veri kaynağınızı tanımlayın.
          </p>
        )}

        {connections.length > 0 && (
          <div className="db-table-wrap">
            <table className="db-table">
              <thead>
                <tr>
                  <th>Bağlantı</th>
                  <th>Sunucu</th>
                  <th>Veritabanı</th>
                  <th>Kullanıcı</th>
                  <th>Son bağlantı</th>
                </tr>
              </thead>
              <tbody>
                {connections.map((c) => (
                  <tr key={c.id}>
                    <td className="db-strong">{c.name}</td>
                    <td className="db-mono">
                      {c.host}
                      {c.port ? `:${c.port}` : ''}
                    </td>
                    <td>{c.database}</td>
                    <td className="db-mono">{c.username}</td>
                    <td className="db-mono">{fmtDate(c.lastConnectedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  );
};

export default DashboardPage;
