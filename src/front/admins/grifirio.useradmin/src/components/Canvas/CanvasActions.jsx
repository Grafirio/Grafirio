import React from 'react';
import { IconFileTypePdf, IconAdjustmentsHorizontal } from '@tabler/icons-react';
import { presentationAvailability } from '../../utils/analysisPresentation';

export default function CanvasActions({ nodes, hasConnection, onMetricRules, onPresentation }) {
  const availability = presentationAvailability(nodes);
  return <div className="cp-actions">
    <button className="gf-btn gf-btn--sm cp-action" disabled={!hasConnection}
      title={hasConnection ? 'Ölçüleri ve tahmin sınırlarını düzenle' : 'Bağlantı bilgisi yüklenince açılır'} onClick={onMetricRules}>
      <IconAdjustmentsHorizontal size={16} stroke={1.7} /> Metrik kuralları
    </button>
    <button className="gf-btn gf-btn--primary gf-btn--sm cp-action" title={availability.message} onClick={onPresentation}>
      <IconFileTypePdf size={17} stroke={1.7} /> PDF sunusu
      {availability.ready > 0 && <span className="cp-action-count" aria-label={`${availability.ready} analiz hazır`}>{availability.ready}</span>}
    </button>
  </div>;
}
