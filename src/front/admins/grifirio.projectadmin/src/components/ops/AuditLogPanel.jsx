import { useAuditLog } from '../../hooks/useAuditLog';
import { formatDateTime } from '../../utils/opsView';

export default function AuditLogPanel({ version }) {
  const { entries, error } = useAuditLog(version);

  return (
    <section className="gf-card pa-section">
      <div className="gf-card__header"><h3>Denetim kaydı</h3></div>
      <div className="gf-card__body">
        {error && <div className="gf-alert gf-alert--danger" role="alert">{error}</div>}
        {!error && entries.length === 0 && <p className="gf-muted">Henüz kayıt yok.</p>}
        {entries.length > 0 && (
          <div className="ops-table-wrap">
            <table className="pa-table">
              <thead>
                <tr>
                  <th scope="col">Zaman</th>
                  <th scope="col">Kim</th>
                  <th scope="col">İşlem</th>
                  <th scope="col">Hedef</th>
                  <th scope="col">Sonuç</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => (
                  <tr key={`${entry.at}-${entry.action}-${entry.target}`}>
                    <td>{formatDateTime(entry.at)}</td>
                    <td>{entry.actor.id} <small className="gf-muted">({entry.actor.type})</small></td>
                    <td>{entry.action}</td>
                    <td>{entry.target}</td>
                    <td className={entry.outcome === 'failed' ? 'ops-error' : ''}>
                      {entry.outcome}
                      {entry.detail && <small className="ops-sub">{entry.detail}</small>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </section>
  );
}
