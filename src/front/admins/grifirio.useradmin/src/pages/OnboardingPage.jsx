import { useMemo, useState } from 'react';
import { useKeycloak } from '@react-keycloak/web';
import {
  PLANS,
  describeError,
  onboardCompany,
  planByCode,
  startSubscription,
} from '../services/onboardingService';
import '../styles/OnboardingPage.css';

const TEAM_SIZES = ['1–5', '6–20', '21–100', '100+'];
const STEPS = ['Çalışma alanı', 'Paket'];

export default function OnboardingPage() {
  const { keycloak } = useKeycloak();
  const token = keycloak.token;

  // Vitrindeki paket dugmesi secimi sorgu dizesinde tasiyor; kullanici ayni
  // secimi burada bir daha yapmasin.
  const preselected = useMemo(
    () => new URLSearchParams(window.location.search).get('plan'),
    []
  );

  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const [company, setCompany] = useState({ name: '', code: '', teamSize: TEAM_SIZES[1] });
  const [planCode, setPlanCode] = useState(
    planByCode(preselected) ? preselected : PLANS[0].code
  );
  const [done, setDone] = useState(null);

  const selectedPlan = planByCode(planCode);

  const submitCompany = async (e) => {
    e.preventDefault();
    if (!company.name.trim()) {
      setError('Çalışma alanı adı gerekli.');
      return;
    }
    setBusy(true);
    setError('');
    try {
      await onboardCompany(token, {
        name: company.name.trim(),
        code: company.code.trim(),
        description: `Ekip büyüklüğü: ${company.teamSize}`,
      });
      setStep(1);
    } catch (err) {
      setError(describeError(err, 'Çalışma alanı oluşturulamadı. Lütfen tekrar deneyin.'));
    } finally {
      setBusy(false);
    }
  };

  const submitPlan = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError('');
    try {
      const sub = await startSubscription(token, planCode);
      setDone(sub);

      // Yetki Keycloak'taki company_id / business_roles niteliklerine yazildi,
      // ama elimizdeki token o niteliklerden once alinmisti. Yenilemeden panele
      // gidersek kullanici kendi kurdugu firmayi goremez.
      await keycloak.updateToken(-1).catch(() => {});
    } catch (err) {
      setError(describeError(err, 'Abonelik başlatılamadı. Lütfen tekrar deneyin.'));
    } finally {
      setBusy(false);
    }
  };

  if (done) {
    const trialEnds = done.trialEndsAt ? new Date(done.trialEndsAt) : null;
    return (
      <div className="ob">
        <div className="ob-card ob-card--done">
          <span className="ob-tick" aria-hidden="true">✓</span>
          <h1>Çalışma alanın hazır.</h1>
          <p>
            <strong>{company.name}</strong> için {selectedPlan?.name} paketi açıldı.
            {trialEnds && (
              <>
                {' '}
                Ücretsiz denemen{' '}
                <strong>{trialEnds.toLocaleDateString('tr-TR')}</strong> tarihine kadar sürüyor.
              </>
            )}
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
                placeholder="Akdeniz Tekstil"
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
                placeholder="AKDENIZ"
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
            <p className="ob-lead">Sonradan değiştirebilirsin; değişiklik aynı gün geçerli olur.</p>

            <div className="ob-plans">
              {PLANS.map((p) => (
                <label key={p.code} className={`ob-plan ${planCode === p.code ? 'is-active' : ''}`}>
                  <input
                    type="radio"
                    name="plan"
                    checked={planCode === p.code}
                    onChange={() => setPlanCode(p.code)}
                  />
                  <span className="ob-plan-name">{p.name}</span>
                  <span className="ob-plan-price">
                    {p.price}
                    {p.unit && <i>{p.unit}</i>}
                  </span>
                  <span className="ob-plan-desc">{p.description}</span>
                </label>
              ))}
            </div>

            <p className="ob-notice">
              Ödeme sağlayıcısı henüz bağlanmadı. Şimdilik kart bilgisi istenmiyor ve hiçbir
              tahsilat yapılmıyor; aboneliğin doğrudan açılıyor.
            </p>

            <div className="ob-actions">
              <button className="ob-btn ob-btn--ghost" type="button" onClick={() => setStep(0)}>
                ← Geri
              </button>
              <button className="ob-btn" type="submit" disabled={busy}>
                {busy ? 'Açılıyor…' : 'Kullanmaya başla →'}
              </button>
            </div>
          </form>
        )}
      </div>
    </div>
  );
}
