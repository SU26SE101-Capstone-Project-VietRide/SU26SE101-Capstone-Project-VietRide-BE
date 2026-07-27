import { execFileSync } from 'node:child_process';
import { connect } from 'amqplib';
import { randomUUID } from 'node:crypto';
import { exportJWK, generateKeyPair, SignJWT } from 'jose';

const root = process.cwd();
const noBuild =
  process.argv.includes('--no-build') || process.env.NOTIFICATION_E2E_SKIP_BUILD === '1';
const e2eKeyId = 'notification-v1-e2e';
const e2eKeyPair = await generateKeyPair('RS256');
const e2ePublicJwk = await exportJWK(e2eKeyPair.publicKey);
e2ePublicJwk.kid = e2eKeyId;
e2ePublicJwk.alg = 'RS256';
e2ePublicJwk.use = 'sig';
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.notification-idempotency-e2e.yml',
  '--profile',
  'app',
];
const env = {
  ...process.env,
  POSTGRES_PORT: '59437',
  PGBOUNCER_PORT: '59438',
  REDIS_PORT: '59382',
  RABBITMQ_PORT: '59682',
  RABBITMQ_MGMT_PORT: '59683',
  GATEWAY_PORT: '59020',
  NOTIFICATION_PORT: '59022',
  POSTGRES_USER: process.env.POSTGRES_USER ?? 'vietride',
  POSTGRES_PASSWORD: process.env.POSTGRES_PASSWORD ?? 'vietride_dev',
  RABBITMQ_USER: process.env.RABBITMQ_USER ?? 'vietride',
  RABBITMQ_PASSWORD: process.env.RABBITMQ_PASSWORD ?? 'vietride_dev',
  INTERNAL_JWT_SECRET:
    process.env.INTERNAL_JWT_SECRET ?? 'notification-idempotency-e2e-secret-32-bytes',
  NOTIFICATION_E2E_JWKS_JSON: JSON.stringify({ keys: [e2ePublicJwk] }),
};
const containers = {
  gateway: 'notification-idem-e2e-gateway',
  notification: 'notification-idem-e2e-service',
  postgres: 'notification-idem-e2e-postgres',
  rabbitmq: 'notification-idem-e2e-rabbitmq',
  redis: 'notification-idem-e2e-redis',
};
const routingKey = 'payment.invoice.issued';
const queueName = 'notification:invoice-issued';
const userId = '11111111-1111-4111-8111-111111111111';
const operatorId = '22222222-2222-4222-8222-222222222222';
const invoiceId = '33333333-3333-4333-8333-333333333333';
const systemAdminId = '44444444-4444-4444-8444-444444444444';
const driverId = '55555555-5555-4555-8555-555555555555';
const assistantId = '66666666-6666-4666-8666-666666666666';
const passengerId = '77777777-7777-4777-8777-777777777777';
const tripId = '88888888-8888-4888-8888-888888888888';
const bookingId = '99999999-9999-4999-8999-999999999999';
const parcelId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const senderId = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
const recipientId = 'cccccccc-cccc-4ccc-8ccc-cccccccccccc';
const stopId = 'dddddddd-dddd-4ddd-8ddd-dddddddddddd';
const alternativeRouteId = 'eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee';
const stationId = 'ffffffff-ffff-4fff-8fff-ffffffffffff';
const unknownOperatorId = '12345678-1234-4234-8234-123456789012';
const messageId = randomUUID();

function run(command, args, options = {}) {
  const output = execFileSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...env, ...options.env },
    stdio: options.stdio ?? ['ignore', 'pipe', 'pipe'],
    maxBuffer: 32 * 1024 * 1024,
  });
  return output?.trim() ?? '';
}

function composeRun(args, options = {}) {
  return run('docker', [...compose, ...args], options);
}

function psql(sql) {
  return run('docker', [
    'exec',
    containers.postgres,
    'psql',
    '-X',
    '-qAt',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    env.POSTGRES_USER,
    '-d',
    'vietride_notification',
    '-c',
    sql,
  ]);
}

function redis(...args) {
  return run('docker', ['exec', containers.redis, 'redis-cli', '--raw', ...args]);
}

function rabbitQueueCount(name) {
  const rows = run('docker', [
    'exec',
    containers.rabbitmq,
    'rabbitmqctl',
    'list_queues',
    '-q',
    'name',
    'messages_ready',
  ]);
  const row = rows.split(/\r?\n/u).find((line) => line.startsWith(`${name}\t`));
  return row ? Number(row.split('\t')[1]) : 0;
}

