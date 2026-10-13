const nodemailer = require('nodemailer');

const SUBJECT_PREFIXES = {
  down: 'DOWN',
  recovered: 'RECOVERED',
  'autoheal-exhausted': 'AUTO-HEAL GAVE UP',
};

/**
 * E-mails state changes only (down, recovered, auto-heal gave up), at most once per
 * service+event per `minIntervalMs`. Without SMTP settings it is a no-op.
 */
function createEmailNotifier({ smtp, minIntervalMs, transporter, now = Date.now, logger = console }) {
  if (!smtp.enabled) {
    logger.info('E-mail alerts disabled: OPS_SMTP_HOST, OPS_SMTP_FROM and OPS_ALERT_RECIPIENTS are not all set');
    return { notify: async () => {} };
  }

  const mailer = transporter ?? nodemailer.createTransport({
    host: smtp.host,
    port: smtp.port,
    secure: smtp.port === 465,
    auth: smtp.user ? { user: smtp.user, pass: smtp.password } : undefined,
  });
  const lastSentAt = new Map();

  async function notify(event) {
    const key = `${event.serviceId}:${event.type}`;
    if (now() - (lastSentAt.get(key) ?? 0) < minIntervalMs) return;
    lastSentAt.set(key, now());

    try {
      await mailer.sendMail({
        from: smtp.from,
        to: smtp.recipients,
        subject: `[Grafirio] ${SUBJECT_PREFIXES[event.type]}: ${event.serviceName}`,
        text: `${event.message}\n\nTime: ${new Date(event.at).toISOString()}`,
      });
    } catch (error) {
      logger.error('Failed to send alert e-mail', { key, message: error.message });
    }
  }

  return { notify };
}

module.exports = { createEmailNotifier };
