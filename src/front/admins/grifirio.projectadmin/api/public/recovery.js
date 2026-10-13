(function () {
  const keyInput = document.getElementById('recovery-key');
  const message = document.getElementById('message');
  const dockerState = document.getElementById('docker-state');
  const servicesBody = document.getElementById('services');
  const REFRESH_INTERVAL_MS = 5000;

  function showMessage(text) {
    message.textContent = text;
  }

  async function callRecovery(path, method) {
    const response = await fetch(`/recovery/${path}`, {
      method,
      headers: { 'X-Recovery-Key': keyInput.value },
    });
    const body = await response.json();
    if (!response.ok) throw new Error(body.error || `HTTP ${response.status}`);
    return body;
  }

  function appendCell(row, text, className) {
    const cell = document.createElement('td');
    cell.textContent = text;
    if (className) cell.className = className;
    row.appendChild(cell);
  }

  function renderStatus(status) {
    dockerState.textContent = status.docker.available ? 'Docker: çalışıyor' : 'Docker: kapalı';
    servicesBody.replaceChildren();
    for (const service of status.services) {
      const row = document.createElement('tr');
      appendCell(row, service.name);
      appendCell(row, service.status, service.status);
      appendCell(row, service.latencyMs ?? '-');
      appendCell(row, service.error ?? '');
      servicesBody.appendChild(row);
    }
    const active = status.operation.active;
    if (active) showMessage(`Devam eden işlem: ${active.name}`);
  }

  async function refresh() {
    if (!keyInput.value) return;
    try {
      renderStatus(await callRecovery('status', 'GET'));
    } catch (error) {
      showMessage(error.message);
    }
  }

  async function runAction(path, successText) {
    try {
      await callRecovery(path, 'POST');
      showMessage(successText);
    } catch (error) {
      showMessage(error.message);
    }
  }

  document.getElementById('refresh').addEventListener('click', refresh);
  document.getElementById('start-docker').addEventListener('click',
    () => runAction('docker/start', 'Docker Desktop başlatılıyor; hazır olması bir dakika sürebilir.'));
  document.getElementById('infrastructure-up').addEventListener('click',
    () => runAction('infrastructure/up', 'Altyapı kaldırılıyor; durum tablosundan izleyin.'));

  setInterval(refresh, REFRESH_INTERVAL_MS);
})();
