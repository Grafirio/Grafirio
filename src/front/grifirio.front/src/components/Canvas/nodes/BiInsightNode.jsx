import React from 'react';

const TYPE_CONFIG = {
  success:  { icon: '✅', color: '#10b981', bg: '#ecfdf5', border: '#a7f3d0' },
  warning:  { icon: '⚠️',  color: '#f59e0b', bg: '#fffbeb', border: '#fde68a' },
  error:    { icon: '❌', color: '#ef4444', bg: '#fef2f2', border: '#fecaca' },
  info:     { icon: 'ℹ️',  color: '#3b82f6', bg: '#eff6ff', border: '#bfdbfe' },
  question: { icon: '💬', color: '#7c3aed', bg: '#f5f3ff', border: '#ddd6fe' },
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
