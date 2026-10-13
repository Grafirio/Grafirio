import axios from 'axios';
import { OPS_SUMMARY_URL, OUTAGE_REQUEST_TIMEOUT_MS } from '../constants/outageApi.js';

/** Detail-free platform status; unauthenticated on purpose so it works when sign-in is the broken part. */
export const getOutageSummary = async (signal) => {
  const { data } = await axios.get(OPS_SUMMARY_URL, { signal, timeout: OUTAGE_REQUEST_TIMEOUT_MS });
  return data;
};
