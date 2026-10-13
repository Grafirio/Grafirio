import { EVENT_LABELS } from '../../constants/ops';
import { formatDateTime } from '../../utils/opsView';

export default function EventsPanel({ events }) {
  return (
    <section className="gf-card pa-section">
      <div className="gf-card__header"><h3>Son uyarılar</h3></div>
      <div className="gf-card__body">
        {events.length === 0 ? (
          <p className="gf-muted">Ajan başladığından beri durum değişikliği olmadı.</p>
        ) : (
          <ul className="ops-list">
            {events.map((event) => (
              <li key={`${event.at}-${event.serviceId}-${event.type}`}>
                <span className={`gf-badge${event.type === 'recovered' ? ' gf-badge--success' : ' gf-badge--danger'}`}>
                  {EVENT_LABELS[event.type] ?? event.type}
                </span>
                <span>{event.message}</span>
                <small className="gf-muted">{formatDateTime(event.at)}</small>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
