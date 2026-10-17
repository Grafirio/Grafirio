import { ERRORS } from '../services/diagnosticApi';

// These are signals from the selected query scope, not confirmed root causes.
export function priorityIssues(summary) {
  if (!summary) return [];
  const issues = (summary.repeatedErrors ?? []).map((group) => ({
    key: `error:${group.code}`, title: ERRORS[group.code] ?? group.code,
    count: group.count, reason: 'Tekrarlayan başarısızlık', priority: 1,
    filter: { status: 'failed', errorCode: group.code },
  }));
  for (const row of summary.visibility?.reviewCandidates ?? []) {
    issues.push({ key: row.id, id: row.id, title: row.question || row.id, count: 1,
      priority: row.reasons.includes('failed') ? 1 : 2,
      reason: row.reasons.map((reason) => ({ failed: 'Başarısız', negative_feedback: 'Olumsuz geri bildirim',
        clarification: 'Açıklama gerekiyor', slow: 'Yavaş yanıt' })[reason] ?? reason).join(' · '),
      companyId: row.companyId, connectionId: row.connectionId });
  }
  return issues.sort((a, b) => a.priority - b.priority || b.count - a.count);
}
