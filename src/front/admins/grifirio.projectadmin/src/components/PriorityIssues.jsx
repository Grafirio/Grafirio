import { priorityIssues } from '../utils/priorityIssues';

export default function PriorityIssues({ summary, onFilter, onOpen }) {
  const issues = priorityIssues(summary);
  return <section className="gf-card dg-detail" aria-labelledby="priority-title">
    <div className="dg-toolbar"><h2 id="priority-title">Öncelikli Sorunlar</h2>
      <span className="gf-badge">{summary.visibility?.reviewCandidateCount ?? '—'} inceleme adayı</span></div>
    <p className="gf-muted gf-text-sm">Seçili filtrelere göre tekrar eden hatalar ve açık inceleme adayları. Gruplar ile adaylar örtüşebilir; sayıları toplanmaz. Bunlar doğrulanmış kök nedenler değildir.</p>
    <div className="pa-issue-list">{issues.slice(0, 5).map((issue) => <article className="pa-issue" key={issue.key}>
      <span className={`gf-badge gf-badge--${issue.priority === 1 ? 'danger' : 'warning'}`}>{issue.priority === 1 ? 'Öncelikli' : 'İncelenmeli'}</span>
      <div><strong>{issue.title}</strong><p className="gf-muted gf-text-sm">{issue.reason} · {issue.count} kayıt</p>
        {issue.companyId && <p className="gf-muted gf-text-sm">Firma: {issue.companyId} · Bağlantı: {issue.connectionId || '—'}</p>}</div>
      <button className="gf-btn gf-btn--sm" onClick={() => issue.id ? onOpen(issue.id) : onFilter(issue.filter)}>İncele</button>
    </article>)}</div>
    {!issues.length && <p className="gf-muted">Bu kapsamda öncelikli sorun sinyali yok.</p>}
    {issues.length > 5 && <p className="gf-muted gf-text-sm">İlk 5 sinyal gösteriliyor. Diğer adaylar ve kırılımlar Analiz sekmesinde.</p>}
  </section>;
}
