import { OPERATION_LABELS } from '../../constants/ops';
import { formatDateTime } from '../../utils/opsView';

export default function OperationNote({ operation }) {
  if (operation.active) {
    return (
      <div className="gf-alert gf-alert--warning" role="status">
        <span className="gf-spinner" /> {OPERATION_LABELS[operation.active.name] ?? operation.active.name} sürüyor…
      </div>
    );
  }
  if (!operation.last) return null;

  const succeeded = operation.last.outcome === 'succeeded';
  return (
    <div className={`gf-alert gf-alert--${succeeded ? 'success' : 'danger'}`} role="status">
      {OPERATION_LABELS[operation.last.name] ?? operation.last.name}: {succeeded ? 'tamamlandı' : 'başarısız'}
      {' '}({formatDateTime(operation.last.finishedAt)})
      {!succeeded && operation.last.detail && <pre className="ops-pre">{operation.last.detail}</pre>}
    </div>
  );
}
