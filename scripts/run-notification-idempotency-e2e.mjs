import { execFileSync } from 'node:child_process';
import { connect } from 'amqplib';
import { randomUUID } from 'node:crypto';

const root = process.cwd();
const noBuild = process.argv.includes('--no-build');
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
  NOTIFICATION_PORT: '59022',
  POSTGRES_USER: process.env.POSTGRES_USER ?? 'vietride',
  POSTGRES_PASSWORD: process.env.POSTGRES_PASSWORD ?? 'vietride_dev',
  RABBITMQ_USER: process.env.RABBITMQ_USER ?? 'vietride',
  RABBITMQ_PASSWORD: process.env.RABBITMQ_PASSWORD ?? 'vietride_dev',
  INTERNAL_JWT_SECRET:
    process.env.INTERNAL_JWT_SECRET ?? 'notification-idempotency-e2e-secret-32-bytes',
};
const containers = {
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

async function publish(payload) {
  const connection = await connect(
    `amqp://${env.RABBITMQ_USER}:${env.RABBITMQ_PASSWORD}@127.0.0.1:${env.RABBITMQ_PORT}`,
  );
  try {
    const channel = await connection.createConfirmChannel();
    await channel.assertExchange('vietride.events', 'topic', { durable: true });
    channel.publish('vietride.events', routingKey, Buffer.from(JSON.stringify(payload)), {
      contentType: 'application/json',
      messageId,
      persistent: true,
    });
    await channel.waitForConfirms();
    await channel.close();
  } finally {
    await connection.close();
  }
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

let failed;
try {
  composeRun(['down', '-v', '--remove-orphans']);
  if (!noBuild) {
    composeRun(['--parallel', '1', 'build', 'notification'], { stdio: 'inherit' });
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
        PERFORM pg_sleep(30);
      END IF;
      RETURN NEW;
    END;
    $e2e$;
    CREATE TRIGGER e2e_pause_first_processed_message
      BEFORE INSERT ON vietride_notification.processed_messages
      FOR EACH ROW EXECUTE FUNCTION vietride_notification.e2e_pause_first_processed_message();
  `);

  await publish(eventPayload());
  await waitFor(
    'push and email sent while processed marker is paused',
    () => sideEffectSnapshot() === '1|1|1|0',
  );
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
