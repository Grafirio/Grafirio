import relationshipKey from './relationshipKey.js';

// Keep failed proposals pending; saving a fact alone is not proof of active application.
export default async function applyRelationshipDecisions({
  pending, matches, accepted, save, onState, onApplied,
}) {
  let remaining = [...pending];
  const selected = new Set(matches.map(relationshipKey));
  let hasFailure = false;
  for (const match of pending.filter(item => selected.has(relationshipKey(item)))) {
    const key = relationshipKey(match);
    onState(key, { busy: true, error: null });
    try {
      const result = await save(match, accepted);
      if (result?.success !== true || result?.applied !== true) {
        throw new Error(result?.error || 'İlişki aktif sözlüğe uygulanamadı. Tekrar deneyin.');
      }
    } catch (error) {
      hasFailure = true;
      onState(key, { busy: false, error: error.response?.data?.error || error.message });
      continue;
    }
    remaining = remaining.filter(item => relationshipKey(item) !== key);
    onState(key, { busy: false, error: null });
    onApplied(match, remaining);
  }
  return { pending: remaining, hasFailure, allApproved: accepted && !hasFailure && remaining.length === 0 };
}