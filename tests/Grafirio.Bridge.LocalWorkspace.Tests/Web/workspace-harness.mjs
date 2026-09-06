import { readFile } from 'node:fs/promises';
import { randomUUID } from 'node:crypto';
import vm from 'node:vm';
import { loadModuleGraph } from './workspace-module-loader.mjs';

const WEB_ROOT = new URL('../../../src/bridge/Grafirio.Bridge.Desktop/LocalWorkspace/Web/', import.meta.url);
const DRAFT_DELAY_MS = 400;
const STARTUP_TIMEOUT_MS = 2_000;

async function withinStartupDeadline(operation, stage) {
  let timer;
  const deadline = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(`Workspace startup timed out during ${stage}`)), STARTUP_TIMEOUT_MS);
  });
  try { return await Promise.race([operation, deadline]); }
  finally { clearTimeout(timer); }
}

class Element {
  constructor(tagName = 'div') {
    this.tagName = tagName;
    this.listeners = new Map();
    this.children = [];
    this.value = '';
    this.disabled = false;
    this.open = false;
    this.textContent = '';
  }
  addEventListener(name, listener) {
    const listeners = this.listeners.get(name) || [];
    listeners.push(listener);
    this.listeners.set(name, listeners);
  }
  dispatch(name) {
    const event = { defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    const results = (this.listeners.get(name) || []).map(listener => listener(event));
    return { event, done: Promise.all(results) };
  }
  click() { return this.disabled ? { done: Promise.resolve() } : this.dispatch('click'); }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  setAttribute() {}
  reportValidity() { return true; }
  reset() {}
  showModal() { this.open = true; }
  close() {
    if (!this.open) return;
    this.open = false;
    queueMicrotask(() => this.dispatch('close'));
  }
  querySelectorAll(selector) {
    const tags = selector.split(',').map(tag => tag.trim());
    return this.children.flatMap(child => [child, ...child.querySelectorAll(selector)])
      .filter(child => tags.includes(child.tagName));
  }
}

export const turn = () => new Promise(resolve => setImmediate(resolve));

export async function createWorkspace() {
  const html = await withinStartupDeadline(readFile(new URL('index.html', WEB_ROOT), 'utf8'), 'HTML read');
  const elements = new Map([...html.matchAll(/<(\w+)[^>]*\bid="([^"]+)"/g)]
    .map(([, tag, id]) => [id, new Element(tag)]));
  const formHtml = html.slice(html.indexOf('<form'), html.indexOf('</form>'));
  elements.get('connection-form').children = [...formHtml.matchAll(/\bid="([^"]+)"/g)]
    .map(([, id]) => elements.get(id)).filter(element => element !== elements.get('connection-form'));
  elements.get('max-rows').value = '100';
  elements.get('timeout').value = '30';
  const sent = [];
  const requests = [];
  const timers = new Map();
  let timerId = 0;
  let receive;
  const context = vm.createContext({
    document: {
      getElementById: id => elements.get(id),
      createElement: tag => new Element(tag)
    },
    window: { chrome: { webview: {
      addEventListener: (_, listener) => { receive = listener; },
      postMessage: message => {
        const copy = structuredClone(message);
        sent.push(copy);
        if (copy.method) requests.push(copy);
      }
    } } },
    crypto: { randomUUID },
    setTimeout: (callback, delay) => {
      timers.set(++timerId, { callback, delay });
      return timerId;
    },
    clearTimeout: id => timers.delete(id)
  });
  const module = await withinStartupDeadline(
    loadModuleGraph(new URL('app.js', WEB_ROOT), context), 'module loading/linking');
  const connection = (id, name = id) => ({ id, name, provider: 'sqlserver', host: 'localhost', database: 'sample', allowedTables: ['dbo.Sales'] });
  const initial = { connections: [connection('old')], selectedConnectionId: 'old', draft: 'SELECT 1' };
  function reply(request, result = null, error = null) {
    receive({ data: error ? { id: request.id, ok: false, error } : { id: request.id, ok: true, result } });
  }
  function take(method) {
    const request = requests.shift();
    if (request?.method !== method) throw new Error(`Expected ${method}, received ${request?.method}`);
    return request;
  }
  await withinStartupDeadline(Promise.all([
    module.evaluate(),
    turn().then(() => reply(take('load'), initial))
  ]), 'module evaluation/load handshake');
  return {
    element: id => elements.get(id), sent, requests, reply, take, connection,
    input(text) { elements.get('sql').value = text; elements.get('sql').dispatch('input'); },
    debounce() {
      for (const [id, timer] of timers) {
        if (timer.delay !== DRAFT_DELAY_MS) continue;
        timers.delete(id);
        timer.callback();
      }
    },
    flush() {
      const id = randomUUID();
      receive({ data: { event: 'workspace-flush', id } });
      return id;
    },
    ack(id) { return sent.find(message => message.event === 'workspace-flushed' && message.id === id); }
  };
}