import { submitAgentQuery, getAgentQueryStatus, getAgentQueryResult } from './dataAnalysisService.js';
import getQueryFailure from '../utils/getQueryFailure.js';
import resolveAgentResponse from '../utils/resolveAgentResponse.js';

const AGENT_POLL_MS = 3000;
const AGENT_MAX_ATTEMPTS = 100;

export default async function askViaAgent(question, { connectionId, parentQueryId = null, onProgress }) {
  if (!connectionId) return { success: false, error: 'Bu kanvas bir bağlantıya bağlı değil.' };
  let queryId;
  try {
    const submitted = await submitAgentQuery(connectionId, question, parentQueryId);
    queryId = submitted?.queryId;
    const immediate = resolveAgentResponse(submitted, queryId);
    if (immediate) return immediate;
    if (!submitted?.success || !queryId) return { success: false, error: submitted?.error || 'Sorgu gönderilemedi.' };
    onProgress?.('Sorgu kuyruğa alındı…');

    for (let attempt = 0; attempt < AGENT_MAX_ATTEMPTS; attempt++) {
      await new Promise(resolve => setTimeout(resolve, AGENT_POLL_MS));
      const status = await getAgentQueryStatus(queryId);
      if (status.status === 'completed' && !status.needsClarification) {
        const payload = await getAgentQueryResult(queryId);
        return resolveAgentResponse({ ...payload, status: payload.status ?? 'completed' }, queryId);
      }
      const response = resolveAgentResponse(status, queryId);
      if (response) return response;
      onProgress?.('Analiz ediliyor…');
    }
    return { success: false, queryId, error: 'Zaman aşımı — analiz 5 dakikada tamamlanmadı.' };
  } catch (error) {
    const failure = getQueryFailure(error);
    return { ...failure, queryId: failure.queryId ?? queryId };
  }
}