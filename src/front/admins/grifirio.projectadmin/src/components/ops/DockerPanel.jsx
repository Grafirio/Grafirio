import OperationNote from './OperationNote';

export default function DockerPanel({ docker, operation, isBusy, onStartDocker, onInfrastructureUp }) {
  return (
    <section className="gf-card pa-section">
      <div className="gf-card__header gf-row gf-row--between">
        <h3>Docker ve altyapı</h3>
        <span className={`gf-badge gf-badge--${docker.available ? 'success' : 'danger'}`}>
          {docker.available ? 'Docker çalışıyor' : 'Docker kapalı'}
        </span>
      </div>
      <div className="gf-card__body gf-stack">
        {!docker.available && (
          <p className="gf-muted">
            Docker çalışmıyor; konteynerler yönetilemez. Önce Docker Desktop&apos;ı başlatın, ardından altyapıyı ayağa kaldırın.
          </p>
        )}
        <div className="gf-row">
          <button className="gf-btn" disabled={isBusy || docker.available} onClick={onStartDocker}>
            Docker&apos;ı başlat
          </button>
          <button className="gf-btn gf-btn--primary" disabled={isBusy || Boolean(operation.active)} onClick={onInfrastructureUp}>
            Altyapıyı ayağa kaldır
          </button>
        </div>
        <OperationNote operation={operation} />
      </div>
    </section>
  );
}
