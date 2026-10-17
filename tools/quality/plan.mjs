import { execFileSync } from 'node:child_process';
import { appendFileSync } from 'node:fs';
import { pathToFileURL } from 'node:url';

const apps = ['src/front/grifirio.front', 'src/front/admins/grifirio.projectadmin', 'src/front/admins/grifirio.useradmin'];
export function planChecks(files, all = false) {
  const paths = files.map((p) => p.replaceAll('\\', '/'));
  const tooling = paths.some((p) => /^(tools\/quality\/|\.github\/workflows\/|scripts\/Prepare-TestPackages\.ps1)/.test(p));
  const dotnet = all || tooling || paths.some((p) => /^(src\/(services|shared|bridge)\/|tests\/|tools\/Grafirio\.|shared-packages\.ref$|nuget\.config$|Grafirio\.sln$)/.test(p));
  const python = all || tooling || paths.some((p) => /^(src\/ai\/|src\/shared\/Grafirio.QueryPolicy\/|src\/services\/Grafirio.DataAnalysis.Api\/|src\/bridge\/Grafirio.Bridge.Contracts\/)/.test(p));
  const sharedFront = all || tooling || paths.some((p) => p.startsWith('src/front/shared/'));
  const frontends = apps.filter((app) => sharedFront || paths.some((p) => p.startsWith(`${app}/`)));
  const ops = all || tooling || paths.some((p) => p.startsWith(`${apps[1]}/api/`));
  // Admin API changes can break the panel without changing any JSX.
  const browser = all || tooling || frontends.includes(apps[1]) || paths.some((p) => p.startsWith('src/services/Grafirio.DataAnalysis.Api/'));
  return { dotnet, python, frontends, ops, browser };
}

export function changedFiles(base) {
  // No shell: file names and refs never become executable command text.
  const git = (...args) => execFileSync('git', args, { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 });
  const paths = [];
  if (base) {
    if (!/^[a-f0-9]{40}$/i.test(base)) throw new Error('Base must be a full commit SHA.');
    paths.push(...git('diff', '--no-renames', '--name-only', '-z', base, 'HEAD').split('\0'));
  }
  paths.push(...git('diff', '--no-renames', '--name-only', '-z', 'HEAD').split('\0'));
  paths.push(...git('ls-files', '--others', '--exclude-standard', '-z').split('\0'));
  return [...new Set(paths.filter(Boolean))];
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const all = process.argv.includes('--all');
  const baseIndex = process.argv.indexOf('--base');
  const plan = planChecks(changedFiles(baseIndex < 0 ? undefined : process.argv[baseIndex + 1]), all);
  console.log(JSON.stringify(plan, null, 2));
  if (process.env.GITHUB_OUTPUT) {
    appendFileSync(process.env.GITHUB_OUTPUT, Object.entries(plan).map(([k, v]) => `${k}=${JSON.stringify(v)}\n`).join(''));
  }
}
