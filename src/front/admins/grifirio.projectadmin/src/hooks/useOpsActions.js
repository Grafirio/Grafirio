import { useCallback, useState } from 'react';
import { describeOpsError } from '../services/opsApi';

/**
 * Runs one operator action at a time and exposes its outcome.
 * `version` changes after every action so dependent views (audit log) can reload.
 */
export function useOpsActions(onSettled) {
  const [pendingKey, setPendingKey] = useState(null);
  const [feedback, setFeedback] = useState(null);
  const [version, setVersion] = useState(0);

  const run = useCallback(async (key, task, successText) => {
    setPendingKey(key);
    setFeedback(null);
    try {
      await task();
      setFeedback({ type: 'success', text: successText });
    } catch (error) {
      setFeedback({ type: 'danger', text: describeOpsError(error) });
    } finally {
      setPendingKey(null);
      setVersion((current) => current + 1);
      await onSettled();
    }
  }, [onSettled]);

  return { pendingKey, feedback, version, run };
}
