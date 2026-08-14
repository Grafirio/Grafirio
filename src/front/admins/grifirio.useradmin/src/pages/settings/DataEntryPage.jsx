import { useCallback, useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getSavedConnections } from '../../services/dataAnalysisService';
import '../../styles/SettingsPages.css';

/**
 * Ayarlar › Veri Girdisi.
 *
 * Taslakta bu sayfanin merkezinde dosya surukle-birak var. Dosya alan bir uc
 * henuz yok — DataAnalysis.Api yalnizca SQL baglantisi uzerinden calisiyor —
 * bu yuzden surukleme alani kapali duruyor ve sayfanin agirligi gercekten
 * calisan yola, yani bagli veri kaynaklarina veriliyor.
 *
 * "Son yuklemeler" ve "kolon eslemesi" tablolarini sahte satirlarla
 * doldurmadim; ikisi de yukleme kaydi tutan bir servise bagli ve o servis
 * yazildiginda buraya gercek veriyle gelecekler.
 */
// embedded: DataSourcesPage bu bileseni "Dosya yukle" sekmesinde gosterir ve
// kendi sayfa basligini kendisi cizer.
export default function DataEntryPage({ embedded = false } = {}) {
  const navigate = useNavigate();

  const [connections, setConnections] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const result = await getSavedConnections();
      setConnections(result?.connections ?? result?.data ?? []);
    } catch (err) {
      setError(
        err?.response?.status === 401
          ? 'Oturumunuz doğrulanamadı. Sayfayı yenileyip tekrar deneyin.'
          : 'Veri kaynakları okunamadı.'
      );
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  const fmtDate = (str) =>
    str ? new Date(str).toLocaleDateString('tr-TR', { day: '2-digit', month: 'short', year: 'numeric' }) : '—';

  return (
    <div className={embedded ? 'st-embed' : 'st'}>
      {!embedded && (
        <div className="st-head">
          <div>
            <p className="st-eyebrow">Ayarlar · Veri</p>
            <h1>Veri Girdisi</h1>
            <p className="st-lead">
              Analiz edilecek veriyi panele bağlayın. Şu an desteklenen yol, okuma yetkisi verilmiş
              bir veritabanı bağlantısıdır.
            </p>
          </div>
          <span className="st-head-meta">
            {loading ? '—' : `${connections.length} bağlı kaynak`}
          </span>
        </div>
      )}

      <section className="st-dropzone">
        <div className="st-dropzone-icon" aria-hidden="true">
          ↑
        </div>
        <h2>Dosya yükleme henüz açık değil</h2>
        <p>
          Excel, CSV ve JSON yükleme tasarımda var ama dosyayı karşılayacak uç yazılmadı. Bu arada
          veriyi doğrudan veritabanınızdan çekebilirsiniz.
        </p>
        <div className="st-dropzone-actions">
          <button type="button" className="st-btn" onClick={() => navigate('/data?tab=connections')}>
            Veritabanından çek
          </button>
          <button type="button" className="st-btn st-btn--ghost" disabled>
            Dosya seç
          </button>
        </div>
        <div className="st-dropzone-formats">.xlsx · .csv · .json — planlanan biçimler</div>
      </section>

      {error && <div className="st-alert">{error}</div>}

      {/* embedded modda (DataSourcesPage icinde) bu tablo "Baglantilar"
          sekmesiyle birebir ayni veriyi tekrar gostermis olurdu; bu yuzden
          yalnizca bagimsiz erisimde (eski /settings/data-input) gorunur. */}
      {!embedded && (
      <section className="st-card">
        <div className="st-card-head">
          <div>
            <h2>Bağlı veri kaynakları</h2>
            <p className="st-card-sub">
              Panelin okuduğu veritabanları. Yenisini eklemek ya da tablo seçmek için bağlantı
              ayarlarına gidin.
            </p>
          </div>
          <button type="button" className="st-link" onClick={() => navigate('/data?tab=connections')}>
            Tümünü yönet →
          </button>
        </div>

        {loading && <p className="st-empty">Yükleniyor…</p>}

        {!loading && connections.length === 0 && !error && (
          <p className="st-empty">
            Henüz bağlı bir veri kaynağı yok. “Veritabanından çek” ile ilk bağlantınızı tanımlayın.
          </p>
        )}

        {!loading && connections.length > 0 && (
          <div className="st-table-wrap">
            <table className="st-table">
              <thead>
                <tr>
                  <th>Kaynak</th>
                  <th>Sunucu</th>
                  <th>Veritabanı</th>
                  <th>Kullanıcı</th>
                  <th>Son bağlantı</th>
                </tr>
              </thead>
              <tbody>
                {connections.map((c) => (
                  <tr key={c.id}>
                    <td className="st-strong">{c.name}</td>
                    <td className="st-mono st-dim">
                      {c.host}
                      {c.port ? `:${c.port}` : ''}
                    </td>
                    <td>{c.database}</td>
                    <td className="st-mono st-dim">{c.username}</td>
                    <td className="st-mono st-dim">{fmtDate(c.lastConnectedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
      )}

      <div className="st-note">
        <i>i</i>
        <div>
          Yükleme geçmişi ve kolon eşleme ekranı, dosya yükleme ucu yazıldığında bu sayfaya
          eklenecek. O zamana kadar veri kaynağınızın tablolarını{' '}
          <strong>Bağlantılar sekmesi › Tablo Seç</strong> üzerinden belirliyorsunuz.
        </div>
      </div>
    </div>
  );
}
