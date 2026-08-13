import React, { createContext, useCallback, useContext, useEffect, useState } from 'react';

const ThemeContext = createContext(null);

const STORAGE_KEY = 'grafirio.theme';

const THEMES = ['light', 'dark'];
const ACCENTS = ['blue', 'orange', 'teal', 'violet'];
const DENSITIES = ['comfortable', 'balanced', 'compact'];

// Varsayilan mavi/lacivert: bugune kadarki tek marka rengiyle ayni goruntu.
// Digerleri (turuncu/teal/violet) Ayarlar > Gorunum ve Tema'dan secilebilir.
const DEFAULTS = { theme: 'light', accent: 'blue', density: 'balanced' };

function readStored() {
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    if (!raw) return DEFAULTS;
    const parsed = JSON.parse(raw);
    return {
      theme: THEMES.includes(parsed.theme) ? parsed.theme : DEFAULTS.theme,
      accent: ACCENTS.includes(parsed.accent) ? parsed.accent : DEFAULTS.accent,
      density: DENSITIES.includes(parsed.density) ? parsed.density : DEFAULTS.density,
    };
  } catch {
    return DEFAULTS;
  }
}

export const ThemeProvider = ({ children }) => {
  const [prefs, setPrefs] = useState(readStored);

  // Attribute'lar dogrudan <html> uzerinde: tokens.css'teki [data-theme]/
  // [data-accent]/[data-density] secicileri boylece tum uygulamayi kaplar,
  // her sayfanin kendi kok elemanina tek tek yazmasi gerekmez.
  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('data-theme', prefs.theme);
    root.setAttribute('data-accent', prefs.accent);
    root.setAttribute('data-density', prefs.density);
  }, [prefs]);

  useEffect(() => {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(prefs));
    } catch {
      // localStorage kapali/dolu olabilir; tercih o oturumda bellekte kalir.
    }
  }, [prefs]);

  const setTheme = useCallback((theme) => {
    if (THEMES.includes(theme)) setPrefs((p) => ({ ...p, theme }));
  }, []);
  const setAccent = useCallback((accent) => {
    if (ACCENTS.includes(accent)) setPrefs((p) => ({ ...p, accent }));
  }, []);
  const setDensity = useCallback((density) => {
    if (DENSITIES.includes(density)) setPrefs((p) => ({ ...p, density }));
  }, []);
  const toggleTheme = useCallback(() => {
    setPrefs((p) => ({ ...p, theme: p.theme === 'dark' ? 'light' : 'dark' }));
  }, []);

  return (
    <ThemeContext.Provider
      value={{ ...prefs, setTheme, setAccent, setDensity, toggleTheme, THEMES, ACCENTS, DENSITIES }}
    >
      {children}
    </ThemeContext.Provider>
  );
};

export const useTheme = () => {
  const context = useContext(ThemeContext);
  if (!context) {
    throw new Error('useTheme must be used within a ThemeProvider');
  }
  return context;
};
