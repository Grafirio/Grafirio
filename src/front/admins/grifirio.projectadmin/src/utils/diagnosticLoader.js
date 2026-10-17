// A response owns the entire list/summary pair. Invalidate immediately on filter
// cleanup, including the debounce window before the next request starts.
export function createDiagnosticLoader({ request, onData, onError, onLoading }) {
  let sequence = 0;
  return {
    invalidate() { ++sequence; },
    async load(filters) {
      const current = ++sequence;
      onLoading(true);
      onError(null);
      try {
        const response = await request({ ...filters, includeSummary: true });
        if (current === sequence) onData(response);
      } catch (error) {
        if (current === sequence) onError(error);
      } finally {
        if (current === sequence) onLoading(false);
      }
    },
  };
}
