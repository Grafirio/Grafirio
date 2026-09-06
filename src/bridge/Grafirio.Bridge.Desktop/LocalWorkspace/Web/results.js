const MAX_VISIBLE_ROWS = 500;

export function renderResult(result) {
  const host = document.getElementById('result');
  const table = document.createElement('table');
  const heading = table.createTHead().insertRow();
  for (const column of result.columns) {
    const cell = document.createElement('th');
    cell.scope = 'col';
    cell.textContent = column;
    heading.append(cell);
  }
  const body = table.createTBody();
  for (const row of result.rows.slice(0, MAX_VISIBLE_ROWS)) {
    const target = body.insertRow();
    for (const value of row) {
      const cell = target.insertCell();
      cell.textContent = value ?? 'NULL';
      if (value === null) cell.className = 'null';
    }
  }
  host.replaceChildren(table);
  document.getElementById('result-count').textContent = `${result.rows.length} satır · ${result.elapsedMilliseconds} ms`;
  const notices = [];
  if (result.truncated) notices.push('Sonuç satır/boyut sınırında kesildi.');
  if (result.rows.length > MAX_VISIBLE_ROWS) notices.push(`Ekranda ilk ${MAX_VISIBLE_ROWS} satır; CSV tüm alınan satırları içerir.`);
  return notices.join(' ') || 'Sorgu tamamlandı.';
}

export function renderSchema(result) {
  const host = document.getElementById('schema');
  const groups = new Map();
  for (const row of result.rows) {
    const key = `${row[0]}.${row[1]}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(`${row[2]} · ${row[3]}${row[4] === 'YES' ? ' · null' : ''}`);
  }
  host.replaceChildren();
  for (const [name, columns] of groups) {
    const details = document.createElement('details');
    const summary = document.createElement('summary');
    summary.textContent = name;
    const list = document.createElement('ul');
    for (const column of columns) {
      const item = document.createElement('li');
      item.textContent = column;
      list.append(item);
    }
    details.append(summary, list);
    host.append(details);
  }
  if (!groups.size || result.truncated) {
    const notice = document.createElement('p');
    notice.className = 'schema-notice';
    notice.textContent = result.truncated ? 'Keşif sınırına ulaşıldı; liste eksik olabilir.' : 'Görünür tablo bulunamadı.';
    host.append(notice);
  }
}