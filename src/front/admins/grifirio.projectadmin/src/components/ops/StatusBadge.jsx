import { STATUS_BADGES, SERVICE_STATUS } from '../../constants/ops';

export default function StatusBadge({ status }) {
  const badge = STATUS_BADGES[status] ?? STATUS_BADGES[SERVICE_STATUS.UNKNOWN];
  return <span className={badge.className}>{badge.label}</span>;
}
