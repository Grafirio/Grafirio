import { CATEGORY_ORDER, SERVICE_ACTION, SERVICE_STATUS } from '../constants/ops';

export const formatTime = (value) => (value ? new Date(value).toLocaleTimeString('tr-TR') : '—');
export const formatDateTime = (value) => (value ? new Date(value).toLocaleString('tr-TR') : '—');

/** Groups services by category in the display order; unknown categories go last. */
export function groupByCategory(services) {
  const groups = new Map();
  for (const service of services) {
    if (!groups.has(service.category)) groups.set(service.category, []);
    groups.get(service.category).push(service);
  }
  const rank = (category) => {
    const index = CATEGORY_ORDER.indexOf(category);
    return index === -1 ? CATEGORY_ORDER.length : index;
  };
  return [...groups.entries()].sort(([left], [right]) => rank(left) - rank(right));
}

/** Which actions make sense right now, with a reason when none do. */
export function getAvailableActions(service, dockerAvailable) {
  if (!dockerAvailable) return { actions: new Set(), reason: 'Docker kapalı' };
  if (!service.container?.exists) {
    return { actions: new Set(), reason: 'Konteyner yok (Docker dışında çalışıyor olabilir)' };
  }
  const actions = service.container.running
    ? new Set([SERVICE_ACTION.STOP, SERVICE_ACTION.RESTART])
    : new Set([SERVICE_ACTION.START]);
  return { actions, reason: null };
}

export const getDownServices = (services) => services.filter((service) => service.status === SERVICE_STATUS.DOWN);