async function waitFor(label, predicate, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      if (await predicate()) return;
    } catch (error) {
      lastError = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${label} timed out${lastError ? `: ${String(lastError)}` : ''}`);
}

async function publish(payload, publishedRoutingKey = routingKey, publishedMessageId = messageId) {
  const connection = await connect(
    `amqp://${env.RABBITMQ_USER}:${env.RABBITMQ_PASSWORD}@127.0.0.1:${env.RABBITMQ_PORT}`,
  );
  try {
    const channel = await connection.createConfirmChannel();
    await channel.assertExchange('vietride.events', 'topic', { durable: true });
    channel.publish('vietride.events', publishedRoutingKey, Buffer.from(JSON.stringify(payload)), {
      contentType: 'application/json',
      messageId: publishedMessageId,
      persistent: true,
    });
    await channel.waitForConfirms();
    await channel.close();
  } finally {
    await connection.close();
  }
}

function notificationRecipients(publishedRoutingKey, publishedMessageId) {
  return psql(`
    SELECT coalesce(string_agg(user_id::text || ':' || type::text, ',' ORDER BY user_id::text, type::text), '')
    FROM vietride_notification.notifications
    WHERE dedupe_key LIKE '${publishedRoutingKey}:${publishedMessageId}:%';
  `);
}

function processedMessageCount(publishedRoutingKey, publishedMessageId) {
  return Number(
    psql(`
      SELECT count(*)
      FROM vietride_notification.processed_messages
      WHERE consumer_name='${publishedRoutingKey}' AND message_id='${publishedMessageId}';
    `),
  );
}

async function waitForNotificationRecipients(
  label,
  publishedRoutingKey,
  publishedMessageId,
  expectedRecipients,
) {
  const expected = [...expectedRecipients].sort().join(',');
  await waitFor(label, () => {
    const actual = notificationRecipients(publishedRoutingKey, publishedMessageId);
    return actual === expected && processedMessageCount(publishedRoutingKey, publishedMessageId) === 1;
  });
}

