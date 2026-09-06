import assert from 'node:assert/strict';
import nodeTest from 'node:test';
import vm from 'node:vm';
import { createWorkspace, turn } from './workspace-harness.mjs';
import { loadModuleGraph } from './workspace-module-loader.mjs';

const TEST_TIMEOUT_MS = 10_000;
const test = (name, run) => nodeTest(name, { timeout: TEST_TIMEOUT_MS }, run);

test('module loader shares a pending diamond dependency regardless of read completion order', async () => {
  for (const delayed of ['channel.js', 'connections.js']) {
    const sources = new Map([
      ['app.js', `import { channel } from './channel.js';
        import { connectionChannel } from './connections.js';
        export const sameChannel = channel === connectionChannel;`],
      ['channel.js', 'export const channel = {};'],
      ['connections.js', `export { channel as connectionChannel } from './channel.js';`]
    ]);
    const reads = new Map();
    let releaseRead;
    let notifyRead;
    const readStarted = new Promise(resolve => { notifyRead = resolve; });
    const readGate = new Promise(resolve => { releaseRead = resolve; });
    const loading = loadModuleGraph(new URL('file:///workspace/app.js'), vm.createContext({}), async url => {
      const name = url.pathname.split('/').at(-1);
      reads.set(name, (reads.get(name) || 0) + 1);
      if (name === delayed) {
        notifyRead();
        await readGate;
      }
      assert.ok(sources.has(name), `Unexpected module: ${name}`);
      return sources.get(name);
    });
    try {
      await readStarted;
      await turn();
      assert.equal(reads.get(delayed), 1, `Duplicate pending read: ${delayed}`);
    } finally {
      releaseRead();
    }
    const entry = await loading;
    await entry.evaluate();
    assert.equal(entry.namespace.sameChannel, true, `Duplicate channel instance when delaying ${delayed}`);
    for (const name of sources.keys()) assert.equal(reads.get(name), 1, name);
  }
});

test('module loader lets the VM link circular dependencies without waiting on itself', async () => {
  const sources = new Map([
    ['/workspace/app.js', `import { readValue } from './dependency.js';
      export const value = 'linked';
      export { readValue };`],
    ['/workspace/dependency.js', `import { value } from './app.js';
      export function readValue() { return value; }`]
  ]);
  const entry = await loadModuleGraph(new URL('file:///workspace/app.js'), vm.createContext({}), async url => {
    assert.ok(sources.has(url.pathname));
    return sources.get(url.pathname);
  });
  await entry.evaluate();
  assert.equal(entry.namespace.readValue(), 'linked');
});

test('deactivation awaits final debounce draft, then closes dialogs and acknowledges', async () => {
  const app = await createWorkspace();
  app.element('new-connection').click();
  app.input('SELECT latest');
  const id = app.flush();
  await turn();
  const save = app.take('saveSelection');
  assert.equal(save.payload.draft, 'SELECT latest');
  assert.equal(app.ack(id), undefined);
  assert.equal(app.element('connection-dialog').open, true);
  app.reply(save);
  await turn();
  assert.equal(app.ack(id).ok, true);
  assert.equal(app.element('connection-dialog').open, false);
  app.debounce();
  await turn();
  assert.equal(app.requests.length, 0);
});

test('flush waits for the latest edit made during an in-flight draft save', async () => {
  const app = await createWorkspace();
  app.input('SELECT first');
  app.debounce();
  await turn();
  const first = app.take('saveSelection');
  app.input('SELECT final');
  const id = app.flush();
  app.reply(first);
  await turn();
  const latest = app.take('saveSelection');
  assert.equal(latest.payload.draft, 'SELECT final');
  assert.equal(app.ack(id), undefined);
  app.reply(latest);
  await turn();
  assert.equal(app.ack(id).ok, true);
});

test('connection save blocks all close paths and stale draft selection writes', async () => {
  const app = await createWorkspace();
  app.element('new-connection').click();
  app.element('connection-name').value = 'New';
  const submit = app.element('connection-form').dispatch('submit');
  await turn();
  const save = app.take('saveConnection');
  for (const id of ['save-connection', 'close-dialog', 'cancel-dialog', 'connection-name'])
    assert.equal(app.element(id).disabled, true, id);
  app.element('close-dialog').click();
  app.element('cancel-dialog').click();
  const cancel = app.element('connection-dialog').dispatch('cancel');
  assert.equal(cancel.event.defaultPrevented, true);
  assert.equal(app.element('connection-dialog').open, true);
  app.element('connection-form').dispatch('submit');
  app.element('new-connection').click();
  assert.equal(app.element('connection-name').value, 'New');
  app.input('SELECT whileSaving');
  app.debounce();
  app.element('sql').dispatch('blur');
  await turn();
  assert.equal(app.requests.length, 0);
  const next = app.connection('new');
  app.reply(save, { connections: [app.connection('old'), next], selectedConnectionId: 'new', draft: 'SELECT 1' });
  await turn();
  const draft = app.take('saveSelection');
  assert.equal(draft.payload.connectionId, 'new');
  assert.equal(draft.payload.draft, 'SELECT whileSaving');
  app.reply(draft);
  await submit.done;
  await turn();
  assert.equal(app.element('connection-dialog').open, false);
  assert.equal(app.requests.length, 0);
  assert.equal(app.element('save-connection').disabled, false);
});

