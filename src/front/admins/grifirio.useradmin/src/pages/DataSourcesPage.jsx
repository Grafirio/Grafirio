import { useSearchParams } from 'react-router-dom';
import SqlConnectionSettings from './SqlConnectionSettings';
import DataEntryPage from './settings/DataEntryPage';
import '../styles/SettingsPages.css';

// Veri kaynaklari: eskiden "Veri Girdisi" ve "SQL Baglanti Ayarlari" iki ayri
// menu girdisi, iki ayri sayfaydi. Ikisi de ayni isi yapiyor — panele veri
// baglamak — bu yuzden tek sayfada iki sekmeye indirildi. Icerideki iki
// bilesen (SqlConnectionSettings, DataEntryPage) degismedi, yalnizca
// "embedded" modda kendi basliklarini gizleyip buraya tasindilar.
const TABS = [
  { key: 'connections', label: 'Bağlantılar' },
  { key: 'upload', label: 'Dosya yükle' },
];

export default function DataSourcesPage() {
  const [params, setParams] = useSearchParams();
  const tab = TABS.some((t) => t.key === params.get('tab')) ? params.get('tab') : 'connections';

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Panel · Veri</p>
          <h1>Veri kaynakları</h1>
          <p className="st-lead">
            Veritabanı bağlantısı ve dosya girişi tek yerde. Önceden bunlar iki menü ve iki sayfaya
            bölünmüştü.
          </p>
        </div>
      </div>

      <div className="st-tabs" role="tablist">
        {TABS.map((t) => (
          <button
            key={t.key}
            type="button"
            role="tab"
            aria-selected={tab === t.key}
            className={tab === t.key ? 'is-active' : ''}
            onClick={() => setParams(t.key === 'connections' ? {} : { tab: t.key })}
          >
            {t.label}
          </button>
        ))}
      </div>

      {tab === 'connections' && <SqlConnectionSettings embedded />}
      {tab === 'upload' && <DataEntryPage embedded />}
    </div>
  );
}
