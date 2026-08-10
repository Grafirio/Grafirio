import React from 'react';
import NodeComposer from './NodeComposer';

const TYPE_CONFIG = {
  success:  { icon: '✅', color: 'var(--success)', bg: 'var(--success-soft)', border: 'var(--success)' },
  warning:  { icon: '⚠️',  color: 'var(--warning)', bg: 'var(--warning-soft)', border: 'var(--warning)' },
  error:    { icon: '❌', color: 'var(--danger)',  bg: 'var(--danger-soft)',  border: 'var(--danger)' },
  info:     { icon: 'ℹ️',  color: 'var(--text-muted)', bg: 'var(--accent-soft)', border: 'var(--border-strong)' },
  question: { icon: '💬', color: 'var(--accent)', bg: 'var(--accent-soft)', border: 'var(--border-strong)' },
};

export default function BiInsightNode({ data, onAsk, onDelete }) {
  const config = TYPE_CONFIG[data?.type] || TYPE_CONFIG.info;

  // Soru düğümü sohbetin tuval üzerindeki ucu: buradan sorulan her soru
  // bu dalın devamı olarak çiziliyor. Diğer düğümlerde kutu yok — bir
  // yorumun ya da uyarının "devamını sormak" tanımsız.
  const canAsk = data?.type === 'question' && typeof onAsk === 'function';

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
        {onDelete && (
          <button
            type="button"
            className="bi-node-action bi-node-action--danger"
            title={data?.type === 'question' ? 'Bu soruyu ve sonuçlarını sil' : 'Bu düğümü sil'}
            onMouseDown={(e) => e.stopPropagation()}
            onClick={(e) => { e.stopPropagation(); onDelete(); }}
          >
            🗑
          </button>
        )}
        <span className="bi-node-type-badge" style={{ background: config.color + '20', color: config.color }}>
          Insight
        </span>
      </div>

      <div className="bi-insight-body">
        <p>{data?.description || ''}</p>
      </div>

      {canAsk && (
        <div className="bi-node-footer">
          <NodeComposer
            placeholder="Bu sorunun üzerinden devam et…"
            busy={Boolean(data?.busy)}
            onSubmit={onAsk}
          />
        </div>
      )}
    </div>
  );
}
