const MS_PER_SECOND = 1000;

/**
 * Pure auto-heal policy. Returns what the monitor should do for one service right now:
 * 'start' | 'restart' | 'give-up' | 'none'.
 */
function decideAutoHeal({ state, container, policy, dependenciesUp, now }) {
  if (!policy.enabled || state.status !== 'down' || state.manuallyStopped || state.gaveUp) return 'none';
  if (state.consecutiveFailures < policy.failureThreshold) return 'none';
  // Heal the dependency first; its own pass will run on the same cycle.
  if (!dependenciesUp) return 'none';
  // A missing container needs `compose up`, which is a deliberate operator action.
  if (!container?.exists) return 'none';
  // A fresh attempt gets its full cooldown before the policy concludes it failed.
  const cooldownMs = policy.cooldownSeconds * MS_PER_SECOND;
  if (state.lastHealAt !== null && now - state.lastHealAt < cooldownMs) return 'none';
  if (state.healAttempts >= policy.maxAttempts) return 'give-up';

  return container.running ? 'restart' : 'start';
}

module.exports = { decideAutoHeal };