test('flush during connection save waits for successful save and new selection', async () => {
  const app = await createWorkspace();
  app.element('new-connection').click();
  app.element('connection-form').dispatch('submit');
  await turn();
  const save = app.take('saveConnection');
  const id = app.flush();
  assert.equal(app.ack(id), undefined);
  app.reply(save, { connections: [app.connection('new')], selectedConnectionId: 'new', draft: 'SELECT 1' });
  await turn();
  const draft = app.take('saveSelection');
  assert.equal(draft.payload.connectionId, 'new');
  app.reply(draft);
  await turn();
  assert.equal(app.ack(id).ok, true);
});

test('connection save error fails deactivation without discarding the dialog; retry succeeds', async () => {
  const app = await createWorkspace();
  app.element('new-connection').click();
  app.element('connection-form').dispatch('submit');
  await turn();
  const save = app.take('saveConnection');
  const id = app.flush();
  app.reply(save, null, 'Disk full');
  await turn();
  assert.equal(app.ack(id).ok, false);
  assert.equal(app.element('connection-dialog').open, true);
  assert.match(app.element('form-error').textContent, /Disk full/);
  app.element('connection-form').dispatch('submit');
  await turn();
  app.reply(app.take('saveConnection'), { connections: [app.connection('new')], selectedConnectionId: 'new', draft: 'SELECT 1' });
  await turn();
  const retry = app.flush();
  await turn();
  app.reply(app.take('saveSelection'));
  await turn();
  assert.equal(app.ack(retry).ok, true);
});

test('failed draft flush is reported and retry persists the retained text', async () => {
  const app = await createWorkspace();
  app.input('SELECT retained');
  const id = app.flush();
  await turn();
  app.reply(app.take('saveSelection'), null, 'Write failed');
  await turn();
  assert.equal(app.ack(id).ok, false);
  assert.equal(app.element('sql').value, 'SELECT retained');
  assert.match(app.element('status').textContent, /Write failed/);
  const retry = app.flush();
  await turn();
  const save = app.take('saveSelection');
  assert.equal(save.payload.draft, 'SELECT retained');
  app.reply(save);
  await turn();
  assert.equal(app.ack(retry).ok, true);
});

test('cancel bypasses queued query and flush still awaits pending work', async () => {
  const app = await createWorkspace();
  app.element('run').click();
  await turn();
  const query = app.take('query');
  app.input('SELECT afterCancel');
  const id = app.flush();
  app.element('cancel').click();
  await turn();
  app.reply(app.take('cancel'));
  app.reply(query, null, 'Canceled');
  await turn();
  const save = app.take('saveSelection');
  assert.equal(app.ack(id), undefined);
  app.reply(save);
  await turn();
  assert.equal(app.ack(id).ok, true);
});

test('delete close and Escape are guarded and flush persists the cleared selection', async () => {
  const app = await createWorkspace();
  app.element('delete-connection').click();
  app.element('confirm-delete').click();
  await turn();
  const deletion = app.take('deleteConnection');
  app.element('cancel-delete').click();
  assert.equal(app.element('delete-dialog').dispatch('cancel').event.defaultPrevented, true);
  assert.equal(app.element('delete-dialog').open, true);
  const id = app.flush();
  app.reply(deletion, { connections: [], selectedConnectionId: null, draft: 'SELECT 1' });
  await turn();
  const save = app.take('saveSelection');
  assert.equal(save.payload.connectionId, null);
  app.reply(save);
  await turn();
  assert.equal(app.ack(id).ok, true);
  assert.equal(app.element('delete-dialog').open, false);
});

test('new form submissions are blocked while native flush awaits persistence', async () => {
  const app = await createWorkspace();
  app.element('new-connection').click();
  app.input('SELECT pending');
  const id = app.flush();
  await turn();
  const save = app.take('saveSelection');
  app.element('connection-form').dispatch('submit');
  app.element('cancel-dialog').click();
  assert.equal(app.element('connection-dialog').dispatch('cancel').event.defaultPrevented, true);
  assert.equal(app.requests.length, 0);
  assert.equal(app.element('connection-dialog').open, true);
  app.reply(save);
  await turn();
  assert.equal(app.ack(id).ok, true);
  assert.equal(app.requests.length, 0);
});

test('delete persistence errors prevent successful flush until explicitly dismissed', async () => {
  const app = await createWorkspace();
  app.element('delete-connection').click();
  app.element('confirm-delete').click();
  await turn();
  const deletion = app.take('deleteConnection');
  const id = app.flush();
  app.reply(deletion, null, 'Delete write failed');
  await turn();
  assert.equal(app.ack(id).ok, false);
  assert.equal(app.element('delete-dialog').open, true);
  app.element('cancel-delete').click();
  const retry = app.flush();
  await turn();
  assert.equal(app.ack(retry).ok, true);
});