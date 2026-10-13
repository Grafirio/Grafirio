import { useCallback, useEffect, useState } from 'react';
import { describeOpsError, getOpsStatus, isCancelled } from '../services/opsApi';
import { STATUS_POLL_INTERVAL_MS } from '../constants/ops';

/** Polls the Ops agent; `error` is set while the agent itself is unreachable. */
export function useOpsStatus() {
  const [status, setStatus] = useState(null);
  const [error, setError] = useState(null);

  const refresh = useCallback(async (signal) => {
    try {
      setStatus(await getOpsStatus(signal));
      setError(null);
    } catch (requestError) {
      if (isCancelled(requestError)) return;
      setError(describeOpsError(requestError));
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    refresh(controller.signal);
    const timer = setInterval(() => refresh(controller.signal), STATUS_POLL_INTERVAL_MS);
    return () => {
      controller.abort();
      clearInterval(timer);
    };
  }, [refresh]);

  return { status, error, refresh };
}
