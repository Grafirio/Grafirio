import { useState } from 'react';
import { AUTO_HEAL_FIELDS } from '../../constants/ops';

export default function AutoHealPanel({ settings, emailAlertsEnabled, isBusy, onSave }) {
  const [draft, setDraft] = useState(settings);

  const handleNumberChange = (key) => (event) => setDraft({ ...draft, [key]: Number(event.target.value) });
  const handleSubmit = (event) => {
    event.preventDefault();
    onSave(draft);
  };

  return (
    <form className="gf-card pa-section" onSubmit={handleSubmit}>
      <div className="gf-card__header gf-row gf-row--between">
        <h3>Kendini onarma</h3>
        <span className={`gf-badge${settings.enabled ? ' gf-badge--success' : ''}`}>
          {settings.enabled ? 'Açık' : 'Kapalı'}
        </span>
      </div>
      <div className="gf-card__body gf-stack">
        <p className="gf-muted">
          Açıkken düşen bir servis eşik kadar art arda başarısız yoklamadan sonra otomatik başlatılır veya yeniden
          başlatılır. Bağımlılığı kapalıysa önce o beklenir; bilerek durdurduğunuz servislere dokunulmaz.
        </p>
        <label className="gf-checkbox">
          <input
            type="checkbox"
            checked={draft.enabled}
            onChange={(event) => setDraft({ ...draft, enabled: event.target.checked })}
          />
          Otomatik onarma açık
        </label>
        <div className="ops-fields">
          {AUTO_HEAL_FIELDS.map(({ key, label, min, max, hint }) => (
            <div className="gf-field" key={key}>
              <label className="gf-label" htmlFor={`autoheal-${key}`}>{label}</label>
              <input
                id={`autoheal-${key}`}
                className="gf-input"
                type="number"
                min={min}
                max={max}
                value={draft[key]}
                onChange={handleNumberChange(key)}
              />
              <span className="gf-hint">{hint}</span>
            </div>
          ))}
        </div>
        <p className="gf-muted gf-text-sm">
          E-posta uyarıları: {emailAlertsEnabled ? 'açık' : 'kapalı (OPS_SMTP_* ve OPS_ALERT_RECIPIENTS tanımlı değil)'}.
        </p>
      </div>
      <div className="gf-card__footer">
        <button type="submit" className="gf-btn gf-btn--primary" disabled={isBusy}>Kaydet</button>
      </div>
    </form>
  );
}
