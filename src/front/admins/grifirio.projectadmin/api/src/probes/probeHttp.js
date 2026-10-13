/** Any HTTP response counts as alive unless `expectStatus` narrows it. */
async function probeHttp({ url, expectStatus }, timeoutMs) {
  const response = await fetch(url, { signal: AbortSignal.timeout(timeoutMs), redirect: 'manual' });
  if (expectStatus && !expectStatus.includes(response.status)) {
    throw new Error(`Unexpected HTTP status ${response.status}`);
  }
  return { detail: `HTTP ${response.status}` };
}

module.exports = { probeHttp };
