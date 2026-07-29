import React from 'react';

const TYPE_CONFIG = {
  success:  { icon: '✅', color: 'var(--success)', bg: 'var(--success-soft)', border: 'var(--success)' },
  warning:  { icon: '⚠️',  color: 'var(--warning)', bg: 'var(--warning-soft)', border: 'var(--warning)' },
  error:    { icon: '❌', color: 'var(--danger)',  bg: 'var(--danger-soft)',  border: 'var(--danger)' },
  info:     { icon: 'ℹ️',  color: 'var(--text-muted)', bg: 'var(--accent-soft)', border: 'var(--border-strong)' },
  question: { icon: '💬', color: 'var(--accent)', bg: 'var(--accent-soft)', border: 'var(--border-strong)' },
};

export default function BiInsightNode({ data }) {
  const config = TYPE_CONFIG[data?.type] || TYPE_CONFIG.info;

  return (
    <div
      className="bi-node bi-insight-node"
      style={{ borderColor: config.border, background: config.bg }}
    >
      <div className="bi-node-header" style={{ borderBottomColor: config.border }}>
        <span className="bi-node-icon">{config.icon}</span>
        <span className="bi-node-title" style={{ color: config.color }}>
          {data?.title || 'Analiz'}
        </span>
        <span className="bi-node-type-badge" style={{ background: config.color + '20', color: config.color }}>
          Insight
        </span>
      </div>
      <div className="bi-insight-body">
        <p>{data?.description || ''}</p>
      </div>
    </div>
  );
}
