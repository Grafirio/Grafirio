export default function getRelationshipResume(data, result, hasRejected) {
  if (!result.allApproved || result.hasFailure || result.pending.length || hasRejected
      || !data.needsRelationshipClarification || !data.clarificationQuestion || !data.queryId) return null;
  return { question: data.clarificationQuestion, parentQueryId: data.queryId };
}