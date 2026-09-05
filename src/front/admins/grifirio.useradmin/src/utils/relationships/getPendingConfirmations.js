import relationshipKey from './relationshipKey.js';

const RELATIONSHIP_FIELDS = ['fromTable', 'fromColumn', 'toTable', 'toColumn'];

export default function getPendingConfirmations(payload = {}) {
  const proposals = payload?.pendingConfirmations
    ?? payload?.audit?.pendingConfirmations
    ?? payload?.llmParameters?.relationship_proposals
    ?? payload?.audit?.llmParameters?.relationship_proposals;
  if (!Array.isArray(proposals)) return [];

  const unique = new Map();
  for (const match of proposals) {
    if (!match || !RELATIONSHIP_FIELDS.every(field =>
      typeof match[field] === 'string' && match[field].trim())) continue;
    unique.set(relationshipKey(match), match);
  }
  return [...unique.values()];
}