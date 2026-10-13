import { CATEGORY_LABELS } from '../../constants/ops';
import { groupByCategory } from '../../utils/opsView';
import ServiceRow from './ServiceRow';

export default function ServiceTable({ services, dockerAvailable, isBusy, onAction }) {
  return (
    <>
      {groupByCategory(services).map(([category, items]) => (
        <section key={category} className="gf-card pa-section">
          <div className="gf-card__header">
            <h3>{CATEGORY_LABELS[category] ?? category}</h3>
          </div>
          <div className="ops-table-wrap">
            <table className="pa-table">
              <thead>
                <tr>
                  <th scope="col">Servis / etkisi</th>
                  <th scope="col">Durum</th>
                  <th scope="col">Yanıt</th>
                  <th scope="col">Konteyner</th>
                  <th scope="col">Hata / onarma</th>
                  <th scope="col">İşlem</th>
                </tr>
              </thead>
              <tbody>
                {items.map((service) => (
                  <ServiceRow
                    key={service.id}
                    service={service}
                    dockerAvailable={dockerAvailable}
                    isBusy={isBusy}
                    onAction={onAction}
                  />
                ))}
              </tbody>
            </table>
          </div>
        </section>
      ))}
    </>
  );
}
