const ACCEPT_ONE = new Set(['evet', 'onaylıyorum']);
const REJECT_ONE = new Set(['hayır', 'reddediyorum']);
const ACCEPT_ALL = 'hepsini onaylıyorum';
const REJECT_ALL = 'hepsini reddediyorum';

export default function parseRelationshipDecision(text, pending) {
  if (!pending.length) return null;
  const answer = text.trim().toLocaleLowerCase('tr-TR').replace(/[.!?]+$/u, '').trim();
  const isAll = answer === ACCEPT_ALL || answer === REJECT_ALL;
  if (!isAll && !ACCEPT_ONE.has(answer) && !REJECT_ONE.has(answer)) return null;
  const accepted = answer === ACCEPT_ALL || ACCEPT_ONE.has(answer);
  return { accepted, matches: isAll || pending.length === 1 ? pending : [], ambiguous: !isAll && pending.length > 1 };
}