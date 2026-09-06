export default function isDesktopApp() {
  return Boolean(globalThis.window?.__GRAFIRIO_DESKTOP__);
}