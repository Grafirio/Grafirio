import StatusBadge from './StatusBadge';
import ServiceActions from './ServiceActions';

export default function ServiceRow({ service, dockerAvailable, isBusy, onAction }) {
  const { autoHeal } = service;

  return (
    <tr>
      <td>
        <strong>{service.name}</strong>
        <small className="ops-sub">{service.impact}</small>
      </td>
      <td>
        <StatusBadge status={service.status} />
        {service.manuallyStopped && <small className="ops-sub">Bilerek durduruldu</small>}
      </td>
      <td>
        {service.latencyMs != null ? `${service.latencyMs} ms` : '—'}
        {service.detail && <small className="ops-sub">{service.detail}</small>}
      </td>
      <td>
        {service.container?.status ?? '—'}
        {service.container?.health && <small className="ops-sub">{service.container.health}</small>}
      </td>
      <td>
        {service.error && <span className="ops-error">{service.error}</span>}
        {autoHeal.gaveUp && <small className="ops-sub ops-error">Otomatik onarma vazgeçti ({autoHeal.attempts} deneme)</small>}
        {!autoHeal.gaveUp && autoHeal.attempts > 0 && (
          <small className="ops-sub">Otomatik onarma: {autoHeal.attempts}. deneme</small>
        )}
      </td>
      <td>
        <ServiceActions service={service} dockerAvailable={dockerAvailable} isBusy={isBusy} onAction={onAction} />
      </td>
    </tr>
  );
}
