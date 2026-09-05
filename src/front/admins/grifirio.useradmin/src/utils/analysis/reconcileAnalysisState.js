const WAITING_STATUSES = ['await_answers', 'awaiting_answers'];

export default function reconcileAnalysisState(previous, state) {
  const status = WAITING_STATUSES.includes(state.status) ? 'awaiting_answers' : state.status;
  const questions = state.questions ?? [];
  const needsAttention = status === 'awaiting_answers' || status === 'failed';
  return {
    ...previous,
    running: status === 'analyzing',
    status,
    trackingAbandoned: false,
    questions,
    answers: Object.fromEntries(questions
      .filter(question => Object.hasOwn(previous.answers, question.id))
      .map(question => [question.id, previous.answers[question.id]])),
    summary: state.summary || '',
    stats: state.tableCount
      ? { tables: state.tableCount, columns: state.columnCount, sampled: state.sampledColumnCount }
      : previous.stats,
    open: needsAttention ? true : previous.open,
    minimized: needsAttention ? false : previous.minimized,
    error: status === 'failed' ? (state.error || state.summary || 'Analiz başarısız oldu.') : '',
  };
}