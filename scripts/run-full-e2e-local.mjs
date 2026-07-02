import { spawnSync } from 'node:child_process';
import path from 'node:path';

const root = process.cwd();
const compose = ['compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app'];
const results = [];

function run(label, command, args, env = process.env) {
  console.log(`\n=== START ${label} ===`);
  const result = spawnSync(command, args, { cwd: root, env, stdio: 'inherit' });
  const ok = !result.error && result.status === 0;
  results.push({ label, ok, status: result.status });
  console.log(`=== END ${label}: ${ok ? 'PASS' : 'FAIL'} ===`);
  return ok;
}

function composeUp(mode) {
  const stub = mode === 'stub' ? 'true' : 'false';
  const env = {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: stub,
    BOOKING_PAYMENT_USE_DEV_STUB: stub,
    BOOKING_IDENTITY_USE_DEV_STUB: stub,
  };
  return run(`stack-${mode}`, 'docker', [...compose, 'up', '-d', '--force-recreate', 'booking', 'gateway'], env);
}

function resetPostgresConnections() {
  if (!run('postgres-connection-reset', 'docker', ['restart', 'vietride_postgres'])) return false;
  for (let attempt = 0; attempt < 30; attempt += 1) {
    const probe = spawnSync(
      'docker',
      ['inspect', '--format', '{{.State.Health.Status}}', 'vietride_postgres'],
      { cwd: root, encoding: 'utf8' },
    );
    if (probe.status === 0 && probe.stdout.trim() === 'healthy') return true;
    spawnSync(process.execPath, ['-e', 'setTimeout(() => {}, 1000)']);
  }
  results.push({ label: 'postgres-health-after-reset', ok: false, status: 1 });
  return false;
}

run('stack-preflight', 'docker', [...compose, 'up', '-d']);
for (const day of ['6', '7', '8', '9', '11']) {
  run(`day-${day}`, process.execPath, [path.join(root, `scripts/run-day${day}-newman-local.js`)]);
}

composeUp('stub');
run('day-13', process.execPath, [path.join(root, 'scripts/run-day13-newman-local.js')]);
run('day-14', process.execPath, [path.join(root, 'scripts/run-day14-voucher-e2e.mjs')]);
resetPostgresConnections();
composeUp('stub');
run('day-15', process.execPath, [path.join(root, 'scripts/run-day15-newman-local.mjs')]);
run('day-17', process.execPath, [path.join(root, 'scripts/run-day17-newman-local.mjs')]);

composeUp('real');
run('day-18', process.execPath, [path.join(root, 'scripts/run-day18-newman-local.mjs')]);
run('day-18-crossday', process.execPath, [path.join(root, 'scripts/run-day18-crossday-local.mjs')]);

console.log('\n=== FULL LOCAL E2E SUMMARY ===');
for (const result of results) console.log(`${result.ok ? 'PASS' : 'FAIL'} | ${result.label}`);
const failures = results.filter((result) => !result.ok);
console.log(`TOTAL: ${results.length - failures.length}/${results.length} stages passed`);
console.log('SKIP | Google OAuth real-token leg (external credential unavailable)');
process.exitCode = failures.length === 0 ? 0 : 1;
