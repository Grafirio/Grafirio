const fs = require('node:fs');
const path = require('node:path');
const { OpsError } = require('../OpsError');

const SETTINGS_FILE_NAME = 'settings.json';
const HTTP_BAD_REQUEST = 400;

const AUTO_HEAL_LIMITS = {
  failureThreshold: { min: 1, max: 10 },
  maxAttempts: { min: 1, max: 10 },
  cooldownSeconds: { min: 10, max: 3600 },
};

const DEFAULT_SETTINGS = {
  autoHeal: { enabled: false, failureThreshold: 3, maxAttempts: 3, cooldownSeconds: 120 },
  manuallyStopped: [],
};

function validateAutoHealPatch(patch) {
  const validated = {};
  if ('enabled' in patch) {
    if (typeof patch.enabled !== 'boolean') throw new OpsError(HTTP_BAD_REQUEST, 'enabled must be a boolean');
    validated.enabled = patch.enabled;
  }
  for (const [field, { min, max }] of Object.entries(AUTO_HEAL_LIMITS)) {
    if (!(field in patch)) continue;
    if (!Number.isInteger(patch[field]) || patch[field] < min || patch[field] > max) {
      throw new OpsError(HTTP_BAD_REQUEST, `${field} must be an integer between ${min} and ${max}`);
    }
    validated[field] = patch[field];
  }
  return validated;
}

/** Persists operator choices (auto-heal policy, services stopped on purpose) across restarts. */
function createSettingsStore(dataDir) {
  const filePath = path.join(dataDir, SETTINGS_FILE_NAME);
  fs.mkdirSync(dataDir, { recursive: true });

  const stored = fs.existsSync(filePath) ? JSON.parse(fs.readFileSync(filePath, 'utf8')) : {};
  const settings = {
    autoHeal: { ...DEFAULT_SETTINGS.autoHeal, ...stored.autoHeal },
    manuallyStopped: new Set(stored.manuallyStopped ?? []),
  };

  const save = () => fs.writeFileSync(filePath, JSON.stringify({
    autoHeal: settings.autoHeal,
    manuallyStopped: [...settings.manuallyStopped],
  }, null, 2));

  return {
    getAutoHeal: () => ({ ...settings.autoHeal }),
    updateAutoHeal(patch) {
      Object.assign(settings.autoHeal, validateAutoHealPatch(patch));
      save();
      return { ...settings.autoHeal };
    },
    isManuallyStopped: (serviceId) => settings.manuallyStopped.has(serviceId),
    markManuallyStopped(serviceId) {
      settings.manuallyStopped.add(serviceId);
      save();
    },
    clearManuallyStopped(serviceId) {
      if (settings.manuallyStopped.delete(serviceId)) save();
    },
  };
}

module.exports = { createSettingsStore };
