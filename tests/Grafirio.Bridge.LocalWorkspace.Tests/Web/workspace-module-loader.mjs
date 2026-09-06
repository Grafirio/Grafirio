import { readFile } from 'node:fs/promises';
import vm from 'node:vm';

export async function loadModuleGraph(entryUrl, context, readSource = url => readFile(url, 'utf8')) {
  const modules = new Map();
  function instantiate(url) {
    if (!modules.has(url.href)) {
      // Cache the pending read as well as its module so diamond imports share one instance.
      modules.set(url.href, Promise.resolve().then(async () =>
        new vm.SourceTextModule(await readSource(url), { context, identifier: url.href })));
    }
    return modules.get(url.href);
  }
  const entry = await instantiate(entryUrl);
  // The VM links the entire graph, including cycles; the linker only supplies instances.
  await entry.link((specifier, parent) => instantiate(new URL(specifier, parent.identifier)));
  return entry;
}