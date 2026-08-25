import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { after, before, beforeEach, test } from 'node:test';
import { LiveGateError, runCli, runGoongLiveGate } from './goong-directions-live-gate.mjs';

const SECRET = 'goong-live-gate-secret-that-must-never-appear';
let server;
let baseUrl;
let mode;
let requestCount;
let activeRequests;
let maximumActiveRequests;

before(async () => {
  server = createServer((request, response) => {
    requestCount += 1;
    activeRequests += 1;
    maximumActiveRequests = Math.max(maximumActiveRequests, activeRequests);
    response.on('finish', () => {
      activeRequests -= 1;
    });
    const requestUrl = new URL(request.url, `http://${request.headers.host}`);
    if (request.method !== 'GET' || requestUrl.pathname !== '/Direction') {
      response.writeHead(404).end();
      return;
    }
    if (['401', '403', '429', '500'].includes(mode)) {
      response.writeHead(Number(mode)).end();
      return;
    }
    if (mode === 'timeout') {
      setTimeout(() => response.writeHead(200).end('{}'), 100);
      return;
    }
    if (mode === 'malformed') {
      response.writeHead(200, { 'content-type': 'application/json' }).end('{bad-json');
      return;
    }

    const origin = parsePoint(requestUrl.searchParams.get('origin'));
    const targets = (requestUrl.searchParams.get('destination') || '')
      .split(';')
      .filter(Boolean)
      .map(parsePoint);
    const legs = targets.map((target, index) => ({
      distance: { value: 1_000 + index },
      duration: { value: 100 + index },
      start_location: index === 0 ? origin : targets[index - 1],
      end_location: target,
    }));
    if (mode === 'wrong-count') legs.pop();
    if (mode === 'wrong-order' && legs.length >= 2) {
      [legs[0].end_location, legs[1].end_location] = [legs[1].end_location, legs[0].end_location];
    }
    response
      .writeHead(200, { 'content-type': 'application/json' })
      .end(JSON.stringify({ routes: [{ legs }] }));
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});

beforeEach(() => {
  mode = 'success';
  requestCount = 0;
  activeRequests = 0;
  maximumActiveRequests = 0;
});

after(async () => {
  server.closeAllConnections();
  await new Promise((resolve, reject) =>
    server.close((error) => (error ? reject(error) : resolve())),
  );
});

test('runs bounded chunks sequentially and validates the complete ordered chain', async () => {
  const report = await runGoongLiveGate({
    fixture: createFixture(12),
    apiKey: SECRET,
    baseUrl,
    maxDestinations: 5,
    minimumRoutes: 1,
    minimumMultipointRoutes: 1,
    timeoutMs: 500,
  });

  assert.equal(requestCount, 3);
  assert.equal(maximumActiveRequests, 1);
  assert.equal(report.requests, 3);
  assert.equal(report.legs, 11);
  assert.equal(report.routes, 1);
  assert.equal(report.multipointRoutes, 1);
  assert.ok(report.p95Ms < report.timeoutMs);
});

test('rejects 401, 403, 429 and 5xx responses', async (context) => {
  for (const status of ['401', '403', '429', '500']) {
    await context.test(status, async () => {
      mode = status;
      await assert.rejects(runGate(), errorWithCode('GOONG_HTTP_STATUS_INVALID'));
    });
  }
});

test('rejects timeout, malformed JSON, wrong leg count and wrong endpoint order', async (context) => {
  for (const [responseMode, code, timeoutMs] of [
    ['timeout', 'GOONG_REQUEST_TIMEOUT', 10],
    ['malformed', 'GOONG_RESPONSE_MALFORMED', 500],
    ['wrong-count', 'GOONG_LEG_COUNT_INVALID', 500],
    ['wrong-order', 'GOONG_ENDPOINT_ORDER_INVALID', 500],
  ]) {
    await context.test(responseMode, async () => {
      mode = responseMode;
      await assert.rejects(runGate(timeoutMs), errorWithCode(code));
    });
  }
});

test('fails before fixture access when the key is absent', async () => {
  let fixtureRead = false;
  const errors = [];
  const exitCode = await runCli({
    argv: commandArguments(),
    env: {},
    stderr: (line) => errors.push(line),
    readFixture: () => {
      fixtureRead = true;
      return createFixture(2);
    },
  });

  assert.equal(exitCode, 1);
  assert.equal(fixtureRead, false);
  assert.deepEqual(errors, ['GOONG_LIVE_GATE=FAIL code=GOONG_API_KEY_MISSING']);
});

test('redacts key, query and full URL from failure output', async () => {
  mode = '401';
  const output = [];
  const exitCode = await runCli({
    argv: commandArguments(),
    env: {
      GOONG_API_KEY: SECRET,
      GOONG_BASE_URL: baseUrl,
      GOONG_MAX_DESTINATIONS_PER_REQUEST: '10',
    },
    stdout: (line) => output.push(line),
    stderr: (line) => output.push(line),
    readFixture: () => createFixture(11),
  });
  const joined = output.join('\n');

  assert.equal(exitCode, 1);
  assert.doesNotMatch(joined, new RegExp(SECRET));
  assert.doesNotMatch(joined, new RegExp(baseUrl.replaceAll('.', '\\.')));
  assert.doesNotMatch(joined, /api_key|origin=|destination=|\/Direction/i);
  assert.match(joined, /code=GOONG_HTTP_STATUS_INVALID/);
});

function runGate(timeoutMs = 500) {
  return runGoongLiveGate({
    fixture: createFixture(11),
    apiKey: SECRET,
    baseUrl,
    maxDestinations: 10,
    minimumRoutes: 1,
    minimumMultipointRoutes: 1,
    timeoutMs,
  });
}

function commandArguments() {
  return [
    '--fixture',
    'not-read-by-self-test.json',
    '--minimum-routes',
    '1',
    '--minimum-multipoint-routes',
    '1',
    '--timeout-ms',
    '500',
  ];
}

function createFixture(pointCount) {
  return {
    routes: [
      {
        name: 'Tuyến kiểm thử Việt Nam',
        points: Array.from({ length: pointCount }, (_, index) => ({
          lat: 10.75 + index * 0.005,
          lng: 106.65 + index * 0.005,
        })),
      },
    ],
  };
}

function parsePoint(value) {
  const [lat, lng] = String(value || '')
    .split(',')
    .map(Number);
  return { lat, lng };
}

function errorWithCode(code) {
  return (error) => error instanceof LiveGateError && error.code === code;
}
