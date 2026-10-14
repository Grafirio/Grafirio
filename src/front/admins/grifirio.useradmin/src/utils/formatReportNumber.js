export default function formatReportNumber(value) {
  return Number.isFinite(value) ? value.toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—';
}
