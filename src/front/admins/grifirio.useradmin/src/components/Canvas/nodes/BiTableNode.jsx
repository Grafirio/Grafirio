import React from 'react';

export default function BiTableNode({ data }) {
  const rows = data?.rows || [];
  const columns = data?.columns || (rows[0] ? Object.keys(rows[0]) : []);

  return (
    <div className="bi-node bi-table-node">
      <div className="bi-node-header">
        <span className="bi-node-icon">🗂️</span>
        <span className="bi-node-title">{data?.title || 'Tablo'}</span>
        <span className="bi-node-type-badge">Table</span>
      </div>
      <div className="bi-table-body">
        {rows.length === 0 ? (
          <div className="bi-table-empty">Veri yok</div>
        ) : (
          <div className="bi-table-scroll">
            <table className="bi-table">
              <thead>
                <tr>
                  {columns.map(col => (
                    <th key={col}>{col}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.slice(0, 10).map((row, i) => (
                  <tr key={i}>
                    {columns.map(col => (
                      <td key={col}>{row[col] ?? '—'}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
            {rows.length > 10 && (
              <div className="bi-table-more">+{rows.length - 10} daha fazla satır</div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
