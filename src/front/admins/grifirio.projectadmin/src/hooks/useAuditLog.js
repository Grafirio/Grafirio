import { useEffect, useState } from 'react';
import { describeOpsError, getAuditLog } from '../services/opsApi';

/** Loads the audit trail on mount and whenever `version` changes. */
export function useAuditLog(version) {
  const [entries, setEntries] = useState([]);
  const [error, setError] = useState(null);

  useEffect(() => {
    let isCurrent = true;
    getAuditLog()
      .then((loaded) => {
        if (!isCurrent) return;
        setEntries(loaded);
        setError(null);
      })
      .catch((requestError) => {
        if (isCurrent) setError(describeOpsError(requestError));
      });
    return () => {
      isCurrent = false;
    };
  }, [version]);

  return { entries, error };
}
