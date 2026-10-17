// Create a source-only snapshot; never upload local environment files or runtime/test data.
const fs = require('node:fs');
const path = require('node:path');
const { execFileSync } = require('node:child_process');
const root = path.resolve(__dirname, '../..');
const destination = path.join(root, '.worktree', 'telemetry-buildcontext');
if (fs.existsSync(destination)) throw new Error('Build snapshot already exists; choose a fresh snapshot rather than mixing versions');
const files = execFileSync('git', ['ls-files', '-c', '-o', '--exclude-standard', '-z'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
let count = 0;
for (const file of new Set(files)) {
  if (!/^(src\/(services|shared|bridge\/Grafirio.Bridge.Contracts)\/|src\/ai\/ai-service-pycaret-engine\/|src\/front\/admins\/grifirio.projectadmin\/|Grafirio\.sln$|nuget\.config$|shared-packages\.ref$|Directory\.[^/]+$|global\.json$)/.test(file)) continue;
  if (/(^|\/)(\.env[^/]*|node_modules|bin|obj|\.venv|__pycache__|data|models|test-results|dist|e2e|test|tests)(\/|$)/.test(file)
    || /\.(sqlite3?|db|dpapi|pem|pfx|key|log)$/i.test(file)) continue;
  const source = path.join(root, file);
  if (!fs.existsSync(source) || !fs.statSync(source).isFile()) continue;
  const target = path.join(destination, file);
  fs.mkdirSync(path.dirname(target), { recursive: true }); fs.copyFileSync(source, target); count++;
}
fs.writeFileSync(path.join(destination, '.dockerignore'), '**/node_modules\n**/bin\n**/obj\n**/.env*\n**/.git\n');
console.log(`Prepared ${count} source files in the ignored build snapshot.`);
