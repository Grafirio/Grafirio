import getPendingConfirmations from './relationships/getPendingConfirmations.js';

export default function resolveAgentResponse(payload, queryId) {
  const result = payload?.result ?? payload ?? {};
  const needsClarification = payload?.status === 'clarification' || result.status === 'clarification'
    || payload?.needsClarification === true || result.needsClarification === true;
  const identity = payload?.queryId ?? result.queryId ?? queryId;
  const pendingConfirmations = getPendingConfirmations({ ...result, ...payload });
  if (needsClarification || payload?.status === 'failed' || payload?.success === false) {
    return {
      success: false,
      needsClarification,
      queryId: identity,
      pendingConfirmations,
      error: payload?.clarificationQuestion || result.clarificationQuestion
        || payload?.error || result.error || payload?.message
        || (needsClarification ? 'Analize devam etmek için soruyu netleştirin.' : 'Analiz başarısız oldu.'),
    };
  }
  if (payload?.status !== 'completed' && payload?.result == null) return null;
  return {
    success: true,
    queryId: identity,
    answer: result.summary || result.answer || 'Analiz tamamlandı.',
    charts: result.charts || [],
    pendingConfirmations,
    audit: { ...(result.audit || {}), llmParameters: payload?.llmParameters },
  };
}