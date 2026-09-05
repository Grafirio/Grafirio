import { useCallback, useRef } from 'react';
import learnRelationship from '../services/learnRelationship.js';
import getPendingConfirmations from '../utils/relationships/getPendingConfirmations.js';
import parseRelationshipDecision from '../utils/relationships/parseRelationshipDecision.js';
import applyRelationshipDecisions from '../utils/relationships/applyRelationshipDecisions.js';
import getRelationshipResume from '../utils/relationships/getRelationshipResume.js';

const CHOICE_HINT = 'Bir eşleşmenin düğmesini kullanın veya “hepsini onaylıyorum” / “hepsini reddediyorum” yazın. Tek bir eşleşme varsa “evet” / “hayır” yeterlidir.';

export default function useRelationshipConfirmations({ connectionId, patchCard, askQuestion }) {
  const sessionsRef = useRef(new Map());
  const activeRef = useRef(new Set());

  const say = useCallback((node, content) => patchCard(node.id, data => ({
    turns: [...(data.turns || []), { role: 'ai', content, ts: Date.now() }],
  })), [patchCard]);

  const respond = useCallback(async (node, matches, accepted) => {
    if (!connectionId || node.data?.loading || activeRef.current.has(node.id)) return;
    const sessionKey = `${connectionId}:${node.id}:${node.data?.queryId}`;
    const session = sessionsRef.current.get(sessionKey) ?? {
      pending: getPendingConfirmations(node.data),
      rejected: Boolean(node.data?.confirmationRejected),
    };
    sessionsRef.current.set(sessionKey, session);
    if (!session.pending.length) return;

    activeRef.current.add(node.id);
    patchCard(node.id, { confirming: true });
    try {
      const question = node.data?.clarificationQuestion
        ?? [...(node.data?.turns || [])].reverse().find(turn => turn.role === 'user')?.content;
      const result = await applyRelationshipDecisions({
        pending: session.pending, matches, accepted,
        save: (match, decision) => learnRelationship(connectionId, match, decision, question),
        onState: (key, state) => patchCard(node.id, data => ({
          confirmationStates: { ...data.confirmationStates, [key]: state },
        })),
        onApplied: (match, remaining) => {
          session.pending = remaining;
          session.rejected ||= !accepted;
          patchCard(node.id, { pendingConfirmations: remaining, confirmationRejected: session.rejected });
          say(node, `${match.fromTable}.${match.fromColumn} → ${match.toTable}.${match.toColumn}: `
            + (accepted ? 'onaylandı ve aktif sözlüğe uygulandı.' : 'reddedildi ve aktif sözlükten kaldırıldı.'));
        },
      });

      const resume = getRelationshipResume(node.data, result, session.rejected);
      if (resume) {
        patchCard(node.id, { needsRelationshipClarification: false });
        await askQuestion({ ...node, data: {
          ...node.data, queryId: resume.parentQueryId, pendingConfirmations: [], needsRelationshipClarification: false,
        } }, resume.question);
      } else if (!result.pending.length && session.rejected && node.data?.needsRelationshipClarification) {
        say(node, 'Reddedilen ilişki nedeniyle özgün soru otomatik çalıştırılmadı. Sorunuzu yeniden ifade edebilirsiniz.');
      }
    } finally {
      activeRef.current.delete(node.id);
      patchCard(node.id, { confirming: false });
    }
  }, [connectionId, patchCard, askQuestion, say]);

  const handleConfirmMatch = useCallback((node, match, accepted) =>
    respond(node, [match], accepted), [respond]);

  const handleAsk = useCallback(async (node, text) => {
    if (node.data?.loading || activeRef.current.has(node.id)) return;
    const sessionKey = `${connectionId}:${node.id}:${node.data?.queryId}`;
    const pending = sessionsRef.current.get(sessionKey)?.pending ?? getPendingConfirmations(node.data);
    const decision = parseRelationshipDecision(text, pending);
    if (decision) {
      patchCard(node.id, data => ({
        turns: [...(data.turns || []), { role: 'user', content: text, ts: Date.now() }],
      }));
      if (decision.ambiguous) say(node, CHOICE_HINT);
      else await respond(node, decision.matches, decision.accepted);
      return;
    }
    sessionsRef.current.delete(sessionKey);
    activeRef.current.add(node.id);
    try {
      await askQuestion(node, text);
    } finally {
      activeRef.current.delete(node.id);
    }
  }, [connectionId, askQuestion, patchCard, respond, say]);

  return { handleAsk, handleConfirmMatch };
}