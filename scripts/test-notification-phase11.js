const DEFAULT_NOTIFICATION_URL = 'http://localhost:3002';

const baseUrl = process.env.BASE_URL || process.env.NOTIFICATION_URL || DEFAULT_NOTIFICATION_URL;
const accessToken = process.env.ACCESS_TOKEN || process.env.PASSENGER_TOKEN;
const authorization = accessToken
  ? accessToken.startsWith('Bearer ')
    ? accessToken
    : `Bearer ${accessToken}`
  : undefined;
const actionTypes = new Set([
  'OPEN_BOOKING_DETAIL',
  'OPEN_CREW_TRIP_BOOKING',
  'OPEN_TRIP_DETAIL',
  'OPEN_TRIP_TRACKING',
  'OPEN_PARCEL_DETAIL',
  'OPEN_WALLET',
  'OPEN_SUBSCRIPTION',
  'OPEN_SHUTTLE_TRACKING',
  'NONE',
]);

const checks = [
  {
    name: 'health returns the notification liveness payload',
    run: async () => {
      const response = await fetch(`${baseUrl}/health`);
      const data = unwrapSuccess(response, await response.json());
      assert(response.status === 200, `expected 200, got ${response.status}`);
      assert(data.status === 'ok', 'expected health status ok');
      assert(data.service === 'notification', 'expected notification service name');
    },
  },
  {
    name: 'notifications API rejects missing auth',
    run: async () => {
      const response = await fetch(`${baseUrl}/v1/notifications`);
      const body = await response.json();
      assert(response.status === 401, `expected 401, got ${response.status}`);
      assert(body.success === false, 'expected error envelope success=false');
      assert(body.error?.code === 'UNAUTHORIZED', 'expected UNAUTHORIZED code');
    },
  },
  {
    name: 'notifications API rejects an invalid query',
    run: async () => {
      requireAccessToken();
      const response = await fetch(`${baseUrl}/v1/notifications?pageSize=101`, {
        headers: { Authorization: authorization },
      });
      const body = await response.json();
      assert(response.status === 400, `expected 400, got ${response.status}`);
      assert(body.success === false, 'expected error envelope success=false');
      assert(body.error?.code === 'VALIDATION_FAILED', 'expected VALIDATION_FAILED code');
    },
  },
  {
    name: 'authenticated inbox items expose semantic actions',
    run: async () => {
      requireAccessToken();
      const response = await fetch(`${baseUrl}/v1/notifications?page=1&pageSize=20`, {
        headers: { Authorization: authorization },
      });
      const data = unwrapSuccess(response, await response.json());
      assert(response.status === 200, `expected 200, got ${response.status}`);
      assert(Array.isArray(data.items), 'expected paged notification items');
      assert(data.items.length > 0, 'expected the test account to have at least one notification');
      for (const item of data.items) {
        assert(actionTypes.has(item.action?.type), `unexpected action type ${item.action?.type}`);
        assert(isPlainObject(item.action?.params), 'expected action params object');
      }
    },
  },
];

run().catch((error) => {
  console.error(`[FAIL] ${error.message}`);
  process.exit(1);
});

async function run() {
  console.log(`Notification Phase 11 smoke checks: ${baseUrl}`);
  for (const check of checks) {
    await check.run();
    console.log(`[PASS] ${check.name}`);
  }
  console.log('[PASS] Notification Phase 11 smoke checks completed');
}

function requireAccessToken() {
  assert(accessToken, 'ACCESS_TOKEN or PASSENGER_TOKEN is required');
}

function unwrapSuccess(response, body) {
  assert(response.status >= 200 && response.status < 300, `expected success, got ${response.status}`);
  assert(body.success === true, 'expected ApiResponse success=true envelope');
  assert(body.data, 'expected ApiResponse data payload');
  assert(body.meta?.timestamp, 'expected envelope meta timestamp');
  return body.data;
}

function isPlainObject(value) {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}
