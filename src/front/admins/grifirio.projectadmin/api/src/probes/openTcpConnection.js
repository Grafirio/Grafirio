const net = require('node:net');

/** Resolves when a TCP connection to host:port opens; rejects on error or timeout. */
function openTcpConnection(host, port, timeoutMs) {
  return new Promise((resolve, reject) => {
    const socket = net.connect({ host, port });
    socket.setTimeout(timeoutMs);
    socket.once('connect', () => {
      socket.destroy();
      resolve();
    });
    socket.once('timeout', () => {
      socket.destroy();
      reject(new Error(`Connection to ${host}:${port} timed out`));
    });
    socket.once('error', reject);
  });
}

module.exports = { openTcpConnection };
