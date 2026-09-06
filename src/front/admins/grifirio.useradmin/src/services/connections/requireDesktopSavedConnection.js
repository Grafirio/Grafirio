import isDesktopApp from '../../auth/desktop/isDesktopApp.js';
import DesktopConnectionError from './DesktopConnectionError.js';
import requestDesktopConnectionContext from './requestDesktopConnectionContext.js';

export default async function requireDesktopSavedConnection(connectionId, readSummary) {
  if (!isDesktopApp()) return;
  const { bridgeId } = await requestDesktopConnectionContext();
  let connection;
  try {
    connection = await readSummary(connectionId);
  } catch {
    throw new DesktopConnectionError();
  }
  // Missing routing metadata must not enable legacy company-wide routing.
  if (connection?.routeAvailable === false || connection?.connectionMode !== 'bridge' || typeof connection.bridgeId !== 'string') {
    throw new DesktopConnectionError();
  }
  if (connection.bridgeId.toLowerCase() !== bridgeId.toLowerCase()) {
    throw new DesktopConnectionError('differentBridge');
  }
}