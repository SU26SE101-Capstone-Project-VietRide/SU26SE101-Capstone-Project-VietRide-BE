const { randomUUID } = require('node:crypto');
const { io } = require('socket.io-client');

const baseUrl = process.env.BASE_URL ?? process.env.NOTIFICATION_URL ?? 'http://localhost:3002';
const recipientToken = process.env.RECIPIENT_TOKEN ?? process.env.ACCESS_TOKEN;
const operatorToken = process.env.OPERATOR_TOKEN;
const otherUserToken = process.env.OTHER_USER_TOKEN;
const tripId = process.env.TRIP_ID;
const socketPath = '/notification/socket.io';
const timeoutMs = 5_000;

async function main() {
  requireEnv('RECIPIENT_TOKEN or ACCESS_TOKEN', recipientToken);
  requireEnv('OPERATOR_TOKEN', operatorToken);
  requireEnv('TRIP_ID', tripId);

  await expectConnectError(undefined, 'missing token');
  await expectConnectError('not-a-jwt', 'invalid token');

  let recipientSocket;
  let otherSocket;
  try {
    recipientSocket = await connect(recipientToken);
    otherSocket = otherUserToken ? await connect(otherUserToken) : undefined;
    let leakedToOtherUser = false;
    otherSocket?.on('notification:created', () => {
      leakedToOtherUser = true;
    });

    const [event] = await Promise.all([
      waitForEvent(recipientSocket, 'notification:created'),
      createTripAnnouncement(),
    ]);
    assertUuid(event?.id, 'notification event id');
    assert(typeof event?.type === 'string' && event.type.length > 0, 'notification type is missing');
    assert(event?.userId === undefined, 'notification event leaked userId');
    assert(event?.action && typeof event.action.type === 'string', 'notification action is missing');
    assert(
      typeof event?.createdAt === 'string' && /\+07:00$/.test(event.createdAt),
      'notification createdAt is not serialized with +07:00',
    );

    await delay(200);
    if (otherSocket) assert(!leakedToOtherUser, 'notification leaked to another authenticated user');

    console.log('PASS notification realtime: auth rejection, delivery, payload, and room isolation');
  } finally {
    recipientSocket?.disconnect();
    otherSocket?.disconnect();
  }
}

async function createTripAnnouncement() {
  const response = await fetch(`${baseUrl}/v1/operator/notifications`, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${operatorToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': randomUUID(),
    },
    body: JSON.stringify({
      scope: 'TRIP',
      tripId,
      title: 'Kiểm thử thông báo thời gian thực',
      body: 'Thông báo kiểm thử tự động từ backend.',
    }),
  });
  const envelope = await response.json();
  assert(response.status === 202, `announcement returned HTTP ${response.status}`);
  assert(envelope?.success === true, 'announcement response is not a successful ApiResponse envelope');
}

function connect(token) {
  return new Promise((resolve, reject) => {
    const socket = io(baseUrl, {
      path: socketPath,
      auth: token ? { token } : {},
      transports: ['websocket'],
      reconnection: false,
      forceNew: true,
    });
    const timeout = setTimeout(() => {
      socket.disconnect();
      reject(new Error('SOCKET_CONNECT_TIMEOUT'));
    }, timeoutMs);
    socket.once('connect', () => {
      clearTimeout(timeout);
      resolve(socket);
    });
    socket.once('connect_error', (error) => {
      clearTimeout(timeout);
      socket.disconnect();
      reject(error);
    });
  });
}

async function expectConnectError(token, label) {
  try {
    const socket = await connect(token);
    socket.disconnect();
    throw new Error(`${label} unexpectedly connected`);
  } catch (error) {
    assert(error instanceof Error && error.message === 'UNAUTHORIZED', `${label} did not return UNAUTHORIZED`);
  }
}

function waitForEvent(socket, eventName) {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => reject(new Error(`${eventName}_TIMEOUT`)), timeoutMs);
    socket.once(eventName, (payload) => {
      clearTimeout(timeout);
      resolve(payload);
    });
  });
}

function assertUuid(value, label) {
  assert(
    typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value),
    `${label} is not a UUID`,
  );
}

function requireEnv(name, value) {
  if (!value) throw new Error(`${name} is required`);
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

main().catch((error) => {
  console.error(`FAIL notification realtime: ${error instanceof Error ? error.message : String(error)}`);
  process.exitCode = 1;
});
