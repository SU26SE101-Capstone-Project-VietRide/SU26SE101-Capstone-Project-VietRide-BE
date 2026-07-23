import { spawnSync } from 'node:child_process';
import process from 'node:process';

const runId = `${process.pid}-${Date.now()}`;
const containers = {
  postgres: `vietride-rag-idem-postgres-${runId}`,
  redis: `vietride-rag-idem-redis-${runId}`,
};

try {
  run('docker', [
    'run',
    '-d',
    '--name',
    containers.postgres,
    '-e',
    'POSTGRES_USER=postgres',
    '-e',
    'POSTGRES_PASSWORD=postgres',
    '-e',
    'POSTGRES_DB=vietride_rag_e2e',
    '-p',
    '127.0.0.1::5432',
    '--health-cmd',
    'pg_isready -U postgres -d vietride_rag_e2e',
    '--health-interval',
    '2s',
    '--health-timeout',
    '2s',
    '--health-retries',
    '30',
    'pgvector/pgvector:pg16',
  ]);
  run('docker', [
    'run',
    '-d',
    '--name',
    containers.redis,
    '-p',
    '127.0.0.1::6379',
    '--health-cmd',
    'redis-cli ping',
    '--health-interval',
    '2s',
    '--health-timeout',
    '2s',
    '--health-retries',
    '30',
    'redis:7-alpine',
  ]);
  for (const name of Object.values(containers)) waitUntilHealthy(name);
  const postgresPort = publishedPort(containers.postgres, '5432/tcp');
  const redisPort = publishedPort(containers.redis, '6379/tcp');
  const env = {
    ...process.env,
    NODE_ENV: 'test',
    DATABASE_URL: `postgresql://postgres:postgres@127.0.0.1:${postgresPort}/vietride_rag_e2e`,
    RAG_DATABASE_URL: `postgresql://postgres:postgres@127.0.0.1:${postgresPort}/vietride_rag_e2e`,
    REDIS_URL: `redis://127.0.0.1:${redisPort}`,
    RABBITMQ_URL: 'amqp://guest:guest@127.0.0.1:1',
    RABBITMQ_EXCHANGE: `vietride.rag.idempotency.${runId}`,
    INTERNAL_JWT_SECRET: 'rag-real-idempotency-e2e-secret-at-least-32-chars',
    OPENROUTER_API_KEY: 'boundary-fake',
    CLOUDINARY_CLOUD_NAME: 'boundary-fake',
    CLOUDINARY_API_KEY: 'boundary-fake',
    CLOUDINARY_API_SECRET: 'boundary-fake',
    RAG_EMBEDDING_DIMENSIONS: '2048',
    RAG_INGEST_WORKER_ENABLED: 'false',
    RAG_OUTBOX_PUBLISH_ENABLED: 'false',
    INTENT_FILTER_ENABLED: 'false',
    QUERY_REWRITE_ENABLED: 'false',
    HYBRID_SEARCH_ENABLED: 'false',
    RERANK_ENABLED: 'false',
    SUMMARIZE_ENABLED: 'false',
    RAG_REAL_IDEMPOTENCY_E2E: '1',
    SENTRY_DSN: '',
  };

  run(
    process.execPath,
    [
      'node_modules/prisma/build/index.js',
      'migrate',
      'deploy',
      '--schema=apps/rag/prisma/schema.prisma',
    ],
    { env },
  );
  run(
    process.execPath,
    [
      'node_modules/jest/bin/jest.js',
      '--config=apps/rag/jest.e2e.config.cts',
      '--runInBand',
      '--testPathPatterns=rag-idempotency.real.e2e-spec.ts',
    ],
    { env },
  );
  console.log('PASS | RAG real PostgreSQL/Redis idempotency system E2E');
} finally {
  for (const name of Object.values(containers).reverse()) {
    spawnSync(executable('docker'), ['rm', '-f', name], {
      stdio: 'ignore',
    });
  }
  console.log('PASS | isolated RAG idempotency containers cleaned up');
}

function run(command, args, options = {}) {
  const result = spawnSync(executable(command), args, {
    cwd: process.cwd(),
    env: options.env ?? process.env,
    stdio: 'inherit',
  });
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} failed with exit code ${result.status}`);
  }
}

function output(command, args) {
  const result = spawnSync(executable(command), args, {
    cwd: process.cwd(),
    encoding: 'utf8',
  });
  if (result.status !== 0) throw new Error(result.stderr || `${command} failed`);
  return result.stdout.trim();
}

function waitUntilHealthy(name) {
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    const status = output('docker', [
      'inspect',
      '--format',
      '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}',
      name,
    ]);
    if (status === 'healthy') return;
    if (status === 'exited' || status === 'dead' || status === 'unhealthy') {
      throw new Error(`${name} entered ${status} state`);
    }
    sleep(1_000);
  }
  throw new Error(`${name} did not become healthy`);
}

function publishedPort(name, containerPort) {
  const value = output('docker', ['port', name, containerPort]);
  const match = /:(\d+)\s*$/.exec(value);
  if (!match) throw new Error(`Cannot resolve ${containerPort} for ${name}: ${value}`);
  return match[1];
}

function sleep(milliseconds) {
  Atomics.wait(new Int32Array(new SharedArrayBuffer(4)), 0, 0, milliseconds);
}

function executable(command) {
  return process.platform === 'win32' && command === 'npx' ? 'npx.cmd' : command;
}
