const normalizeName = (value) => value.replace(/[[\]]/g, '').trim().toLowerCase();

export default function relationshipKey(match) {
  return `rel:${normalizeName(match.fromTable)}.${normalizeName(match.fromColumn)}`
    + `->${normalizeName(match.toTable)}.${normalizeName(match.toColumn)}`;
}