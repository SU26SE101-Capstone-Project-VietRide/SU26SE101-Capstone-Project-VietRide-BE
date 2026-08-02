import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const day37 = fs.readFileSync(new URL('./run-day37-subscription-e2e.mjs', import.meta.url), 'utf8');
const day37Compose = fs.readFileSync(
  new URL('../infra/docker/docker-compose.day37-e2e.yml', import.meta.url),
  'utf8',
);
const day38 = fs.readFileSync(
  new URL('./run-day38-invoice-settlement-e2e.mjs', import.meta.url),
  'utf8',
);

test('Day 37 allocates isolated ports and only tears down its unique compose project', () => {
  assert.match(day37, /server\.listen\(0, '127\.0\.0\.1'/);
  assert.match(day37, /const composeProject = `day37-e2e-\$\{invocationId\}`/);
  assert.match(day37, /'-p',\s*composeProject/);
  assert.doesNotMatch(day37, /55001/);

  const upIndex = day37.indexOf("'up', '-d', '--build'");
  const downIndexes = [...day37.matchAll(/'down', '-v', '--remove-orphans'/g)].map(
    (match) => match.index,
  );
  assert.ok(upIndex > 0);
  assert.deepEqual(downIndexes.length, 1);
  assert.ok(downIndexes[0] > upIndex, 'cleanup must not delete a stack before this runner owns it');
});

test('Day 37 compose object names are invocation-owned and include RAG', () => {
  for (const service of [
    'postgres',
    'redis',
    'rabbitmq',
    'identity',
    'trip',
    'booking',
    'payment',
    'parcel',
    'gateway',
    'rag',
  ]) {
    assert.ok(
      day37Compose.includes(`container_name: \${DAY37_CONTAINER_PREFIX:-day37-e2e}-${service}`),
    );
  }
  assert.match(day37Compose, /name: \$\{DAY37_CONTAINER_PREFIX:-day37-e2e\}-net/);
});

test('Day 37 exercises PENDING_PAYMENT quota and active-plan module behavior', () => {
  assert.match(day37, /localEnvValue\('INTERNAL_JWT_SECRET'\)/);
  assert.doesNotMatch(day37, /console\.log\([^\n]*INTERNAL_JWT_SECRET/);
  assert.match(day37, /status='PENDING_PAYMENT'/);
  assert.match(day37, /ACTIVE quota uses active plan/);
  assert.match(day37, /PENDING_PAYMENT quota uses active plan/);
  assert.equal(
    (day37.match(/\{ resource: 'VEHICLES', delta: 1 \},\s*crypto\.randomUUID\(\)/g) ?? []).length,
    4,
  );
  assert.match(day37, /quotaAtCapacity[\s\S]*currentVehicles === 2/);
  assert.match(day37, /quotaExceeded\.status === 422/);
  assert.match(day37, /SUBSCRIPTION_LIMIT_EXCEEDED/);
  assert.match(day37, /quotaAfterRejection[\s\S]*currentVehicles === 2/);
  assert.match(day37, /\/v1\/operator\/policies\?page=1&pageSize=1/);
  assert.match(day37, /SUBSCRIPTION_MODULE_DISABLED/);
  assert.match(day37, /blockedParcel[\s\S]*\/v1\/parcels/);
  assert.match(day37, /blockedParcel\.status === 403/);
  assert.match(day37, /parcelCountAfter === parcelCountBefore/);
  assert.match(day37, /'DRIVER','ACTIVE','\$\{operatorId\}'/);
  assert.match(day37, /vehicle_id,driver_user_id,departure_date_time/);
  assert.doesNotMatch(
    day37,
    /vehicle_types[\s\S]{0,500}ON CONFLICT \(id\) DO UPDATE SET is_active=true, deleted_at/,
  );
  assert.doesNotMatch(
    day37,
    /INSERT INTO trips[\s\S]{0,1000}ON CONFLICT \(id\) DO UPDATE SET status='SCHEDULED', deleted_at/,
  );
});

test('Day 38 fixtures use the current active_plan_id schema', () => {
  assert.match(day38, /\(id,operator_id,active_plan_id,status/);
  assert.match(day38, /active_plan_id=EXCLUDED\.active_plan_id/);
  assert.doesNotMatch(day38, /\bprevious_active_plan_id\b/);
  assert.doesNotMatch(day38, /\bplan_id\b/);
  assert.match(day38, /POSTGRES_PORT: '55438'/);
  assert.match(day38, /RABBITMQ_PORT: '55682'/);
  assert.match(day38, /'up', '-d', '--build', 'gateway', 'notification'\], \{ env: e2eEnv \}/);
  assert.match(day38, /requestKeys\.set\(label, randomUUID\(\)\)/);
  assert.match(day38, /'Idempotency-Key': requestKey\(key\)/);
  assert.equal((day38.match(/vnp_PayDate: vnPayDate\(\)/g) ?? []).length, 2);
  assert.match(day38, /key: `day38-job-\$\{name\}-\$\{randomUUID\(\)\}`/);
});

test('Day 38 covers pending manual, zero-net, weekly exclusion, and one-winner race', () => {
  assert.match(day38, /Settlement marker timeout',\s*120_000/);
  assert.match(day38, /Manual PENDING_HOLD settlement and weekly exclusion/);
  assert.match(day38, /Manual zero-net settlement cancels without side effects/);
  assert.match(day38, /Manual versus weekly terminal race invariant/);
  assert.match(day38, /'PENDING_HOLD'/);
  assert.match(day38, /CANCELLED:ADMIN_MANUAL:true/);
  assert.match(day38, /Race duplicated settlement Outbox event/);
  assert.match(day38, /results\.length < 28/);
});

test('Day 38 distinguishes an accepted VNPay callback from an idempotent replay', () => {
  assert.match(day38, /firstIpn[\s\S]*RspCode\) === '00'/);
  assert.match(day38, /replayIpn[\s\S]*RspCode\) === '02'/);
  assert.match(day38, /phaseAReplay[\s\S]*RspCode\) === '02'/);
  assert.match(day38, /Order Already Confirmed/);
});
