import { request } from './channel.js';

const EMPTY_ID = '00000000-0000-0000-0000-000000000000';
const DEFAULT_PORTS = { sqlserver: 1433, postgres: 5432, mysql: 3306 };
const field = id => document.getElementById(id);

export function renderConnections(snapshot, onSelect) {
  const host = field('connections');
  host.replaceChildren();
  for (const connection of snapshot.connections) {
    const button = document.createElement('button');
    button.className = `connection-card${connection.id === snapshot.selectedConnectionId ? ' selected' : ''}`;
    button.setAttribute('aria-pressed', String(connection.id === snapshot.selectedConnectionId));
    const name = document.createElement('strong');
    name.textContent = connection.name;
    const detail = document.createElement('small');
    detail.textContent = `${connection.provider} · ${connection.database}`;
    button.append(name, detail);
    button.addEventListener('click', () => onSelect(connection.id));
    host.append(button);
  }
  if (!snapshot.connections.length) {
    const empty = document.createElement('p');
    empty.className = 'empty';
    empty.textContent = 'İlk yerel bağlantınızı + ile ekleyin.';
    host.append(empty);
  }
}

export function initializeConnectionForm(onSaved, persistDraft, perform, canOpen) {
  const dialog = field('connection-dialog');
  const form = field('connection-form');
  let saving = false;
  let deactivating = false;
  let generation = 0;
  let saveError = null;
  const close = () => {
    if (saving || deactivating) return;
    generation++;
    saveError = null;
    dialog.close();
  };
  field('close-dialog').addEventListener('click', close);
  field('cancel-dialog').addEventListener('click', close);
  dialog.addEventListener('cancel', event => {
    event.preventDefault();
    close();
  });
  dialog.addEventListener('close', () => {
    if (!dialog.open) { generation++; field('password').value = ''; }
  });
  field('provider').addEventListener('change', () => {
    field('port').value = DEFAULT_PORTS[field('provider').value];
    field('trust-certificate').checked = false;
    field('trust-certificate').disabled = field('provider').value !== 'sqlserver';
  });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (saving || deactivating || !dialog.open || !form.reportValidity()) return;
    saving = true;
    const operationGeneration = generation;
    saveError = null;
    const connection = {
      id: field('connection-id').value || EMPTY_ID,
      name: field('connection-name').value.trim(), provider: field('provider').value,
      host: field('host').value.trim(), port: Number(field('port').value),
      database: field('database').value.trim(), username: field('username').value.trim(),
      password: field('password').value, trustServerCertificate: field('trust-certificate').checked,
      allowedTables: field('allowed-tables').value.split('\n').map(value => value.trim()).filter(Boolean)
    };
    field('password').value = '';
    const controls = [...form.querySelectorAll('input, select, textarea, button')];
    const previousDisabled = controls.map(control => control.disabled);
    controls.forEach(control => { control.disabled = true; });
    try {
      await perform(async () => {
        await persistDraft();
        const snapshot = await request('saveConnection', connection);
        if (generation !== operationGeneration) return;
        onSaved(snapshot);
        dialog.close();
      });
    } catch (error) {
      saveError = error;
      if (generation === operationGeneration)
        field('form-error').textContent = `${error.message} Yeni parola girdiyseniz tekrar yazın.`;
    } finally {
      connection.password = '';
      saving = false;
      controls.forEach((control, index) => { control.disabled = previousDisabled[index]; });
      if (deactivating) setDeactivating(true);
    }
  });
  const open = connection => {
    if (saving || deactivating || !canOpen()) return;
    generation++;
    saveError = null;
    form.reset();
    field('form-error').textContent = '';
    field('dialog-title').textContent = connection ? 'Bağlantıyı düzenle' : 'Bağlantı ekle';
    field('connection-id').value = connection?.id || '';
    field('connection-name').value = connection?.name || '';
    field('provider').value = connection?.provider || 'sqlserver';
    field('host').value = connection?.host || '';
    field('port').value = connection?.port || DEFAULT_PORTS.sqlserver;
    field('database').value = connection?.database || '';
    field('username').value = connection?.username || '';
    field('password').required = !connection;
    field('password').placeholder = connection?.hasPassword ? 'Kayıtlı · değiştirmek için yazın' : '';
    field('trust-certificate').checked = connection?.trustServerCertificate || false;
    field('trust-certificate').disabled = field('provider').value !== 'sqlserver';
    field('allowed-tables').value = connection?.allowedTables.join('\n') || '';
    dialog.showModal();
  };
  function setDeactivating(value) {
    deactivating = value;
    for (const id of ['save-connection', 'close-dialog', 'cancel-dialog'])
      field(id).disabled = saving || deactivating;
  }
  return {
    open,
    setDeactivating,
    closeForDeactivation() {
      if (saving) throw new Error('Bağlantı kaydı henüz tamamlanmadı.');
      if (saveError) throw saveError;
      generation++;
      field('password').value = '';
      dialog.close();
    }
  };
}