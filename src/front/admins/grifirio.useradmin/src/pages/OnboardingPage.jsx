import { useEffect, useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  TEST_PAYMENT_ENABLED,
  createCompany,
  createOrder,
  fetchPlans,
  payOrder,
} from '../services/onboardingService';
import '../styles/OnboardingPage.css';

const TEAM_SIZES = ['1–5', '6–20', '21–100', '100+'];

// Vitrindeki paket adlari ile Identity'nin plan kodlari arasindaki kopru.
// Kodlar SubscriptionPlans ile birebir ayni olmak zorunda.
const PLAN_LABELS = {
  TRIAL: 'Deneme',
  STANDARD: 'Takım',
  ENTERPRISE: 'Kurumsal',
};

const STEPS = ['Çalışma alanı', 'Paket', 'Ödeme'];

const planCodeOf = (p) => p.subscriptionPlan ?? p.SubscriptionPlan;

export default function OnboardingPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;

  // Vitrindeki "Takim'i sec" dugmesi plani sorgu dizesinde tasiyor; kullanici
  // ayni secimi burada bir daha yapmasin.
  const preselected = useMemo(
    () => new URLSearchParams(window.location.search).get('plan'),
    []
  );

  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const [company, setCompany] = useState({ name: '', code: '', teamSize: TEAM_SIZES[1] });
  const [companyId, setCompanyId] = useState(null);

  const [plans, setPlans] = useState([]);
  const [planId, setPlanId] = useState(null);

  const [address, setAddress] = useState({
    province: '',
    district: '',
    street: '',
    zipCode: '',
  });

  const [order, setOrder] = useState(null);
  const [done, setDone] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const list = await fetchPlans(token);
        if (cancelled) return;
        setPlans(list);
        const match = preselected && list.find((p) => planCodeOf(p) === preselected);
        if (match) setPlanId(match.id);
      } catch {
        // Katalog okunamazsa akis durmasin; kullanici paketi elle secer ya da
        // daha sonra secer. Sirket adimi bundan bagimsiz calisiyor.
        if (!cancelled) setPlans([]);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [token, preselected]);

  const selectedPlan = plans.find((p) => p.id === planId) || null;

  const submitCompany = async (e) => {
    e.preventDefault();
    if (!company.name.trim()) {
      setError('Çalışma alanı adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      const created = await createCompany(token, {
        name: company.name.trim(),
        code: company.code.trim(),
        description: `Ekip büyüklüğü: ${company.teamSize}`,
      });
      setCompanyId(created?.id ?? created?.companyId ?? true);
      setStep(1);
    } catch (err) {
      setError(
        err?.response?.data?.detail ||
          err?.response?.data?.title ||
          'Çalışma alanı oluşturulamadı. Lütfen tekrar deneyin.'
      );
    } finally {
      setBusy(false);
    }
  };

  const submitPlan = (e) => {
    e.preventDefault();
    if (!planId) {
      setError('Bir paket seçin.');
      return;
    }
    setError('');
    setStep(2);
  };

  const submitPayment = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const created = await createOrder(token, {
        productId: planId,
        quantity: 1,
        address,
      });
      setOrder(created);

      const code = created?.orderCode ?? created?.code;
      await payOrder(token, { orderCode: code, amount: selectedPlan?.price ?? 0 });

      // Abonelik, odeme olayini Identity tuketince aciliyor; birkac saniye
      // surebilir. Kullaniciyi bos bir ekranda birakmamak icin burada
      // bitmis sayiyoruz, erisim kontrolu bir sonraki yuklemede zaten yapiliyor.
      setDone(true);
    } catch (err) {
      setError(
        err?.response?.data?.detail ||
          err?.response?.data?.title ||
          'Ödeme tamamlanamadı. Sipariş oluştuysa tekrar denemeden önce bizimle iletişime geçin.'
      );
    } finally {
      setBusy(false);
    }
  };

  if (done) {
    return (
      <div className="ob">
        <div className="ob-card ob-card--done">
          <span className="ob-tick" aria-hidden="true">✓</span>
          <h1>Çalışma alanın hazır.</h1>
          <p>
            <strong>{company.name}</strong> için {PLAN_LABELS[planCodeOf(selectedPlan || {})] ||
              'paket'} aboneliği açıldı. Şimdi ilk veri kaynağını bağlayabilirsin.
          </p>
          <a className="ob-btn" href="/">Panele git</a>
        </div>
      </div>
    );
  }

  return (
    <div className="ob">
      <div className="ob-card">
        <ol className="ob-steps">
          {STEPS.map((label, i) => (
            <li key={label} className={i === step ? 'is-current' : i < step ? 'is-done' : ''}>
              <span className="ob-step-n">{i < step ? '✓' : i + 1}</span>
              {label}
            </li>
          ))}
        </ol>

        {error && <div className="ob-error">{error}</div>}

        {step === 0 && (
          <form className="ob-form" onSubmit={submitCompany}>
            <h1>Çalışma alanını kuralım.</h1>
            <p className="ob-lead">
              Dashboard’lar, veri kaynakları ve ekip yetkileri bu alanın altında durur.
            </p>

            <label className="ob-field">
              <span>Şirket / çalışma alanı adı</span>
              <input
                value={company.name}
                onChange={(e) => setCompany({ ...company, name: e.target.value })}
                placeholder="Enco"
                autoFocus
              />
            </label>

            <label className="ob-field">
              <span>
                Kısa kod <i>isteğe bağlı</i>
              </span>
              <input
                value={company.code}
                onChange={(e) => setCompany({ ...company, code: e.target.value })}
                placeholder="ENCO"
              />
            </label>

            <fieldset className="ob-field">
              <span>Ekipte kaç kişi var?</span>
              <div className="ob-chips">
                {TEAM_SIZES.map((s) => (
                  <button
                    type="button"
                    key={s}
                    className={company.teamSize === s ? 'is-active' : ''}
                    onClick={() => setCompany({ ...company, teamSize: s })}
                  >
                    {s}
                  </button>
                ))}
              </div>
            </fieldset>

            <button className="ob-btn" type="submit" disabled={busy}>
              {busy ? 'Oluşturuluyor…' : 'Devam et →'}
            </button>
          </form>
        )}

        {step === 1 && (
          <form className="ob-form" onSubmit={submitPlan}>
            <h1>Paketini seç.</h1>
            <p className="ob-lead">Sonradan yükseltebilirsin; değişiklik aynı gün geçerli olur.</p>

            {plans.length === 0 ? (
              <p className="ob-empty">
                Paket listesi alınamadı. Sayfayı yenilemeyi deneyin ya da bizimle iletişime geçin.
              </p>
            ) : (
              <div className="ob-plans">
                {plans.map((p) => {
                  const code = planCodeOf(p);
                  return (
                    <label key={p.id} className={`ob-plan ${planId === p.id ? 'is-active' : ''}`}>
                      <input
                        type="radio"
                        name="plan"
                        checked={planId === p.id}
                        onChange={() => setPlanId(p.id)}
                      />
                      <span className="ob-plan-name">{PLAN_LABELS[code] || p.name}</span>
                      <span className="ob-plan-price">
                        {p.price > 0 ? `₺${p.price.toLocaleString('tr-TR')}` : 'Ücretsiz'}
                      </span>
                      {p.description && <span className="ob-plan-desc">{p.description}</span>}
                    </label>
                  );
                })}
              </div>
            )}

            <div className="ob-actions">
              <button className="ob-btn ob-btn--ghost" type="button" onClick={() => setStep(0)}>
                ← Geri
              </button>
              <button className="ob-btn" type="submit" disabled={!planId}>
                Devam et →
              </button>
            </div>
          </form>
        )}

        {step === 2 && (
          <form className="ob-form" onSubmit={submitPayment}>
            <h1>Son adım.</h1>
            <p className="ob-lead">Fatura adresi siparişe işlenir.</p>

            <div className="ob-summary">
              <span>{PLAN_LABELS[planCodeOf(selectedPlan || {})] || selectedPlan?.name}</span>
              <strong>
                {selectedPlan?.price > 0
                  ? `₺${selectedPlan.price.toLocaleString('tr-TR')}`
                  : 'Ücretsiz'}
              </strong>
            </div>

            <div className="ob-row">
              <label className="ob-field">
                <span>İl</span>
                <input
                  value={address.province}
                  onChange={(e) => setAddress({ ...address, province: e.target.value })}
                  placeholder="İstanbul"
                />
              </label>
              <label className="ob-field">
                <span>İlçe</span>
                <input
                  value={address.district}
                  onChange={(e) => setAddress({ ...address, district: e.target.value })}
                  placeholder="Kadıköy"
                />
              </label>
            </div>

            <label className="ob-field">
              <span>Adres</span>
              <input
                value={address.street}
                onChange={(e) => setAddress({ ...address, street: e.target.value })}
                placeholder="Cadde, sokak, no"
              />
            </label>

            <label className="ob-field">
              <span>Posta kodu</span>
              <input
                value={address.zipCode}
                onChange={(e) => setAddress({ ...address, zipCode: e.target.value })}
                placeholder="34000"
              />
            </label>

            {TEST_PAYMENT_ENABLED && (
              <p className="ob-notice">
                Ödeme sağlayıcısı henüz bağlanmadı. Bu adım siparişi oluşturup test geçişiyle
                tamamlar; kart bilgisi istenmez ve hiçbir tahsilat yapılmaz.
              </p>
            )}

            <div className="ob-actions">
              <button className="ob-btn ob-btn--ghost" type="button" onClick={() => setStep(1)}>
                ← Geri
              </button>
              <button className="ob-btn" type="submit" disabled={busy}>
                {busy ? 'Tamamlanıyor…' : 'Aboneliği başlat →'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
