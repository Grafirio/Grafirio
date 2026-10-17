import { getDownServices } from '../../utils/opsView';

/** What users cannot do right now, derived from the services that are down. */
export default function ImpactSummary({ services }) {
  const down = getDownServices(services);
  if (down.length === 0) {
    if (!services.length || services.some((service) => service.status !== 'up')) {
      return <div className="gf-alert gf-alert--warning" role="status">Bazı servislerin durumu henüz doğrulanamadı.</div>;
    }
    return <div className="gf-alert gf-alert--success" role="status">Tüm servisler çalışıyor.</div>;
  }

  return (
    <div className="gf-alert gf-alert--danger" role="alert">
      <strong>{down.length} servis kapalı. Kullanıcıları şu an etkileyenler:</strong>
      <ul className="ops-impact">
        {down.map((service) => (
          <li key={service.id}><strong>{service.name}:</strong> {service.impact}</li>
        ))}
      </ul>
    </div>
  );
}
