export default function buildConnectionUpdate(changes) {
  const { password, ...fields } = changes;
  // Omission preserves the saved credential; whitespace can be a real password.
  return password === '' || password == null ? fields : { ...fields, password };
}