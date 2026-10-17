import { useCallback, useState } from 'react';
import { useOpsStatus } from '../hooks/useOpsStatus';
import { useOpsActions } from '../hooks/useOpsActions';
import {
  bringInfrastructureUp, controlService, startDocker, updateAutoHeal,
} from '../services/opsApi';
import { ACTION_LABELS, CONFIRMED_ACTIONS } from '../constants/ops';
import { formatTime } from '../utils/opsView';
import AuditLogPanel from '../components/ops/AuditLogPanel';
import AutoHealPanel from '../components/ops/AutoHealPanel';
import ConfirmDialog from '../components/ops/ConfirmDialog';
import DockerPanel from '../components/ops/DockerPanel';
import EventsPanel from '../components/ops/EventsPanel';
import ImpactSummary from '../components/ops/ImpactSummary';
import ServiceTable from '../components/ops/ServiceTable';
import '../styles/ops.css';

export default function ServicesPage() {
  const { status, error, refresh } = useOpsStatus();
  const { pendingKey, feedback, version, run } = useOpsActions(refresh);
  const [pendingConfirmation, setPendingConfirmation] = useState(null);
  const isBusy = pendingKey !== null;

  const executeServiceAction = useCallback((service, action) => run(
    `${service.id}:${action}`,
    () => controlService(service.id, action),
    `${service.name}: ${ACTION_LABELS[action]} tamamlandı.`,
  ), [run]);

  const handleServiceAction = (service, action) => {
    if (CONFIRMED_ACTIONS.has(action)) setPendingConfirmation({ service, action });
    else executeServiceAction(service, action);
  };

  const handleConfirm = () => {
    const { service, action, execute } = pendingConfirmation;
    setPendingConfirmation(null);
    if (execute) execute();
    else executeServiceAction(service, action);
  };

  const handleStartDocker = () => run('docker', startDocker, 'Docker Desktop başlatılıyor; hazır olması bir dakika sürebilir.');
  const handleInfrastructureUp = () => run('infrastructure', bringInfrastructureUp, 'Altyapı ayağa kaldırılıyor; ilerlemeyi aşağıdan izleyin.');
  const handleSaveAutoHeal = (settings) => run('autoheal', () => updateAutoHeal(settings), 'Otomatik onarma ayarları kaydedildi.');

  return (
    <div className="gf-page">
      <div className="gf-page-header">
        <div>
          <h1 className="gf-page-title">Servisler</h1>
          <p className="gf-page-subtitle">
            Altyapının gerçek durumu ve kontrolü
            {status?.checkedAt && ` · son yoklama ${formatTime(status.checkedAt)}`}
          </p>
        </div>
        <button className="gf-btn" onClick={() => refresh()} disabled={isBusy}>Yenile</button>
      </div>

      {error && <div className="gf-alert gf-alert--danger" role="alert">{error}</div>}
      {feedback && <div className={`gf-alert gf-alert--${feedback.type}`} role="status">{feedback.text}</div>}

      {!status && !error && (
        <div className="gf-card gf-stack" style={{ padding: 'var(--space-5)' }}>
          {[0, 1, 2].map((index) => <div key={index} className="gf-skeleton gf-skeleton--line" />)}
        </div>
      )}

      {status && (
        <>
          <ImpactSummary services={status.services} />
          <DockerPanel
            docker={status.docker}
            operation={status.operation}
            isBusy={isBusy}
            onStartDocker={handleStartDocker}
            onInfrastructureUp={() => setPendingConfirmation({ title: 'Altyapıyı ayağa kaldır',
              message: 'Veritabanı, mesajlaşma ve kimlik altyapısı için eksik konteynerler oluşturulacak ve servisler başlatılacak. İşlem ortak altyapıyı etkiler.',
              confirmLabel: 'Altyapıyı başlat', execute: handleInfrastructureUp })}
          />
          <ServiceTable
            services={status.services}
            dockerAvailable={Boolean(status.docker.available)}
            isBusy={isBusy}
            onAction={handleServiceAction}
          />
          <AutoHealPanel
            settings={status.autoHeal}
            emailAlertsEnabled={status.emailAlertsEnabled}
            isBusy={isBusy}
            onSave={(settings) => setPendingConfirmation({ title: 'Otomatik onarma ayarlarını değiştir',
              message: `Otomatik onarma ${settings.enabled ? 'açılacak' : 'kapatılacak'}. Açık olduğunda eşiklere göre servisler otomatik başlatılabilir veya yeniden başlatılabilir.`,
              confirmLabel: 'Ayarları uygula', execute: () => handleSaveAutoHeal(settings) })}
          />
          <EventsPanel events={status.events} />
          <AuditLogPanel version={version} />
        </>
      )}

      {pendingConfirmation && (
        <ConfirmDialog
          title={pendingConfirmation.title ?? `${pendingConfirmation.service.name}: ${ACTION_LABELS[pendingConfirmation.action]}`}
          message={pendingConfirmation.message ?? `${pendingConfirmation.service.impact} Bu servisi ${ACTION_LABELS[pendingConfirmation.action].toLowerCase()} istediğinize emin misiniz?`}
          confirmLabel={pendingConfirmation.confirmLabel ?? ACTION_LABELS[pendingConfirmation.action]}
          onConfirm={handleConfirm}
          onCancel={() => setPendingConfirmation(null)}
        />
      )}
    </div>
  );
}
