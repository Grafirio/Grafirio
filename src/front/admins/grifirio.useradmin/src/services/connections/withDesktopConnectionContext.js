import isDesktopApp from '../../auth/desktop/isDesktopApp.js';
import requestDesktopConnectionContext from './requestDesktopConnectionContext.js';

export default async function withDesktopConnectionContext(payload) {
  if (!isDesktopApp()) return payload;
  const { bridgeId } = await requestDesktopConnectionContext();
  return { ...payload, connectionMode: 'bridge', bridgeId };
}