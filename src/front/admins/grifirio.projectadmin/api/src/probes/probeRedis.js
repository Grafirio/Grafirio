const net = require('node:net');

const PING_COMMAND = 'PING\r\n';
const ALIVE_REPLIES = ['+PONG', '-NOAUTH'];

/** Sends PING over the raw protocol. A NOAUTH reply still proves the server is alive, so no credentials are needed. */
function probeRedis({ host, port }, timeoutMs) {
  return new Promise((resolve, reject) => {
    const socket = net.connect({ host, port });
    socket.setTimeout(timeoutMs);
    socket.once('connect', () => socket.write(PING_COMMAND));
    socket.once('data', (data) => {
      socket.destroy();
      const reply = data.toString();
      if (ALIVE_REPLIES.some((prefix) => reply.startsWith(prefix))) resolve({});
      else reject(new Error('Unexpected Redis reply'));
    });
    socket.once('timeout', () => {
      socket.destroy();
      reject(new Error('Redis ping timed out'));
    });
    socket.once('error', reject);
  });
}

module.exports = { probeRedis };
