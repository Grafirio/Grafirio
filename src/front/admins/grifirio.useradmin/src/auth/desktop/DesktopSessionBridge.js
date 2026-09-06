import {
  DESKTOP_ERROR, DESKTOP_MESSAGE, DesktopSessionError, REQUEST_TIMEOUT_MS,
} from './desktopSessionProtocol.js';

export default class DesktopSessionBridge {
  constructor(webview, onTokens, onSessionExpired) {
    this.webview = webview;
    this.onTokens = onTokens;
    this.onSessionExpired = onSessionExpired;
    this.pendingRequests = new Map();
    this.handleMessage = this.handleMessage.bind(this);
  }

  connect() {
    if (!this.webview?.addEventListener || !this.webview?.postMessage) {
      throw new DesktopSessionError(DESKTOP_ERROR.OFFLINE);
    }
    // Only WebView2's native channel is trusted, never window message events.
    this.webview.addEventListener('message', this.handleMessage);
  }

  send(type) {
    this.post({ type });
  }

  post(message) {
    try {
      this.webview.postMessage(message);
    } catch {
      throw new DesktopSessionError(DESKTOP_ERROR.OFFLINE);
    }
  }

  request(type) {
    const requestId = globalThis.crypto.randomUUID();
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pendingRequests.delete(requestId);
        reject(new DesktopSessionError(DESKTOP_ERROR.TIMEOUT));
      }, REQUEST_TIMEOUT_MS);
      this.pendingRequests.set(requestId, { resolve, reject, timeout });
      try {
        this.post({ type, requestId });
      } catch (error) {
        this.pendingRequests.delete(requestId);
        clearTimeout(timeout);
        reject(error);
      }
    });
  }

  handleMessage({ data }) {
    // postWebMessageAsJson delivers objects; strings and unrelated events are ignored.
    if (!data || typeof data !== 'object' || Array.isArray(data)) return;

    if (data.type === DESKTOP_MESSAGE.SESSION_EXPIRED) {
      for (const request of this.pendingRequests.values()) {
        clearTimeout(request.timeout);
        request.reject(new DesktopSessionError(DESKTOP_ERROR.SIGNED_OUT));
      }
      this.pendingRequests.clear();
      this.onSessionExpired();
      return;
    }

    if (data.type !== DESKTOP_MESSAGE.TOKENS) return;
    if (data.requestId === undefined) {
      this.onTokens(data);
      return;
    }

    const request = this.pendingRequests.get(data.requestId);
    if (!request) return;
    this.pendingRequests.delete(data.requestId);
    clearTimeout(request.timeout);
    request.resolve(data);
  }

}