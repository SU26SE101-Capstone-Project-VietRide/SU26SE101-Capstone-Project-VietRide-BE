import { spawnSync } from 'node:child_process';

const CHILD_TIMEOUT_MS = 1_200_000;
const requiredOutbox = [
  'trip.trip.boarding_started',
  'trip.trip.started',
  'parcel.parcel.loaded',
  'trip.stop.arrived',
  'parcel.parcel.unloaded',
  'trip.trip.completed',
];
const tripStates = ['SCHEDULED', 'BOARDING', 'IN_PROGRESS', 'COMPLETED'];
const parcelStates = ['PENDING', 'LOADED', 'IN_TRANSIT', 'UNLOADED'];
const polling = {
  scheduleGeneration: { intervalMs: 500, timeoutMs: 30_000 },
  autoBoarding: { intervalMs: 500, timeoutMs: 960_000 },
  eventConsumption: { intervalMs: 500, timeoutMs: 45_000 },
};
const leaks = [
  /Bearer\s+eyJ/i,
  /-----BEGIN (?:RSA )?PRIVATE KEY-----/i,
  /Idempotency-Key\s*[:=]\s*[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}/i,
];

function normalE2eCommand() {
  if (process.platform !== 'win32') {
    return { command: 'npm', args: ['run', 'e2e:day30'] };
  }

  // npm.cmd cannot be passed directly to spawnSync on this Windows Node build.
  return {
    command: process.env.ComSpec || 'cmd.exe',
    args: ['/d', '/s', '/c', 'npm.cmd run e2e:day30'],
  };
}

function validateSummary(label, output, expectFailure) {
  const marker = expectFailure ? 'DAY30_FAILURE_INJECTION=EXECUTED' : 'DAY30_RUN=PASS';
  if (!output.includes(marker)) {
    throw new Error(label + ' execution marker missing');
  }

  const line = output.split(/\r?\n/).find((value) => value.startsWith('DAY30_REDACTED_SUMMARY='));
  if (!line) {
    throw new Error(label + ' redacted summary missing');
  }

  const summary = JSON.parse(line.slice('DAY30_REDACTED_SUMMARY='.length));
  if (
    [
      summary.redacted !== true,
      summary.autoFromSchedule !== true,
      summary.cleanupResidue !== 0,
      summary.replayCount !== 1,
      summary.duplicateTransitionCount !== 0,
      summary.duplicateOutboxCount !== 0,
    ].some(Boolean)
  ) {
    throw new Error(label + ' summary state/cleanup/replay counts invalid');
  }
  if (
    [
      summary.result !== (expectFailure ? 'EXPECTED_FAILURE' : 'PASS'),
      summary.failureInjection !== expectFailure,
    ].some(Boolean)
  ) {
    throw new Error(label + ' summary result mismatch');
  }
  if (
    [
      JSON.stringify(summary.tripStates) !== JSON.stringify(tripStates),
      JSON.stringify(summary.parcelStates) !== JSON.stringify(parcelStates),
      JSON.stringify(summary.polling) !== JSON.stringify(polling),
    ].some(Boolean)
  ) {
    throw new Error(label + ' state sequence mismatch');
  }
  for (const key of requiredOutbox) {
    if (summary.outboxCounts?.[key] !== 1 || summary.duplicateCounts?.[key] !== 0) {
      throw new Error(label + ' Outbox count mismatch: ' + key);
    }
  }
}

function runChild(label, command, args, expectFailure) {
  const result = spawnSync(command, args, {
    encoding: 'utf8',
    timeout: CHILD_TIMEOUT_MS,
    killSignal: 'SIGTERM',
  });
  if (result.error) {
    const detail =
      result.error.code === 'ETIMEDOUT'
        ? 'timed out after ' + CHILD_TIMEOUT_MS + 'ms'
        : 'spawn error: ' + result.error.message;
    throw new Error(label + ' ' + detail);
  }
  if (result.signal) {
    throw new Error(label + ' terminated by signal ' + result.signal);
  }

  const output = String(result.stdout ?? '') + '\n' + String(result.stderr ?? '');
  if (leaks.some((pattern) => pattern.test(output))) {
    throw new Error(label + ' leaked credential/key/idempotency output');
  }
  if (result.status !== 0) {
    throw new Error(label + ' exited ' + result.status);
  }
  validateSummary(label, output, expectFailure);
  console.log(label + ' redacted summary: PASS');
}

runChild(
  'failure-injection',
  process.execPath,
  ['scripts/run-day30-sprint4-demo.mjs', '--verify-cleanup-failure'],
  true,
);
runChild('normal-e2e', ...Object.values(normalE2eCommand()), false);