async function makeAccessToken(subject, role) {
  return new SignJWT({ role, email: `${subject}@notification-e2e.local`, hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', typ: 'JWT', kid: e2eKeyId })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(subject)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(e2eKeyPair.privateKey);
}

async function gatewayGet(pathname, accessToken) {
  const response = await fetch(`http://127.0.0.1:${env.GATEWAY_PORT}${pathname}`, {
    headers: { authorization: `Bearer ${accessToken}` },
  });
  const body = await response.json();
  if (!response.ok) {
    throw new Error(`Gateway GET ${pathname} failed (${response.status}): ${JSON.stringify(body)}`);
  }
  return body;
}

async function rabbitMqReady() {
  try {
    const connection = await connect(
      `amqp://${env.RABBITMQ_USER}:${env.RABBITMQ_PASSWORD}@127.0.0.1:${env.RABBITMQ_PORT}`,
    );
    await connection.close();
    return true;
  } catch {
    return false;
  }
}

async function gatewayReady() {
  try {
    const response = await fetch(`http://127.0.0.1:${env.GATEWAY_PORT}/health`);
    return response.ok;
  } catch {
    return false;
  }
}

function eventPayload(amount = '1200000') {
  return {
    eventId: messageId,
    invoiceId,
    invoiceNumber: 'VR-E2E-CRASH-001',
    operatorId,
    amount,
    invoiceWebUrl: `https://operator.e2e.local/invoices/${invoiceId}`,
    downloadApiUrl: `https://api.e2e.local/v1/operator/invoices/${invoiceId}/download`,
  };
}

function sideEffectSnapshot() {
  return psql(`
    SELECT concat_ws('|',
      (SELECT count(*) FROM vietride_notification.notifications WHERE dedupe_key = '${routingKey}:${messageId}:${userId}:INVOICE_ISSUED'),
      (SELECT count(*) FROM vietride_notification.notification_deliveries d JOIN vietride_notification.notifications n ON n.id=d.notification_id WHERE n.dedupe_key = '${routingKey}:${messageId}:${userId}:INVOICE_ISSUED' AND d.status='SENT'),
      (SELECT count(*) FROM vietride_notification.email_deliveries WHERE dedupe_key = '${routingKey}:${messageId}:${userId}:email' AND status='SENT'),
      (SELECT count(*) FROM vietride_notification.processed_messages WHERE consumer_name='${routingKey}' AND message_id='${messageId}')
    );
  `);
}

async function runV1AcceptanceMatrix() {
  const occurredAt = new Date().toISOString();

  const routeChangedEventId = randomUUID();
  await publish(
    {
      eventId: routeChangedEventId,
      occurredAt,
      tripId,
      operatorId,
      tripStatus: 'IN_PROGRESS',
      alternativeRouteId,
      affectedBookings: [
        {
          bookingId,
          candidateStops: [
            {
              stopId,
              stationId: null,
              stationName: 'Điểm dừng thay thế',
              sequence: 1,
              estimatedArrivalAt: occurredAt,
            },
            {
              stopId: null,
              stationId,
              stationName: 'Bến đích',
              sequence: 2,
              estimatedArrivalAt: occurredAt,
            },
          ],
        },
      ],
    },
    'trip.trip.route_changed',
    routeChangedEventId,
  );
  await waitForNotificationRecipients(
    'route-change crew and affected-passenger fan-out',
    'trip.trip.route_changed',
    routeChangedEventId,
    [
      `${driverId}:TRIP_ROUTE_CHANGED`,
      `${assistantId}:TRIP_ROUTE_CHANGED`,
      `${passengerId}:TRIP_ROUTE_CHANGED`,
    ],
  );

  const delayedEventId = randomUUID();
  await publish(
    {
      eventId: delayedEventId,
      occurredAt,
      tripId,
      stopId,
      stopName: 'Bến trung tâm',
      delayMinutes: 25,
      etaNew: occurredAt,
    },
    'trip.trip.delayed',
    delayedEventId,
  );
  await waitForNotificationRecipients(
    'delay passenger and operator-admin fan-out',
    'trip.trip.delayed',
    delayedEventId,
    [`${passengerId}:TRIP_DELAYED`, `${userId}:TRIP_DELAYED`],
  );
  console.log('PASS | passenger route/delay and crew/operator fan-out policies');

  const reviewRequestedEventId = randomUUID();
  await publish(
    {
      eventId: reviewRequestedEventId,
      occurredAt,
      parcelId,
      reviewReason: 'Cần duyệt khối lượng thực tế',
    },
    'parcel.parcel.review_requested',
    reviewRequestedEventId,
  );
  await waitForNotificationRecipients(
    'parcel review request operator fan-out',
    'parcel.parcel.review_requested',
    reviewRequestedEventId,
    [`${userId}:PARCEL_REVIEW_REQUESTED`],
  );

  const reviewTimeoutEventId = randomUUID();
  await publish(
    {
      eventId: reviewTimeoutEventId,
      occurredAt,
      parcelId,
      reason: 'Quá hạn duyệt đơn gửi hàng',
    },
    'parcel.parcel.cancelled',
    reviewTimeoutEventId,
  );
  await waitForNotificationRecipients(
    'parcel review-timeout sender policy',
    'parcel.parcel.cancelled',
    reviewTimeoutEventId,
    [`${senderId}:PARCEL_REJECTED`],
  );

  const settlementTimeoutEventId = randomUUID();
  await publish(
    {
      eventId: settlementTimeoutEventId,
      occurredAt,
      parcelId,
      parcelCode: 'PRC-E2E-001',
      operatorId,
      userId: senderId,
      tripId,
      reason: 'FINAL_PAYMENT_TIMEOUT',
      forfeitedDepositVnd: 50000,
      refundAmount: 0,
    },
    'parcel.parcel.auto_rejected',
    settlementTimeoutEventId,
  );
  await waitForNotificationRecipients(
    'parcel settlement-timeout sender policy',
    'parcel.parcel.auto_rejected',
    settlementTimeoutEventId,
    [`${senderId}:PARCEL_REJECTED`],
  );

  const settlementRecoveredEventId = randomUUID();
  await publish(
    {
      eventId: settlementRecoveredEventId,
      occurredAt,
      parcelId,
      parcelCode: 'PRC-E2E-001',
      userId: senderId,
      tripId,
      recoveredStatus: 'READY_TO_LOAD',
      refundAmountVnd: 0,
    },
    'parcel.parcel.settlement_recovered',
    settlementRecoveredEventId,
  );
  await waitForNotificationRecipients(
    'parcel settlement recovery sender policy',
    'parcel.parcel.settlement_recovered',
    settlementRecoveredEventId,
    [`${senderId}:PARCEL_SETTLEMENT_RECOVERED`],
  );
  console.log('PASS | Parcel review, timeout and settlement recovery policies');

  const registrationEventId = randomUUID();
  await publish(
    {
      eventId: registrationEventId,
      occurredAt,
      operatorId,
      companyName: 'Nhà xe Thử nghiệm Việt',
    },
    'identity.operator.registration_submitted',
    registrationEventId,
  );
  await waitForNotificationRecipients(
    'operator registration system-admin recipient',
    'identity.operator.registration_submitted',
    registrationEventId,
    [`${systemAdminId}:OPERATOR_REGISTRATION_SUBMITTED`],
  );

  const usageWarningEventId = randomUUID();
  await publish(
    {
      eventId: usageWarningEventId,
      occurredAt,
      subscriptionId: randomUUID(),
      operatorId,
      resource: 'DRIVERS',
      periodKey: '2026-07',
      used: 8,
      limit: 10,
      usagePercent: 80,
    },
    'identity.subscription.usage_warning',
    usageWarningEventId,
  );
  await waitForNotificationRecipients(
    'subscription warning operator-admin recipient',
    'identity.subscription.usage_warning',
    usageWarningEventId,
    [`${userId}:SUBSCRIPTION_USAGE_WARNING`],
  );

  const voucherEventId = randomUUID();
  await publish(
    {
      eventId: voucherEventId,
      occurredAt,
      voucherId: randomUUID(),
      operatorId,
      voucherCode: 'VIETRIDE26',
      voucherType: 'PERCENT_OFF',
      voucherValue: 15,
    },
    'booking.voucher.consent_requested',
    voucherEventId,
  );
  await waitForNotificationRecipients(
    'voucher consent operator-admin recipient',
    'booking.voucher.consent_requested',
    voucherEventId,
    [`${userId}:VOUCHER_CONSENT_REQUESTED`],
  );

  const walletEventId = randomUUID();
  const recoveryNotificationId = randomUUID();
  psql(`
    INSERT INTO vietride_notification.notifications
      (id, user_id, type, title, body, data, dedupe_key)
    VALUES
      ('${recoveryNotificationId}', '${passengerId}', 'WALLET_DEBITED',
       'Ví đã bị trừ tiền', 'Ví VietRide của bạn vừa bị trừ 25000 VND.', '{}',
       'payment.wallet.debited:${walletEventId}:${passengerId}:WALLET_DEBITED');
  `);
  await publish(
    {
      eventId: walletEventId,
      occurredAt,
      userId: passengerId,
      walletTransactionId: randomUUID(),
      amount: 25000,
      balanceAfter: 75000,
      referenceType: 'BOOKING',
      referenceId: bookingId,
    },
    'payment.wallet.debited',
    walletEventId,
  );
  await waitFor('DB-to-queue replay recovery', () => {
    const snapshot = psql(`
      SELECT concat_ws('|',
        (SELECT count(*) FROM vietride_notification.notifications WHERE dedupe_key='payment.wallet.debited:${walletEventId}:${passengerId}:WALLET_DEBITED'),
        (SELECT count(*) FROM vietride_notification.notification_deliveries WHERE notification_id='${recoveryNotificationId}' AND status='SENT'),
        (SELECT count(*) FROM vietride_notification.processed_messages WHERE consumer_name='payment.wallet.debited' AND message_id='${walletEventId}')
      );
    `);
    return snapshot === '1|1|1';
  });
  if (redis('ZSCORE', 'notification:fcm-push:completed', recoveryNotificationId) === '') {
    throw new Error('DB-to-queue recovery did not retain the deterministic FCM job');
  }
  console.log('PASS | new producer facts and DB-to-queue replay recovery');

  const dlqEventId = randomUUID();
  const dlqQueue = 'notification:booking-voucher-consent-requested.dlq';
  if (rabbitQueueCount(dlqQueue) !== 0) throw new Error(`${dlqQueue} was not empty before test`);
  await publish(
    {
      eventId: dlqEventId,
      occurredAt,
      voucherId: randomUUID(),
      operatorId: unknownOperatorId,
      voucherCode: 'RETRY26',
      voucherType: 'FIXED_AMOUNT',
      voucherValue: 50000,
    },
    'booking.voucher.consent_requested',
    dlqEventId,
  );
  await waitFor('bounded RabbitMQ retry to DLQ', () => rabbitQueueCount(dlqQueue) === 1, 90_000);
  if (
    notificationRecipients('booking.voucher.consent_requested', dlqEventId) !== '' ||
    processedMessageCount('booking.voucher.consent_requested', dlqEventId) !== 0
  ) {
    throw new Error('Transient recipient failure produced a side effect before DLQ');
  }
  console.log('PASS | transient dependency failure exhausted bounded retry into DLQ');

  const unicodeNotificationId = psql(`
    SELECT id
    FROM vietride_notification.notifications
    WHERE dedupe_key='parcel.parcel.settlement_recovered:${settlementRecoveredEventId}:${senderId}:PARCEL_SETTLEMENT_RECOVERED';
  `);
  const expectedUnicode = psql(`
    SELECT title || '|' || body
    FROM vietride_notification.notifications
    WHERE id='${unicodeNotificationId}';
  `);
  const senderToken = await makeAccessToken(senderId, 'PASSENGER');
  const response = await gatewayGet('/v1/notifications?page=1&pageSize=100', senderToken);
  const items = response?.data?.items;
  const notification = Array.isArray(items)
    ? items.find((item) => item.id === unicodeNotificationId)
    : undefined;
  if (!notification || `${notification.title}|${notification.body}` !== expectedUnicode) {
    throw new Error('Gateway did not preserve persisted Notification Unicode');
  }
  console.log('PASS | Gateway preserved persisted Vietnamese Unicode byte-for-byte');
}

let failed;
try {
  composeRun(['down', '-v', '--remove-orphans']);
  if (!noBuild) {
    composeRun(['--parallel', '1', 'build', 'notification', 'gateway'], { stdio: 'inherit' });
  }
  composeRun(['up', '-d', '--wait', 'postgres', 'redis', 'rabbitmq', 'identity-mock'], {
    stdio: 'inherit',
  });
  await waitFor(
    'PostgreSQL notification database initialization',
    () => psql('SELECT 1;') === '1',
    90_000,
  );
  await waitFor('RabbitMQ AMQP listener initialization', rabbitMqReady, 90_000);
  composeRun(['up', '-d', '--no-deps', '--wait', 'notification'], { stdio: 'inherit' });
  console.log('PASS | isolated PostgreSQL/Redis/RabbitMQ/Notification stack healthy');

  await waitFor('RabbitMQ invoice queue binding', () => rabbitQueueCount(queueName) === 0);
  psql(`
    CREATE SEQUENCE vietride_notification.e2e_processed_message_gate_seq START 1;
    CREATE OR REPLACE FUNCTION vietride_notification.e2e_pause_first_processed_message()
    RETURNS trigger LANGUAGE plpgsql AS $e2e$
    BEGIN
      IF nextval('vietride_notification.e2e_processed_message_gate_seq') = 1 THEN
        PERFORM pg_sleep(60);
      END IF;
      RETURN NEW;
    END;
    $e2e$;
    CREATE TRIGGER e2e_pause_first_processed_message
      BEFORE INSERT ON vietride_notification.processed_messages
      FOR EACH ROW EXECUTE FUNCTION vietride_notification.e2e_pause_first_processed_message();
  `);

  await publish(eventPayload());
  try {
    await waitFor(
      'push and email sent while processed marker is paused',
      () => sideEffectSnapshot() === '1|1|1|0',
      90_000,
    );
  } catch (error) {
    throw new Error(
      `Crash-window side effects did not converge; snapshot=${sideEffectSnapshot()}`,
      { cause: error },
    );
  }
  await waitFor(
    'processed marker trigger entered',
    () =>
      psql('SELECT last_value FROM vietride_notification.e2e_processed_message_gate_seq;') === '1',
  );

  run('docker', ['kill', containers.notification]);
  if (sideEffectSnapshot() !== '1|1|1|0') {
    throw new Error(`Crash window was not established: ${sideEffectSnapshot()}`);
  }
  console.log(
    'PASS | process killed after DB/BullMQ/provider side effects and before marker commit',
  );

  const processingKey = `notification:idem:processing:${routingKey}:${messageId}`;
  if (redis('EXISTS', processingKey) !== '1') {
    throw new Error('Crash did not leave the owner processing lock for TTL recovery');
  }
  redis('EXPIRE', processingKey, '1');
  await waitFor('orphan lock TTL expiry', () => redis('EXISTS', processingKey) === '0');

  composeRun(['up', '-d', '--no-build', '--wait', 'notification'], { stdio: 'inherit' });
  await waitFor('redelivery durable marker', () => sideEffectSnapshot() === '1|1|1|1', 60_000);
  const markerAttempts = Number(
    psql('SELECT last_value FROM vietride_notification.e2e_processed_message_gate_seq;'),
  );
  if (markerAttempts < 2) {
    throw new Error('RabbitMQ did not redeliver the interrupted message');
  }

  const notificationId = psql(
    `SELECT id FROM vietride_notification.notifications WHERE dedupe_key='${routingKey}:${messageId}:${userId}:INVOICE_ISSUED';`,
  );
  const emailDeliveryId = psql(
    `SELECT id FROM vietride_notification.email_deliveries WHERE dedupe_key='${routingKey}:${messageId}:${userId}:email';`,
  );
  if (
    redis('ZSCORE', 'notification:fcm-push:completed', notificationId) === '' ||
    redis('ZSCORE', 'notification:email-send:completed', emailDeliveryId) === ''
  ) {
    throw new Error('Completed deterministic BullMQ jobs were not retained for replay dedupe');
  }
  console.log(
    'PASS | crash retry kept one notification, one push delivery, one email and one job each',
  );

  const sequenceBeforeReplay = psql(
    'SELECT last_value FROM vietride_notification.e2e_processed_message_gate_seq;',
  );
  await publish(eventPayload());
  await new Promise((resolve) => setTimeout(resolve, 1_500));
  if (
    sideEffectSnapshot() !== '1|1|1|1' ||
    psql('SELECT last_value FROM vietride_notification.e2e_processed_message_gate_seq;') !==
      sequenceBeforeReplay
  ) {
    throw new Error('Exact duplicate re-executed notification side effects');
  }
  console.log('PASS | same MessageId and same payload is a durable no-op');

  await publish(eventPayload('1300000'));
  await waitFor(
    'payload mismatch routed to bounded retry',
    () => rabbitQueueCount(`${queueName}.retry`) === 1,
    15_000,
  );
  const logs = composeRun(['logs', '--no-color', '--tail', '120', 'notification']);
  if (!logs.includes(`MESSAGE_PAYLOAD_MISMATCH_${routingKey}_${messageId}`)) {
    throw new Error('Same MessageId with different payload was not reported as mismatch');
  }
  if (sideEffectSnapshot() !== '1|1|1|1') {
    throw new Error('Mismatched payload changed durable side effects');
  }
  console.log('PASS | same MessageId with different payload is rejected before side effects');

  run(
    process.execPath,
    [
      'node_modules/jest/bin/jest.js',
      '--config',
      'apps/notification/jest.e2e.config.cts',
      '--runInBand',
      'apps/notification/src/notifications/message-idempotency.system.e2e-spec.ts',
    ],
    {
      stdio: 'inherit',
      env: {
        NOTIFICATION_IDEMPOTENCY_SYSTEM_E2E: '1',
        DATABASE_URL: `postgresql://${env.POSTGRES_USER}:${env.POSTGRES_PASSWORD}@127.0.0.1:${env.POSTGRES_PORT}/vietride_notification`,
        NOTIFICATION_DATABASE_URL: `postgresql://${env.POSTGRES_USER}:${env.POSTGRES_PASSWORD}@127.0.0.1:${env.POSTGRES_PORT}/vietride_notification`,
        REDIS_URL: `redis://127.0.0.1:${env.REDIS_PORT}`,
      },
    },
  );
  console.log('PASS | real owner-token compare-delete and durable payload hash checks');

  composeRun(['up', '-d', '--no-deps', '--wait', 'gateway'], { stdio: 'inherit' });
  await waitFor('Gateway health', gatewayReady, 90_000);
  console.log('PASS | isolated Gateway and JWKS dependency fixture healthy');
  await runV1AcceptanceMatrix();
  console.log('PASS | Notification v1 selective real-stack acceptance matrix');
} catch (error) {
  failed = error;
  console.error(error instanceof Error ? error.stack : error);
  try {
    console.error(composeRun(['logs', '--no-color', '--tail', '160', 'notification']));
    console.error(
      run('docker', [
        'exec',
        containers.rabbitmq,
        'rabbitmqctl',
        'list_queues',
        'name',
        'messages_ready',
        'messages_unacknowledged',
      ]),
    );
  } catch (diagnosticError) {
    console.error(`FAIL | diagnostics | ${String(diagnosticError)}`);
  }
} finally {
  try {
    composeRun(['down', '-v', '--remove-orphans']);
    console.log('PASS | isolated Notification idempotency stack cleanup');
  } catch (error) {
    failed ??= error;
    console.error(`FAIL | cleanup | ${String(error)}`);
  }
}

process.exitCode = failed ? 1 : 0;
