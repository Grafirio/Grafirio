import { useNavigate } from 'react-router-dom';
import '../styles/SettingsPages.css';

// Yedi ayar sayfasi onceden iki acilir menude sakliydi. Artik hepsi burada,
// ne ise yaradiklariyla birlikte — kart basligina degil aciklamaya bakip da
// dogru sayfayi bulmak zorunda kalinmasin diye.
const CARDS = [
  { to: '/settings/company', glyph: 'Ş', title: 'Şirket Ayarları', desc: 'Yasal kimlik, vergi ve sicil bilgileri, adresler ve banka hesapları.' },
  // Belgeler Şirket Ayarları'nın bir sekmesi ama hub'dan doğrudan
  // açılabiliyor: evrak aramak için önce şirket ayarlarına girip sekme
  // bulmak gereksiz bir adım.
  { to: '/settings/company?tab=documents', glyph: 'E', title: 'Belgeler', desc: 'Vergi levhası, imza sirküleri ve sicil evrakı — önizlemeli.' },
  { to: '/settings/users', glyph: 'K', title: 'Kullanıcılar', desc: 'Kim var, hangi rolde, o rol neye izin veriyor.' },
  { to: '/settings/department', glyph: 'D', title: 'Departmanlar', desc: 'Kullanıcının hangi analizleri göreceğini belirler.' },
  { to: '/settings/theme', glyph: 'G', title: 'Görünüm ve Tema', desc: 'Açık/karanlık mod, marka rengi ve yoğunluk.' },
  { to: '/settings/notifications', glyph: 'B', title: 'Bildirimler', desc: 'Hangi olay, kime, hangi kanaldan.' },
];
// Üyelik burada değil: taşıma taslağında da üst seviyede kendi sekmesi
// var (bkz. Nav.jsx TABS) — faturayı görmek iki tıklık bir iş olmasın diye.

export default function SettingsHubPage() {
  const navigate = useNavigate();

  return (
    <div className="st">
      <div className="st-head">
        <div>
          <p className="st-eyebrow">Panel · Ayarlar</p>
          <h1>Ayarlar</h1>
          <p className="st-lead">Şirket, kullanıcı, yetki ve üyelik işlemlerinin hepsi burada.</p>
        </div>
      </div>

      <div className="st-hub-grid">
        {CARDS.map((c) => (
          <button key={c.to} type="button" className="st-hub-card" onClick={() => navigate(c.to)}>
            <span className="st-hub-card-head">
              <span className="st-hub-glyph">{c.glyph}</span>
              <strong>{c.title}</strong>
              <span className="st-hub-arrow">→</span>
            </span>
            <span className="st-hub-desc">{c.desc}</span>
          </button>
        ))}
      </div>
    </div>
  );
}
