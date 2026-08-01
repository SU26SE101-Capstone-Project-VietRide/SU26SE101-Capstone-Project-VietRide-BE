import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import test from 'node:test';
import { day36IdempotencyKey } from './day36-idempotency-keys.mjs';

const uuidV4Pattern = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/;
const helperUrl = new URL('./day36-idempotency-keys.mjs', import.meta.url).href;

function keyFromFreshProcess(label) {
  const script = `
    const { day36IdempotencyKey } = await import(${JSON.stringify(helperUrl)});
    console.log(day36IdempotencyKey(${JSON.stringify(label)}));
  `;
  const result = spawnSync(process.execPath, ['--input-type=module', '--eval', script], {
    encoding: 'utf8',
    windowsHide: true,
  });

  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}

test('returns a stable UUID-v4 for the same label', () => {
  const first = day36IdempotencyKey('day36-dispatch-1');
  const replay = day36IdempotencyKey('day36-dispatch-1');

  assert.match(first, uuidV4Pattern);
  assert.equal(replay, first);
});

test('returns different UUID-v4 keys for different labels', () => {
  const first = day36IdempotencyKey('day36-booking-1');
  const second = day36IdempotencyKey('day36-booking-2');

  assert.match(first, uuidV4Pattern);
  assert.match(second, uuidV4Pattern);
  assert.notEqual(second, first);
});

test('returns a fresh UUID-v4 from each fresh process', () => {
  const first = keyFromFreshProcess('day36-dispatch-1');
  const second = keyFromFreshProcess('day36-dispatch-1');

  assert.match(first, uuidV4Pattern);
  assert.match(second, uuidV4Pattern);
  assert.notEqual(second, first);
});
