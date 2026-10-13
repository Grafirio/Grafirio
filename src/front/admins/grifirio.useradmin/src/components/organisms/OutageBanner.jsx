import { useOutageSummary } from '../../hooks/useOutageSummary.js';
import { OUTAGE_STATUS_DEGRADED } from '../../constants/outageApi.js';
import '../../styles/OutageBanner.css';

/** Tells users a platform component is down so a failing page is not mistaken for lost data. */
const OutageBanner = () => {
  const summary = useOutageSummary();
  if (summary?.status !== OUTAGE_STATUS_DEGRADED) return null;

  return (
    <div className="outage-banner" role="status">
      <strong>Bazı hizmetler geçici olarak kullanılamıyor.</strong>
      {' '}Kayıtlı verileriniz silinmedi; ekibimiz durumu inceliyor.
      {summary.affectedFeatures.length > 0 && (
        <ul className="outage-banner-list">
          {summary.affectedFeatures.map((feature) => <li key={feature}>{feature}</li>)}
        </ul>
      )}
    </div>
  );
};

export default OutageBanner;
