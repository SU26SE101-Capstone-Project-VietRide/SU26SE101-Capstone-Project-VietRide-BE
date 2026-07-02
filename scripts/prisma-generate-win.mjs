import { execSync } from 'node:child_process';
import { existsSync, readdirSync, rmSync, openSync, closeSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const root = join(__dirname, '..');

const schema = process.argv[2] || 'prisma/schema.prisma';
const project = process.argv[3] || 'rag';
const cwd = join(root, 'apps', project);
const generatedDir = join(cwd, 'src', 'generated', `${project}-prisma-client`);

function cleanStaleTemp() {
  if (!existsSync(generatedDir)) return;
  for (const entry of readdirSync(generatedDir, { withFileTypes: true })) {
    if (entry.isFile() && /query_engine-.*\.tmp\d+$/.test(entry.name)) {
      try { rmSync(join(generatedDir, entry.name), { force: true }); } catch { /* best-effort */ }
    }
  }
}

function isEngineLocked() {
  const dll = join(generatedDir, 'query_engine-windows.dll.node');
  if (!existsSync(dll)) return false;
  try {
    // Try to open for writing — fails with EBUSY/EPERM if loaded by another process
    const fd = openSync(dll, 'r+');
    closeSync(fd);
    return false;
  } catch {
    return true;
  }
}

function isClientHealthy() {
  if (!existsSync(generatedDir)) return false;
  const required = [
    'index.js', 'index.d.ts',
    'runtime/library.js', 'runtime/library.d.ts',
  ];
  return required.every((f) => existsSync(join(generatedDir, f)));
}

cleanStaleTemp();

const isLocked = isEngineLocked();
const healthy = isClientHealthy();

if (isLocked && healthy) {
  console.log(`[prisma-generate-win] Engine DLL locked (server running) but client is healthy — skipping regenerate, existing client is valid.`);
  process.exit(0);
}

let stderr = '';
try {
  execSync(`npx prisma generate --schema=${schema}`, {
    cwd,
    stdio: ['inherit', 'inherit', 'pipe'],
    shell: true,
    env: { ...process.env },
  });
  console.log('[prisma-generate-win] Prisma generate succeeded.');
  process.exit(0);
} catch (err) {
  stderr = err.stderr?.toString() || '';
}
if (stderr.includes('EPERM')) {
  if (isClientHealthy()) {
    console.log('[prisma-generate-win] EPERM during generate (engine DLL locked). Client is healthy — treated as success.');
    process.exit(0);
  }
  console.error('[prisma-generate-win] EPERM and client is NOT healthy. Cannot proceed.');
  process.exit(1);
}
console.error('[prisma-generate-win] Prisma generate failed:', stderr);
process.exit(1);
