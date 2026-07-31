import { useState } from 'react';
import { planCheckoutUrl, CONTACT_EMAIL } from '../config';

// Paket kodlari Identity'deki SubscriptionPlans ile birebir ayni (TRIAL,
// STANDARD, ENTERPRISE). Vitrinde gorunen adlar degisebilir ama kodlar
// degisemez; ikisi ayrisirsa musteri aldigini sandigi seyi almamis olur.
const plans = [
  {
    code: 'TRIAL',
    name: 'Deneme',
    accent: '#0E8F8C',
    tagline: 'Verinin işe yarayıp yaramadığını görmek için.',
    price: { monthly: '₺0', yearly: '₺0' },
    unit: '14 gün',
    cta: 'Ücretsiz başla',
    href: null,
    features: [
      '1 veri kaynağı',
      '3 kullanıcı',
      'Sohbet ederek analiz',
      'Kanvas ve temel widget’lar',
      'E-posta desteği',
    ],
  },
  {
    code: 'STANDARD',
    name: 'Takım',
    accent: '#F8C630',
    tagline: 'Ekibin her gün açtığı yer.',
    price: { monthly: '₺2.900', yearly: '₺2.320' },
    unit: '/ ay',
    featured: true,
    badge: 'En çok seçilen',
    cta: 'Takım’ı seç',
    href: null,
    features: [
      '5 veri kaynağı',
      '25 kullanıcı',
      'Sınırsız kanvas ve paylaşım',
      'Departman ve yetki yönetimi',
      'Tahmin modelleri',
      'Öncelikli destek',
    ],
  },
  {
    code: 'ENTERPRISE',
    name: 'Kurumsal',
    accent: '#8A2E8E',
    tagline: 'Birden çok şirket, birden çok kaynak, kendi kuralların.',
    price: { monthly: 'Görüşelim', yearly: 'Görüşelim' },
    unit: '',
    cta: 'Bize yazın',
    href: `mailto:${CONTACT_EMAIL}?subject=Grafirio%20Kurumsal%20paket`,
    features: [
      'Sınırsız kaynak ve kullanıcı',
      'Kendi kimlik sağlayıcın (SSO)',
      'Özel veri kaynağı entegrasyonu',
      'Ayrılmış kaynak ve SLA',
      'Kurulum ve eğitim desteği',
    ],
  },
];

export default function Pricing() {
  const [yearly, setYearly] = useState(true);

  return (
    <section className="section" id="fiyatlar">
      <div className="wrap">
        <div className="section-head is-center">
          <p className="mono eyebrow" style={{ color: '#F0902B' }}>
            Fiyatlandırma
          </p>
          <h2 className="section-title">Kullandığın kadar büyüyen bir plan</h2>
          <p className="section-lead">
            Denemeyle başla, ihtiyaç duydukça büyüt. Paket değişikliği aynı gün geçerli olur.
          </p>

          <div className="toggle" role="group" aria-label="Faturalama dönemi">
            <button
              type="button"
              className={!yearly ? 'is-active' : ''}
              onClick={() => setYearly(false)}
              aria-pressed={!yearly}
            >
              Aylık
            </button>
            <button
              type="button"
              className={yearly ? 'is-active' : ''}
              onClick={() => setYearly(true)}
              aria-pressed={yearly}
            >
              Yıllık <span className="toggle-save">%20</span>
            </button>
          </div>
        </div>

        <div className="plan-grid">
          {plans.map((p) => (
            <article
              className={`plan ${p.featured ? 'is-featured' : ''}`}
              style={{ '--accent': p.accent }}
              key={p.code}
            >
              {p.badge && <span className="plan-badge">{p.badge}</span>}

              <span className="plan-rule" aria-hidden="true" />
              <h3 className="plan-name">{p.name}</h3>
              <p className="plan-tagline">{p.tagline}</p>

              <div className="plan-price">
                <strong>{yearly ? p.price.yearly : p.price.monthly}</strong>
                {p.unit && <span>{p.unit}</span>}
              </div>
              <p className="mono plan-note">
                {p.code === 'STANDARD'
                  ? `${yearly ? 'yıllık ödemede, aylık karşılığı' : 'aylık ödemede'} · KDV hariç`
                  : ' '}
              </p>

              <a
                className={`btn btn-block ${p.featured ? 'btn-primary' : 'btn-secondary'}`}
                href={p.href ?? planCheckoutUrl(p.code)}
              >
                {p.cta}
              </a>

              <ul className="plan-features">
                {p.features.map((f) => (
                  <li key={f}>
                    <span className="check" aria-hidden="true">
                      ✓
                    </span>
                    {f}
                  </li>
                ))}
              </ul>
            </article>
          ))}
        </div>
      </div>
    </section>
  );
}
