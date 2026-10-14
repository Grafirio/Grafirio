import React from 'react';
import { IconInfoCircle } from '@tabler/icons-react';

export default function AnalysisNotices({ notices = [] }) {
  return notices.map((notice, i) => <div className="analysis-notice" role="note" key={i}><IconInfoCircle size={19} stroke={1.7} /><div><strong>Tahmin süresi hakkında</strong><p>{notice}</p></div></div>);
}
