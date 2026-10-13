import { useEffect, useState } from 'react';
import { getOutageSummary } from '../services/outageService.js';
import { OUTAGE_POLL_INTERVAL_MS } from '../constants/outageApi.js';

/**
 * Latest platform status, or null when unknown. A failed or slow status request is
 * treated as "unknown", never as an outage: the banner must not itself raise false alarms.
 */
export function useOutageSummary() {
  const [summary, setSummary] = useState(null);

  useEffect(() => {
    const controller = new AbortController();

    const load = async () => {
      try {
        setSummary(await getOutageSummary(controller.signal));
      } catch {
        if (!controller.signal.aborted) setSummary(null);
      }
    };

    load();
    const timer = setInterval(load, OUTAGE_POLL_INTERVAL_MS);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, []);

  return summary;
}
