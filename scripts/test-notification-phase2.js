const DEFAULT_BASE_URL = 'http://localhost:3002';
const TEST_NOTIFICATION_ID = '11111111-1111-4111-8111-111111111111';

const baseUrl = (process.env.BASE_URL ?? process.env.NOTIFICATION_URL ?? DEFAULT_BASE_URL).replace(/\/$/, '');
const accessToken = process.env.ACCESS_TOKEN;
const notificationId = process.env.NOTIFICATION_ID ?? TEST_NOTIFICATION_ID;

async function main() {
  const failures = [];

  await runCase('auth fail: list requires bearer token', failures, async () => {
    const response = await request('/v1/notifications');
    assertStatus(response, 401);
    assertErrorEnvelope(await response.json(), 'UNAUTHORIZED');
  });

  await runCase('validation fail: pageSize must be <= 100', failures, async () => {
    const response = await request('/v1/notifications?pageSize=101', accessToken);
    assertStatus(response, 400);
    assertErrorEnvelope(await response.json());
  });

  await runCase('happy path: list notifications with bearer token', failures, async () => {
    requireAccessToken();
    const response = await request('/v1/notifications?page=1&pageSize=20', accessToken);
    assertStatus(response, 200);
    const body = await response.json();
    assertSuccessEnvelope(body);
    if (!Array.isArray(body.data?.items)) {
      throw new Error('Expected data.items to be an array');
    }
  });

  await runCase('happy path: mark notification read', failures, async () => {
    requireAccessToken();
    const response = await request(`/v1/notifications/${notificationId}/read`, accessToken, {
      method: 'POST',
    });
    assertStatus(response, 204);
  });

  if (failures.length > 0) {
    for (const failure of failures) {
      process.stderr.write(`FAIL ${failure.name}: ${failure.message}\n`);
    }
    process.exit(1);
  }

  process.stdout.write('PASS notification phase 2 script verify\n');
}

async function runCase(name, failures, fn) {
  try {
    await fn();
    process.stdout.write(`PASS ${name}\n`);
  } catch (error) {
    failures.push({ name, message: error instanceof Error ? error.message : String(error) });
  }
}

async function request(path, token, init = {}) {
  return fetch(`${baseUrl}${path}`, {
    ...init,
    headers: {
      ...(init.headers ?? {}),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
  });
}

function assertStatus(response, expectedStatus) {
  if (response.status !== expectedStatus) {
    throw new Error(`Expected HTTP ${expectedStatus}, got ${response.status}`);
  }
}

function assertSuccessEnvelope(body) {
  if (body?.success !== true || typeof body.statusCode !== 'number' || body.data === undefined) {
    throw new Error('Expected ApiResponse success envelope');
  }
}

function assertErrorEnvelope(body, expectedCode) {
  if (body?.success !== false || typeof body.statusCode !== 'number' || !body.error?.code) {
    throw new Error('Expected ApiResponse error envelope');
  }
  if (expectedCode && body.error.code !== expectedCode) {
    throw new Error(`Expected error code ${expectedCode}, got ${body.error.code}`);
  }
}

function requireAccessToken() {
  if (!accessToken) {
    throw new Error('ACCESS_TOKEN is required for this case');
  }
}

main().catch((error) => {
  process.stderr.write(`${error instanceof Error ? error.stack : String(error)}\n`);
  process.exit(1);
});
