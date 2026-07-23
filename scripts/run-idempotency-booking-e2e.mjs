import { spawnSync } from 'node:child_process';

const root = process.cwd();
const noBuild = process.argv.includes('--no-build');
const reuseImages = process.argv.includes('--reuse-images') || process.env.E2E_REUSE_IMAGES === '1';
const useCachedImages = noBuild || reuseImages;
const compose = [
  'compose',
  '--env-file',
  '.env',
  '-f',
  'infra/docker/docker-compose.yml',
  '-f',
  'infra/docker/docker-compose.day37-e2e.yml',
  '--profile',
  'app',
];
const e2eEnv = {
  POSTGRES_PORT: '55437',
  REDIS_PORT: '56379',
  RABBITMQ_PORT: '55672',
  RABBITMQ_MGMT_PORT: '55673',
  IDENTITY_PORT: '55001',
  TRIP_PORT: '55002',
  BOOKING_PORT: '55003',
  PAYMENT_PORT: '55004',
  PARCEL_PORT: '55005',
  NOTIFICATION_PORT: '55012',
  GATEWAY_PORT: '55300',
};

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    env: { ...process.env, ...e2eEnv, ...options.env },
    stdio: options.stdio ?? ['ignore', 'pipe', 'pipe'],
    maxBuffer: 32 * 1024 * 1024,
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed: ${result.stderr || result.stdout}`);
  }
  return result.stdout?.trim() ?? '';
}

let failed;
try {
  run('docker', [...compose, 'down', '-v', '--remove-orphans']);
  if (reuseImages && !noBuild) {
    run('docker', [...compose, '--parallel', '1', 'build', 'payment', 'booking'], {
      stdio: 'inherit',
    });
  }
  if (useCachedImages) {
    run(
      'docker',
      [...compose, 'up', '-d', '--no-build', '--wait', 'postgres', 'redis', 'rabbitmq'],
      {
        stdio: 'inherit',
      },
    );
    run(
      'docker',
      [...compose, 'up', '-d', '--no-build', '--wait', 'identity', 'trip', 'booking', 'payment'],
      { stdio: 'inherit' },
    );
    run('docker', [...compose, 'up', '-d', '--no-build', '--no-deps', '--wait', 'gateway'], {
      stdio: 'inherit',
    });
  } else {
    run('docker', [
      ...compose,
      '--parallel',
      '1',
      'up',
      '-d',
      '--build',
      '--wait',
      'gateway',
      'notification',
    ]);
  }
  console.log('PASS | isolated booking compose health | http://localhost:55300');

  run('node', ['--env-file=.env', 'scripts/run-station-stop-booking-vnpay-e2e.mjs'], {
    stdio: 'inherit',
    env: {
      GATEWAY_BASE_URL: 'http://localhost:55300',
      PAYMENT_BASE_URL: 'http://localhost:55004',
      POSTGRES_CONTAINER: 'day37-e2e-postgres',
      GATEWAY_CONTAINER: 'day37-e2e-gateway',
      PAYMENT_CONTAINER: 'day37-e2e-payment',
      E2E_COMPOSE_OVERLAY: 'infra/docker/docker-compose.day37-e2e.yml',
      E2E_OWNS_BASE_FIXTURES: '1',
      IDEMPOTENCY_BOOKING_FOCUSED: '1',
      E2E_SKIP_NOTIFICATION_CLEANUP: useCachedImages ? '1' : '0',
    },
  });
  console.log('PASS | booking/payment/trip idempotency system E2E');
} catch (error) {
  failed = error;
  console.error(error instanceof Error ? error.stack : error);
  try {
    const serviceLogs = run('docker', [...compose, 'logs', '--no-color', 'payment', 'booking']);
    const relevantLogs = serviceLogs
      .split(/\r?\n/u)
      .filter((line) =>
        /RabbitMQ delivery|nack|MessageId|exception|error|payment-succeeded|ConfirmBookingOnPayment/iu.test(
          line,
        ),
      )
      .slice(-120)
      .join('\n');
    console.error(`E2E relevant service logs:\n${relevantLogs}`);
  } catch (diagnosticError) {
    console.error(`FAIL | service-log diagnostics | ${String(diagnosticError)}`);
  }
  try {
    run(
      'docker',
      [
        'exec',
        'day37-e2e-postgres',
        'psql',
        '-U',
        process.env.POSTGRES_USER ?? 'vietride',
        '-d',
        'vietride_payment',
        '-c',
        'select id, event_type, status, payload from vietride_payment.outbox_events order by created_at desc limit 5;',
      ],
      { stdio: 'inherit' },
    );
    run(
      'docker',
      [
        'exec',
        'day37-e2e-postgres',
        'psql',
        '-U',
        process.env.POSTGRES_USER ?? 'vietride',
        '-d',
        'vietride_booking',
        '-c',
        'select consumer_name, message_id, payload_hash from vietride_booking.integration_inbox order by processed_at desc limit 5;',
      ],
      { stdio: 'inherit' },
    );
  } catch (diagnosticError) {
    console.error(`FAIL | database diagnostics | ${String(diagnosticError)}`);
  }
  try {
    run(
      'docker',
      [
        'exec',
        'day37-e2e-rabbitmq',
        'rabbitmqctl',
        'list_queues',
        'name',
        'messages_ready',
        'messages_unacknowledged',
      ],
      { stdio: 'inherit' },
    );
  } catch (diagnosticError) {
    console.error(`FAIL | RabbitMQ diagnostics | ${String(diagnosticError)}`);
  }
} finally {
  try {
    run('docker', [...compose, 'down', '-v', '--remove-orphans']);
    console.log('PASS | isolated booking compose cleanup');
  } catch (error) {
    failed ??= error;
    console.error(`FAIL | isolated booking compose cleanup | ${String(error)}`);
  }
}

process.exitCode = failed ? 1 : 0;
