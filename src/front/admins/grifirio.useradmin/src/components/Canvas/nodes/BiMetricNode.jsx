import React from 'react';

const TREND_ICONS = { up: '↑', down: '↓', neutral: '→' };
const TREND_COLORS = { up: 'var(--success)', down: 'var(--danger)', neutral: 'var(--text-muted)' };

export default function BiMetricNode({ data }) {
  const trend = data?.trend || 'neutral';
  const trendColor = TREND_COLORS[trend];

  return (
    <div className="bi-node bi-metric-node">
      <div className="bi-node-header">
        <span className="bi-node-icon">📈</span>
        <span className="bi-node-title">{data?.label || 'Metrik'}</span>
        <span className="bi-node-type-badge">KPI</span>
      </div>
      <div className="bi-metric-body">
        <div className="bi-metric-value">{data?.value ?? '—'}</div>
        {data?.unit && <div className="bi-metric-unit">{data.unit}</div>}
        {data?.change !== undefined && (
          <div className="bi-metric-change" style={{ color: trendColor }}>
            {TREND_ICONS[trend]} {data.change}
            {data?.changePeriod && <span className="bi-metric-period"> {data.changePeriod}</span>}
          </div>
        )}
        {data?.description && (
          <div className="bi-metric-desc">{data.description}</div>
        )}
      </div>
    </div>
  );
}
