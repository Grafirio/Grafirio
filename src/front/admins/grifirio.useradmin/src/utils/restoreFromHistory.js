import { nodeIds } from '../components/Canvas/canvasGraph.js';
import { DEFAULT_CHART_TYPE } from '../components/Canvas/chartTypes.js';
import getPendingConfirmations from './relationships/getPendingConfirmations.js';
import relationshipKey from './relationships/relationshipKey.js';

const RESTORE_BASE_X = 80;
const RESTORE_BASE_Y = 80;
const CARD_GAP = 60;
const CARD_HEIGHT = 420;

export default function restoreFromHistory(queries = [], relationshipDecisions = {}) {
  const rootOf = new Map();
  const cards = new Map();
  for (const item of queries) {
    const root = (item.parentQueryId && rootOf.get(item.parentQueryId)) || item.queryId;
    rootOf.set(item.queryId, root);
    if (!cards.has(root)) {
      cards.set(root, { root, turns: [], chart: null, chartType: null, evidence: null, audit: null });
    }
    const card = cards.get(root);
    const ts = new Date(item.createdAt).getTime();
    const result = item.result ?? {};
    card.turns.push({ role: 'user', content: item.question, ts });
    const proposals = getPendingConfirmations({ ...result, ...item });
    card.pendingConfirmations = proposals.filter(match => typeof relationshipDecisions[relationshipKey(match)] !== 'boolean');
    card.needsRelationshipClarification = item.status === 'clarification' && card.pendingConfirmations.length > 0;
    card.clarificationQuestion = card.needsRelationshipClarification ? item.question : null;
    card.confirmationStates = {};
    card.confirmationRejected = proposals.some(match => relationshipDecisions[relationshipKey(match)] === false);

    if (item.status === 'completed') {
      card.turns.push({ role: 'ai', content: result.summary || result.answer || 'Analiz tamamlandı.', ts });
      const chart = (result.charts || [])[0];
      if (chart) {
        card.chart = chart;
        card.chartType = chart.type || card.chartType;
      }
      card.evidence = result.audit?.evidence ?? null;
      card.audit = { ...(result.audit || {}), llmParameters: item.llmParameters };
    } else if (item.status === 'clarification') {
      card.turns.push({ role: 'ai', ts,
        content: item.clarificationQuestion || result.clarificationQuestion || item.error || result.error
          || 'Bu soruyu çözemedim; biraz daha açık yazar mısınız?',
      });
      card.audit = { ...(result.audit || {}), llmParameters: item.llmParameters };
    } else {
      card.turns.push({ role: 'ai', error: true, ts, content: result.error || 'Bu analiz tamamlanmadı.' });
    }
    card.queryId = item.queryId;
  }

  let y = RESTORE_BASE_Y;
  const nodes = [];
  for (const { root, ...card } of cards.values()) {
    nodes.push({
      id: nodeIds.card(root), type: 'biAnalysisCard', position: { x: RESTORE_BASE_X, y },
      data: { ...card, chartType: card.chartType || DEFAULT_CHART_TYPE },
    });
    y += CARD_HEIGHT + CARD_GAP;
  }
  return { nodes, nextPos: { x: RESTORE_BASE_X, y } };
}