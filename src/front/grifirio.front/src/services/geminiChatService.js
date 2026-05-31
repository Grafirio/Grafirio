/**
 * Gemini Chat Service
 * Pipeline: Frontend → C# DataAnalysis API → MassTransit → RabbitMQ
 *           → Django AI Consumer → Celery Task → Gemini
 *           → C# QueryResultStore → Frontend poll
 */
const CSHARP_BASE =
  import.meta.env.VITE_API_URL
    ? `${import.meta.env.VITE_API_URL}/data-analysis`
    : '/data-analysis';

const POLL_INTERVAL_MS = 2000;
const POLL_MAX_ATTEMPTS = 45; // ~90 saniye (BiChartNode visual timeout ile eşleşir)

/**
 * Soruyu MassTransit pipeline'ına gönder ve yanıtı bekle.
 * @param {string} question
 * @param {Array<{role:'user'|'model', content:string}>} history
 * @param {{tableName?: string, predictData?: Object, database?: string, tables?: string[]}} options
 * @returns {Promise<{success:boolean, answer?:string, error?:string}>}
 */
export const sendChatMessage = async (question, history = [], options = {}) => {
  const requestId = crypto.randomUUID();

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
        type:    result?.type   ?? 'text',      // 'text' | 'chart'
        answer:  result?.answer ?? 'Yanıt alındı.',
        charts:  result?.charts ?? [],
      };
    }

    if (data.status === 'failed' || data.status === 'error') {
      let result = data.result;
      if (typeof result === 'string') {
        try { result = JSON.parse(result); } catch { result = {}; }
      }
      return { success: false, error: result?.answer ?? 'İşlem başarısız.' };
    }

    // 'processing' veya 'not_found' → beklemeye devam
  }

  return { success: false, error: 'Zaman aşımı — AI servisi 4 dakika içinde yanıt vermedi. Lütfen tekrar deneyin.' };
};
