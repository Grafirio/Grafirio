import { learnFact } from './dataAnalysisService';

export default async function learnRelationship(connectionId, match, accepted, question = null) {
  const result = await learnFact(connectionId, {
    kind: 'relationship', accepted, question,
    fromTable: match.fromTable,
    fromColumn: match.fromColumn,
    toTable: match.toTable,
    toColumn: match.toColumn,
  });
  if (result?.success !== true || result?.applied !== true) {
    throw new Error(result?.error || 'İlişki aktif sözlüğe uygulanamadı. Tekrar deneyin.');
  }
  return result;
}