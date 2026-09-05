import getPendingConfirmations from './relationships/getPendingConfirmations.js';

export default function getQueryFailure(error) {
  const body = error.response?.data;
  return {
    success: false,
    error: body?.clarificationQuestion || body?.error || body?.detail || body?.title || error.message,
    status: body?.status,
    needsClarification: body?.needsClarification === true || body?.status === 'clarification',
    pendingConfirmations: getPendingConfirmations(body),
    queryId: body?.queryId,
  };
}