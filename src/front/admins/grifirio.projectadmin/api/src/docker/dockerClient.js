const Docker = require('dockerode');

const PING_TIMEOUT_MS = 3_000;
const STOP_TIMEOUT_SECONDS = 15;
const HTTP_NOT_MODIFIED = 304;
const HTTP_NOT_FOUND = 404;

const withTimeout = (promise, timeoutMs, message) => {
  let timer;
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => reject(new Error(message)), timeoutMs);
  });
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer));
};

/** Thin wrapper over the Docker Engine API: no shell, no command strings. */
function createDockerClient(docker = new Docker()) {
  async function isAvailable() {
    try {
      await withTimeout(docker.ping(), PING_TIMEOUT_MS, 'Docker ping timed out');
      return true;
    } catch {
      // An unreachable daemon is the answer here, not an error to propagate.
      return false;
    }
  }

  async function getContainerState(name) {
    try {
      const { State } = await docker.getContainer(name).inspect();
      return { exists: true, running: State.Running, status: State.Status, health: State.Health?.Status ?? null };
    } catch (error) {
      if (error.statusCode === HTTP_NOT_FOUND) return { exists: false, running: false, status: 'missing', health: null };
      throw error;
    }
  }

  async function applyAction(name, action) {
    const container = docker.getContainer(name);
    try {
      if (action === 'start') await container.start();
      else if (action === 'stop') await container.stop({ t: STOP_TIMEOUT_SECONDS });
      else if (action === 'restart') await container.restart({ t: STOP_TIMEOUT_SECONDS });
      else throw new Error(`Unsupported container action: ${action}`);
    } catch (error) {
      // 304 = already in the requested state.
      if (error.statusCode !== HTTP_NOT_MODIFIED) throw error;
    }
  }

  return { isAvailable, getContainerState, applyAction };
}

module.exports = { createDockerClient };
