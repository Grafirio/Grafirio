/** An error whose message is safe to return to the caller together with its HTTP status. */
class OpsError extends Error {
  constructor(status, message) {
    super(message);
    this.name = 'OpsError';
    this.status = status;
  }
}

module.exports = { OpsError };
