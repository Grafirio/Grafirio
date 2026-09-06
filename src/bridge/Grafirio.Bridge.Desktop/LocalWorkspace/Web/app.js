import { onFlush, request } from './channel.js';
import { initializeConnectionForm, renderConnections } from './connections.js';
import { renderResult, renderSchema } from './results.js';

const SAVE_DELAY_MS = 400;
const element = id => document.getElementById(id);
let snapshot = { connections: [], selectedConnectionId: null, draft: '' };
let busy = false;
let saveTimer;
let hasResult = false;
let loaded = false;
let draftVersion = 0;
let savedDraftVersion = 0;
let operations = Promise.resolve();
let pendingOperations = 0;
let deactivating = false;
let deleting = false;
let deleteError = null;

function status(message, isError = false) {
  element('status').textContent = message;
  element('status').className = isError ? 'error' : '';
}

function selected() {
  return snapshot.connections.find(connection => connection.id === snapshot.selectedConnectionId);
}

function refreshControls() {
  const connection = selected();
  for (const id of ['edit-connection', 'delete-connection', 'test', 'discover'])
    element(id).disabled = busy || !loaded || !connection;
  element('new-connection').disabled = busy || !loaded;
  element('sql').disabled = !loaded || deactivating;
  element('run').disabled = busy || !connection || connection.provider !== 'sqlserver';
  element('cancel').disabled = !busy;
  element('export').disabled = busy || !hasResult;
  for (const button of element('connections').querySelectorAll('button')) button.disabled = busy;
}

function updateSnapshot(next) {
  if (snapshot.selectedConnectionId !== next.selectedConnectionId) draftVersion++;
  snapshot = next;
  renderConnections(snapshot, selectConnection);
  const connection = selected();
  element('connection-label').textContent = connection ? `${connection.name} / ${connection.host} / ${connection.database}` : 'Henüz bağlantı seçilmedi';
  element('provider-note').textContent = connection && connection.provider !== 'sqlserver'
    ? 'Bu sağlayıcıda bağlantı testi ve şema keşfi kullanılabilir. Güvenli serbest SQL desteği henüz eklenmedi.'
    : 'SQL Server: yalnızca izin verilen fiziksel tablolar; özel salt-okunur kullanıcı ve VIEW DEFINITION gerekir.';
  refreshControls();
}

async function persistDraft() {
  clearTimeout(saveTimer);
  if (!loaded) return;
  // Called only inside an operation: capture selection after preceding connection mutations finish.
  while (savedDraftVersion !== draftVersion) {
    const version = draftVersion;
    element('draft-status').textContent = 'Kaydediliyor…';
    try {
      await request('saveSelection', { connectionId: snapshot.selectedConnectionId, draft: element('sql').value });
      savedDraftVersion = version;
      element('draft-status').textContent = version === draftVersion ? 'Yerel olarak kaydedildi' : 'Kaydedilecek değişiklikler var';
    } catch (error) {
      element('draft-status').textContent = 'Kayıt başarısız';
      throw error;
    }
  }
}

function saveDraft() { return report(perform(persistDraft)); }

function clearResults() {
  hasResult = false;
  element('result').replaceChildren();
  element('result-count').textContent = '';
  element('schema').replaceChildren();
}

async function selectConnection(id) {
  if (busy) return;
  await report(perform(async () => {
    await persistDraft();
    clearResults();
    updateSnapshot({ ...snapshot, selectedConnectionId: id });
    await persistDraft();
    status('Yerel bağlantı seçildi.');
  }));
}

function perform(operation) {
  pendingOperations++;
  busy = true;
  refreshControls();
  const result = operations.then(operation).finally(() => {
    pendingOperations--;
    busy = pendingOperations > 0 || deactivating;
    refreshControls();
  });
  // Each caller owns its error; a failed operation must not poison later retries.
  operations = result.then(() => undefined, () => undefined);
  return result;
}

async function report(operation) {
  try { return await operation; }
  catch (error) { status(error.message, true); }
}

const connectionForm = initializeConnectionForm(next => {
  clearResults();
  updateSnapshot(next);
  status('Bağlantı yalnızca yerel depoya kaydedildi.');
}, persistDraft, perform, () => !busy && loaded);

