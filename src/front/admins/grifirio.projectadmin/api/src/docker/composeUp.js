const { spawn } = require('node:child_process');

const COMPOSE_TIMEOUT_MS = 5 * 60_000;
const OUTPUT_TAIL_CHARS = 4_000;

/**
 * Runs `docker compose up -d --wait` for the given compose service names.
 * Arguments are passed as an array without a shell, so names cannot inject commands.
 */
function composeUp({ composeFile, composeDir, serviceNames, env }) {
  return new Promise((resolve, reject) => {
    const child = spawn(
      'docker',
      ['compose', '-f', composeFile, 'up', '-d', '--wait', ...serviceNames],
      { cwd: composeDir, env, shell: false, windowsHide: true },
    );

    let output = '';
    const collect = (chunk) => {
      output = (output + chunk.toString()).slice(-OUTPUT_TAIL_CHARS);
    };
    child.stdout.on('data', collect);
    child.stderr.on('data', collect);

    const timer = setTimeout(() => child.kill(), COMPOSE_TIMEOUT_MS);
    child.once('error', (error) => {
      clearTimeout(timer);
      reject(error);
    });
    child.once('close', (exitCode) => {
      clearTimeout(timer);
      resolve({ exitCode, output });
    });
  });
}

module.exports = { composeUp };
