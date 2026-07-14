import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '--profile',
  'app',
];
const results = [];
const approvedExclusions = Object.freeze([]);

function relayOutput(output, destination) {
  if (output) destination.write(output);
}

function isApprovedExclusion(stage, skipLine) {
  return approvedExclusions.some(
    (exclusion) =>
      exclusion.stage === stage &&
      typeof exclusion.reason === 'string' &&
      exclusion.reason.length > 0 &&
      typeof exclusion.sotCitation === 'string' &&
      exclusion.sotCitation.length > 0 &&
      typeof exclusion.humanApproval === 'string' &&
      exclusion.humanApproval.length > 0 &&
      skipLine.includes(exclusion.reason),
  );
}

function detectUnapprovedStageSkips(output) {
  const skips = [];
  const skipPattern = /^\s*SKIP\s*\|\s*(D1[1-9])\b.*$/gim;
  let match;
  while ((match = skipPattern.exec(output)) !== null) {
    const stage = match[1];
    if (!isApprovedExclusion(stage, match[0])) skips.push({ stage, line: match[0].trim() });
  }
  return skips;
}

function run(label, command, args, env = process.env) {
  console.log(`\n=== START | ${label} ===`);
  const result = spawnSync(command, args, { cwd: root, env, encoding: 'utf8' });
  const output = `${result.stdout ?? ''}${result.stderr ?? ''}`;
  relayOutput(result.stdout, process.stdout);
  relayOutput(result.stderr, process.stderr);
  const ok = !result.error && result.status === 0;
  const skippedStages = detectUnapprovedStageSkips(output);
  for (const skipped of skippedStages) {
    console.error(
      `FAIL | ${skipped.stage} | unapproved exclusion emitted by ${label}: ${skipped.line}`,
    );
  }
  const stageOk = ok && skippedStages.length === 0;
  results.push({ label, ok: stageOk, status: result.status, skippedStages });
  console.log(`=== END | ${label} | ${stageOk ? 'PASS' : 'FAIL'} ===`);
  return stageOk;
}

function composeUp(mode) {
  const stub = mode === 'stub' ? 'true' : 'false';
  const env = {
    ...process.env,
    BOOKING_TRIP_USE_DEV_STUB: stub,
    BOOKING_PAYMENT_USE_DEV_STUB: stub,
    BOOKING_IDENTITY_USE_DEV_STUB: stub,
  };
  return run(
    `stack-${mode}`,
    'docker',
    [...compose, 'up', '-d', '--force-recreate', 'booking', 'gateway'],
    env,
  );
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

const sprint3Stages = [
  { id: 'D11', script: 'scripts/run-day11-newman-local.js', mode: 'real' },
  { id: 'D12', script: 'scripts/run-day12-newman-local.mjs', mode: 'real' },
  { id: 'D13', script: 'scripts/run-day13-newman-local.js', mode: 'stub' },
  { id: 'D14', script: 'scripts/run-day14-voucher-e2e.mjs', mode: 'stub' },
  { id: 'D15', script: 'scripts/run-day15-newman-local.mjs', mode: 'stub' },
  { id: 'D16', script: 'scripts/run-day16-newman-local.mjs', mode: 'stub' },
  { id: 'D17', script: 'scripts/run-day17-newman-local.mjs', mode: 'stub' },
  { id: 'D18', script: 'scripts/run-day18-newman-local.mjs', mode: 'real' },
  { id: 'D19', script: 'scripts/run-day19-newman-local.mjs', mode: 'real' },
];

const missingRequiredStages = sprint3Stages.filter(
  ({ script }) => !fs.existsSync(path.join(root, script)),
);
for (const { id, script } of sprint3Stages) {
  console.log(`REQUIRED | ${id} | ${script}`);
}
if (missingRequiredStages.length > 0) {
  for (const { id, script } of missingRequiredStages) {
    console.error(`FAIL | ${id} | required matrix invocation is missing: ${script}`);
  }
  console.error('Required Sprint 3 matrix stages cannot be skipped without an approved exclusion.');
  process.exitCode = 1;
} else {
  run('stack-preflight', 'docker', [...compose, 'up', '-d']);
  const day11 = sprint3Stages.find((stage) => stage.id === 'D11');
  const day12 = sprint3Stages.find((stage) => stage.id === 'D12');
  run(day11.id, process.execPath, [path.join(root, day11.script)]);
  run(day12.id, process.execPath, [path.join(root, day12.script)]);

  composeUp('stub');
  for (const stage of sprint3Stages.filter(({ mode }) => mode === 'stub')) {
    run(stage.id, process.execPath, [path.join(root, stage.script)]);
  }
  resetPostgresConnections();

  composeUp('real');
  for (const stage of sprint3Stages.filter(
    (stage) => stage.mode === 'real' && stage.id !== 'D11' && stage.id !== 'D12',
  )) {
    run(stage.id, process.execPath, [path.join(root, stage.script)]);
  }
  run('D18-crossday', process.execPath, [path.join(root, 'scripts/run-day18-crossday-local.mjs')]);

  console.log('\n=== FULL LOCAL E2E SUMMARY ===');
  for (const result of results) console.log(`${result.ok ? 'PASS' : 'FAIL'} | ${result.label}`);
  const failures = results.filter((result) => !result.ok);
  console.log(`TOTAL: ${results.length - failures.length}/${results.length} stages passed`);
  process.exitCode = failures.length === 0 ? 0 : 1;
}
