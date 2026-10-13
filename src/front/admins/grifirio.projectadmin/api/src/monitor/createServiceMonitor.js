const { decideAutoHeal } = require('./decideAutoHeal');

// Two consecutive failed checks before alerting, so a single slow probe does not page anyone.
const ALERT_AFTER_FAILURES = 2;
const MAX_RECENT_EVENTS = 50;
const AUTO_HEAL_ACTOR = { type: 'autoheal', id: 'monitor' };

const createState = () => ({
  status: 'unknown',
  latencyMs: null,
  error: null,
  detail: null,
  container: null,
  consecutiveFailures: 0,
  alertedDown: false,
  manuallyStopped: false,
  healAttempts: 0,
  lastHealAt: null,
  gaveUp: false,
});

/** Periodically probes every registered service, raises alerts on state changes and applies the auto-heal policy. */
function createServiceMonitor({ registry, docker, probe, settings, audit, notifier, intervalMs, now = Date.now, logger = console }) {
  const states = new Map(registry.services.map((service) => [service.id, createState()]));
  const recentEvents = [];
  let dockerAvailable = null;
  let checkedAt = null;
  let timer = null;
  let inFlight = null;

  function emit(type, service, message) {
    const event = { type, serviceId: service.id, serviceName: service.name, message, at: now() };
    recentEvents.unshift(event);
    if (recentEvents.length > MAX_RECENT_EVENTS) recentEvents.pop();
    void notifier.notify(event);
  }

  async function readContainer(service) {
    if (!dockerAvailable) return null;
    try {
      return await docker.getContainerState(service.container);
    } catch (error) {
      return { exists: null, running: false, status: 'unknown', health: null, error: error.message };
    }
  }

  function recordObservation(service, result, container) {
    const state = states.get(service.id);
    Object.assign(state, {
      container,
      latencyMs: result.latencyMs,
      error: result.error,
      detail: result.detail,
      manuallyStopped: settings.isManuallyStopped(service.id),
    });

    if (result.up) {
      if (state.alertedDown) emit('recovered', service, `${service.name} is reachable again.`);
      Object.assign(state, { status: 'up', consecutiveFailures: 0, alertedDown: false, healAttempts: 0, gaveUp: false });
      return;
    }

    state.status = 'down';
    state.consecutiveFailures += 1;
    const shouldAlert = !state.alertedDown && !state.manuallyStopped && state.consecutiveFailures >= ALERT_AFTER_FAILURES;
    if (shouldAlert) {
      state.alertedDown = true;
      emit('down', service, `${service.name} is down. Impact: ${service.impact}`);
    }
  }

  async function healService(service, decision) {
    const state = states.get(service.id);
    if (decision === 'give-up') {
      state.gaveUp = true;
      emit('autoheal-exhausted', service, `${service.name} is still down after ${state.healAttempts} automatic attempts. Manual action is required.`);
      audit.record({ actor: AUTO_HEAL_ACTOR, action: 'autoheal-give-up', target: service.id, outcome: 'failed' });
      return;
    }

    state.healAttempts += 1;
    state.lastHealAt = now();
    try {
      await docker.applyAction(service.container, decision);
      audit.record({ actor: AUTO_HEAL_ACTOR, action: `autoheal-${decision}`, target: service.id, outcome: 'succeeded' });
    } catch (error) {
      audit.record({ actor: AUTO_HEAL_ACTOR, action: `autoheal-${decision}`, target: service.id, outcome: 'failed', detail: error.message });
    }
  }

  async function runAutoHeal() {
    const policy = settings.getAutoHeal();
    if (!policy.enabled || !dockerAvailable) return;

    for (const service of registry.services) {
      const state = states.get(service.id);
      const dependenciesUp = service.dependsOn.every((id) => states.get(id).status === 'up');
      const decision = decideAutoHeal({ state, container: state.container, policy, dependenciesUp, now: now() });
      if (decision !== 'none') await healService(service, decision);
    }
  }

  async function runCycle() {
    dockerAvailable = await docker.isAvailable();
    await Promise.all(registry.services.map(async (service) => {
      const [result, container] = await Promise.all([probe(service), readContainer(service)]);
      recordObservation(service, result, container);
    }));
    checkedAt = now();
    await runAutoHeal();
  }

  function checkNow() {
    inFlight ??= runCycle()
      .catch((error) => logger.error('Monitor cycle failed', error))
      .finally(() => {
        inFlight = null;
      });
    return inFlight;
  }

  function snapshot() {
    return {
      checkedAt,
      docker: { available: dockerAvailable },
      services: registry.services.map((service) => {
        const state = states.get(service.id);
        return {
          id: service.id,
          name: service.name,
          category: service.category,
          dependsOn: service.dependsOn,
          impact: service.impact,
          status: state.status,
          latencyMs: state.latencyMs,
          detail: state.detail,
          error: state.error,
          container: state.container,
          consecutiveFailures: state.consecutiveFailures,
          manuallyStopped: state.manuallyStopped,
          autoHeal: { attempts: state.healAttempts, gaveUp: state.gaveUp, lastHealAt: state.lastHealAt },
        };
      }),
      events: recentEvents,
    };
  }

  /** Coarse, detail-free view for end users: no service names, hosts or errors. */
  function publicSummary() {
    const affected = new Set(
      registry.services.filter((service) => states.get(service.id).status === 'down').map((service) => service.impact),
    );
    const status = checkedAt === null ? 'unknown' : affected.size > 0 ? 'degraded' : 'ok';
    return { status, checkedAt, affectedFeatures: [...affected] };
  }

  function start() {
    void checkNow();
    timer = setInterval(checkNow, intervalMs);
  }

  function stop() {
    clearInterval(timer);
  }

  return { start, stop, checkNow, snapshot, publicSummary };
}

module.exports = { createServiceMonitor };
