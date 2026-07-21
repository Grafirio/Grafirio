/**
 * AI Chat Service
 * Pipeline: Frontend → C# DataAnalysis API → MassTransit → RabbitMQ
 *           → Django AI (Planner → Executor → Composer) → LLM
 *           → C# QueryResultStore (Redis) → Frontend poll
 */
const CSHARP_BASE =
  import.meta.env.VITE_API_URL
    ? `${import.meta.env.VITE_API_URL}/data-analysis`
    : '/data-analysis';

const POLL_INTERVAL_MS = 2000;
const POLL_MAX_ATTEMPTS = 90; // ~3 dakika — çok görevli planlar 90 saniyeyi aşabilir

/**
 * Soruyu MassTransit pipeline'ına gönder ve yanıtı bekle.
 * @param {string} question
 * @param {Array<{role:'user'|'model', content:string}>} history
 * @param {{tableName?: string, predictData?: Object, database?: string, tables?: string[],
 *          onProgress?: (progress:number, message:string) => void}} options
 * @returns {Promise<{success:boolean, type?:string, answer?:string, charts?:Array,
 *                    failedTasks?:Array<{title:string, reason:string}>, error?:string}>}
 */
export const sendChatMessage = async (question, history = [], options = {}) => {
  const requestId = crypto.randomUUID();
  const { onProgress } = options;

  // 1. C# API'ye gönder → MassTransit publish tetiklenir
  const sendRes = await fetch(`${CSHARP_BASE}/api/ai/reports/ask-question`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      requestId,
      question,
      database: options.database || '',
      tables:   options.tables   || [],
      history,          // List<ChatHistoryItem>? — C# bunu MassTransit context'ine ekler
      tableName: options.tableName || null,
      predictData: options.predictData || null,
    }),
  });

  if (!sendRes.ok) {
    const err = await sendRes.json().catch(() => ({}));
    return { success: false, error: err.message || `HTTP ${sendRes.status}` };
  }

  // 2. Sonuç hazır olana kadar polling yap
  for (let attempt = 0; attempt < POLL_MAX_ATTEMPTS; attempt++) {
    await new Promise(r => setTimeout(r, POLL_INTERVAL_MS));

    const pollRes = await fetch(`${CSHARP_BASE}/api/ai/reports/status/${requestId}`);
    if (!pollRes.ok) continue;

    const data = await pollRes.json();

    if (data.status === 'completed') {
      const result = typeof data.result === 'string' ? JSON.parse(data.result) : data.result;
      return {
        success: result?.success ?? true,
        type:    result?.type   ?? 'text',      // 'text' | 'chart' | 'composite'
        answer:  result?.answer ?? 'Yanıt alındı.',
        charts:  result?.charts ?? [],
        failedTasks: result?.failedTasks ?? [],
      };
    }

    if (data.status === 'failed' || data.status === 'error') {
      let result = data.result;
      if (typeof result === 'string') {
        try { result = JSON.parse(result); } catch { result = {}; }
      }
      return { success: false, error: result?.answer ?? 'İşlem başarısız.' };
    }

    // 'processing' → ilerleme bildir, beklemeye devam
    if (data.status === 'processing' && onProgress) {
      onProgress(data.progress ?? 0, data.progressMessage ?? '');
    }
  }

  return { success: false, error: 'Zaman aşımı — AI servisi 3 dakika içinde yanıt vermedi. Lütfen tekrar deneyin.' };
};
