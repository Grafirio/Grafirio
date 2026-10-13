import { ACTION_LABELS } from '../../constants/ops';
import { getAvailableActions } from '../../utils/opsView';

export default function ServiceActions({ service, dockerAvailable, isBusy, onAction }) {
  const { actions, reason } = getAvailableActions(service, dockerAvailable);

  return (
    <div className="ops-actions">
      {Object.entries(ACTION_LABELS).map(([action, label]) => (
        <button
          key={action}
          className="gf-btn gf-btn--sm"
          disabled={isBusy || !actions.has(action)}
          title={reason ?? undefined}
          onClick={() => onAction(service, action)}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