element('new-connection').addEventListener('click', () => connectionForm.open());
element('edit-connection').addEventListener('click', () => connectionForm.open(selected()));
element('sql').addEventListener('input', () => {
  draftVersion++;
  element('draft-status').textContent = 'Kaydedilecek değişiklikler var';
  clearTimeout(saveTimer);
  saveTimer = setTimeout(saveDraft, SAVE_DELAY_MS);
});
element('sql').addEventListener('blur', saveDraft);
element('test').addEventListener('click', () => report(perform(async () => {
  status('Bağlantı sınanıyor…');
  const result = await request('test', { id: snapshot.selectedConnectionId });
  status(result.message);
})));
element('discover').addEventListener('click', () => report(perform(async () => {
  status('Şema okunuyor…');
  renderSchema(await request('discover', { id: snapshot.selectedConnectionId }));
  status('Şema keşfi tamamlandı. Tablo izinleri otomatik değiştirilmedi.');
})));
element('run').addEventListener('click', () => report(perform(async () => {
  if (!element('max-rows').reportValidity() || !element('timeout').reportValidity()) return;
  await persistDraft();
  hasResult = false;
  element('result').replaceChildren();
  element('result-count').textContent = '';
  status('Salt-okunur sorgu çalışıyor…');
  const result = await request('query', {
    connectionId: snapshot.selectedConnectionId, sql: element('sql').value,
    maxRows: Number(element('max-rows').value), timeoutSeconds: Number(element('timeout').value)
  });
  hasResult = true;
  status(renderResult(result));
})));
element('cancel').addEventListener('click', async () => {
  try { await request('cancel'); status('İptal istendi.'); }
  catch (error) { status(error.message, true); }
});
element('export').addEventListener('click', () => report(perform(async () => {
  const result = await request('export');
  status(result.exported ? 'CSV kaydedildi. Dosya şifrelenmez; güvenli bir konumda saklayın.' : 'Dışa aktarma iptal edildi.');
})));
element('delete-connection').addEventListener('click', () => {
  if (!busy) { deleteError = null; element('delete-dialog').showModal(); }
});
element('cancel-delete').addEventListener('click', () => {
  if (!deleting && !deactivating) { deleteError = null; element('delete-dialog').close(); }
});
element('delete-dialog').addEventListener('cancel', event => {
  if (deleting || deactivating) event.preventDefault();
  else deleteError = null;
});
element('confirm-delete').addEventListener('click', async () => {
  if (deleting || deactivating) return;
  deleting = true;
  deleteError = null;
  element('confirm-delete').disabled = true;
  element('cancel-delete').disabled = true;
  try {
    await perform(async () => {
      await persistDraft();
      const next = await request('deleteConnection', { id: snapshot.selectedConnectionId });
      clearResults();
      updateSnapshot(next);
      element('delete-dialog').close();
      status('Yerel bağlantı silindi.');
    });
  } catch (error) {
    deleteError = error;
    status(error.message, true);
  } finally {
    deleting = false;
    element('confirm-delete').disabled = false;
    element('cancel-delete').disabled = false;
  }
});
element('sql').addEventListener('keydown', event => {
  if (event.ctrlKey && event.key === 'Enter') {
    event.preventDefault();
    if (!element('run').disabled) element('run').click();
  }
});
onFlush(async () => {
  deactivating = true;
  connectionForm.setDeactivating(true);
  clearTimeout(saveTimer);
  try {
    await perform(async () => {
      if (!loaded) throw new Error('Yerel çalışma alanı yüklenemedi; kayıt doğrulanamadı.');
      await persistDraft();
      if (deleteError) throw deleteError;
      connectionForm.closeForDeactivation();
      element('delete-dialog').close();
    });
  } catch (error) {
    status(error.message, true);
    throw error;
  } finally {
    deactivating = false;
    connectionForm.setDeactivating(false);
    busy = pendingOperations > 0;
    refreshControls();
  }
});
refreshControls();
await report(perform(async () => {
  const initial = await request('load');
  loaded = true;
  element('sql').value = initial.draft;
  updateSnapshot(initial);
  savedDraftVersion = draftVersion;
  status('Yerel çalışma alanı hazır.');
}));