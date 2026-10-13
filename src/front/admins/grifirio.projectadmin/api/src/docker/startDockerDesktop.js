const { spawn } = require('node:child_process');

/** Launches Docker Desktop detached; returns once the process has been spawned, not when the daemon is ready. */
function startDockerDesktop(executablePath) {
  return new Promise((resolve, reject) => {
    const child = spawn(executablePath, [], { detached: true, stdio: 'ignore', shell: false });
    child.once('error', reject);
    child.once('spawn', () => {
      child.unref();
      resolve();
    });
  });
}

module.exports = { startDockerDesktop };
