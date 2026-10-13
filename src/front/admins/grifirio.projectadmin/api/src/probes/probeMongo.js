const { MongoClient } = require('mongodb');

/** Mongo's `ping` command needs no authentication, so only reachability is verified. */
async function probeMongo({ host, port }, timeoutMs) {
  const client = new MongoClient(`mongodb://${host}:${port}`, {
    serverSelectionTimeoutMS: timeoutMs,
    connectTimeoutMS: timeoutMs,
  });
  try {
    await client.connect();
    await client.db('admin').command({ ping: 1 });
    return {};
  } finally {
    await client.close();
  }
}

module.exports = { probeMongo };
