const DEFAULT_NOTIFICATION_URL = 'http://localhost:3002';

const baseUrl =
  process.env.BASE_URL ||
  process.env.NOTIFICATION_URL ||
  DEFAULT_NOTIFICATION_URL;

const checks = [
  {
    name: 'health returns liveness payload',
    run: async () => {
      const response = await fetch(`${baseUrl}/health`);
      const body = await response.json();
      const data = unwrapSuccess(body);
      assert(response.status === 200, `expected 200, got ${response.status}`);
      assert(data.status === 'ok', 'expected status ok');
      assert(data.service === 'notification', 'expected notification service name');
    },
  },
  {
    name: 'ready returns dependency payload',
    run: async () => {
      const response = await fetch(`${baseUrl}/ready`);
      const body = await response.json();
      const data = unwrapSuccess(body);
      assert(response.status === 200, `expected 200, got ${response.status}`);
      assert(data.status === 'ok', 'expected readiness status ok');
      assert(data.dependencies?.prisma === 'ok', 'expected Prisma readiness ok');
      assert(data.dependencies?.redis === 'ok', 'expected Redis readiness ok');
      assert(data.dependencies?.rabbitmq === 'ok', 'expected RabbitMQ readiness ok');
    },
  },
  {
    name: 'notifications API rejects missing auth with ApiResponse envelope',
    run: async () => {
      const response = await fetch(`${baseUrl}/v1/notifications`);
      const body = await response.json();
      assert(response.status === 401, `expected 401, got ${response.status}`);
      assert(body.success === false, 'expected error envelope success=false');
      assert(body.error?.code === 'UNAUTHORIZED', 'expected UNAUTHORIZED code');
      assert(body.meta?.timestamp, 'expected envelope meta timestamp');
    },
  },
];

run().catch((error) => {
  console.error(`[FAIL] ${error.message}`);
  process.exit(1);
});

async function run() {
  console.log(`Notification Phase 9 smoke checks: ${baseUrl}`);
  for (const check of checks) {
    await check.run();
    console.log(`[PASS] ${check.name}`);
  }
  console.log('[PASS] Notification Phase 9 smoke checks completed');
}

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function unwrapSuccess(body) {
  assert(body.success === true, 'expected ApiResponse success=true envelope');
  assert(body.data, 'expected ApiResponse data payload');
  assert(body.meta?.timestamp, 'expected envelope meta timestamp');

  return body.data;
}
