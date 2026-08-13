const { execFileSync } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const { existsSync, readFileSync } = require('node:fs');
const { resolve } = require('node:path');

const DEFAULT_BASE_URL = 'http://localhost:3000';
const DEFAULT_EMAIL = 'rag.passenger.test@vietride.local';
const DEFAULT_PASSWORD = 'Test@123456';
const DEFAULT_PHONE = '+84900000001';
const DEFAULT_QUESTION = 'Quy dinh hanh ly la gi?';
const REQUEST_TIMEOUT_MS = 60_000;
const CHAT_TIMEOUT_MS = 180_000;
const UUID_PATTERN = /\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\b/i;

loadEnvFile(process.env.RAG_FULL_FLOW_ENV_FILE ?? '.env.rag-full-flow');
loadEnvFile('.env');

const BASE_URL = (process.env.BASE_URL ?? DEFAULT_BASE_URL).replace(/\/$/, '');
const DB_MODE = (process.env.DB_MODE ?? 'direct').toLowerCase();
const IDENTITY_DB_URL = process.env.IDENTITY_DB_URL;
const RAG_DATABASE_URL = process.env.RAG_DATABASE_URL;
const TEST_EMAIL = process.env.RAG_TEST_EMAIL ?? DEFAULT_EMAIL;
const TEST_PASSWORD = process.env.RAG_TEST_PASSWORD ?? DEFAULT_PASSWORD;
const TEST_PHONE = process.env.RAG_TEST_PHONE ?? DEFAULT_PHONE;
const TEST_QUESTION = process.env.RAG_TEST_QUESTION ?? DEFAULT_QUESTION;
const PSQL_BIN = process.env.PSQL_BIN ?? 'psql';
const PSQL_DOCKER_IMAGE = process.env.PSQL_DOCKER_IMAGE ?? 'postgres:16-alpine';
let useDockerPsql = false;

const REGISTER_URL = `${BASE_URL}/v1/auth/register`;
const LOGIN_URL = `${BASE_URL}/v1/auth/login`;
const CHAT_URL = `${BASE_URL}/v1/rag/chat`;

async function main() {
  assert(DB_MODE === 'direct' || DB_MODE === 'manual', 'DB_MODE must be either direct or manual.');

  await preflightGateway();

  let userExistedBeforeSeed = true;
  if (DB_MODE === 'direct') {
    assertDirectDbEnv();
    preflightPsql();
    preflightIdentityDb();
    preflightRagDb();
    userExistedBeforeSeed = await seedPassengerAccount();
  } else {
    pass('DB_MODE=manual: skipping DB preflight, seed, and cited chunk DB verification.');
  }

  const accessToken = await login(userExistedBeforeSeed);
  await testNegativeAuth();
  const doneData = await testRagChat(accessToken);
  if (DB_MODE === 'direct') {
    verifyPersistedCitedChunks(doneData.assistantMessageId, doneData.citations);
  } else {
    pass(`DB_MODE=manual: skipped DB verification for ${doneData.citations.length} friendly citations.`);
  }

  pass('RAG real deploy full-flow automation passed.');
}

function assertDirectDbEnv() {
  assert(IDENTITY_DB_URL, 'Set IDENTITY_DB_URL before running with DB_MODE=direct.');
  assert(RAG_DATABASE_URL, 'Set RAG_DATABASE_URL before running with DB_MODE=direct.');
  assert(!IDENTITY_DB_URL.includes('<DB_PASSWORD>'), 'Replace <DB_PASSWORD> in IDENTITY_DB_URL before running with DB_MODE=direct.');
  assert(!RAG_DATABASE_URL.includes('<DB_PASSWORD>'), 'Replace <DB_PASSWORD> in RAG_DATABASE_URL before running with DB_MODE=direct.');
}

function preflightPsql() {
  try {
    execFileSync(PSQL_BIN, ['--version'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    pass(`psql CLI is available via ${PSQL_BIN}.`);
    useDockerPsql = false;
    return;
  } catch {
    // Fall through to Docker-based psql below. This keeps the script runnable on machines
    // where PostgreSQL client tools are not installed in PATH.
  }

  try {
    execFileSync('docker', ['run', '--rm', PSQL_DOCKER_IMAGE, 'psql', '--version'], {
      encoding: 'utf8',
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    useDockerPsql = true;
    pass(`psql CLI is available via Docker image ${PSQL_DOCKER_IMAGE}.`);
  } catch {
    fail('psql CLI is required. Install PostgreSQL client tools, set PSQL_BIN to psql.exe, or start Docker with postgres:16-alpine available.');
  }
}

async function preflightGateway() {
  let response;
  try {
    response = await fetchWithTimeout(`${BASE_URL}/health`, { method: 'GET' }, REQUEST_TIMEOUT_MS);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    fail(`Gateway is not reachable at ${BASE_URL}/health: ${message}`);
  }
  assert(response.ok, `Gateway health expected 2xx, got ${response.status}. Is Gateway running at ${BASE_URL}?`);
  pass('Gateway is reachable.');
}

function preflightIdentityDb() {
  const table = 'vietride_identity.users';
  const exists = psqlValue(IDENTITY_DB_URL, `select to_regclass('${table}') is not null;`);
  assert(exists === 't', `Identity DB table ${table} was not found.`);
  pass(`Identity DB table ${table} is reachable.`);
}

function preflightRagDb() {
  const documentCount = Number(psqlValue(
    RAG_DATABASE_URL,
    "select count(*) from vietride_rag.knowledge_documents where status = 'APPROVED'::vietride_rag.knowledge_document_status and ingest_status = 'COMPLETED'::vietride_rag.knowledge_document_ingest_status;",
  ));
  assert(documentCount > 0, 'RAG DB must contain at least one APPROVED + COMPLETED knowledge document.');

  const chunkCount = Number(psqlValue(
    RAG_DATABASE_URL,
    'select count(*) from vietride_rag.knowledge_chunks;',
  ));
  assert(chunkCount > 0, 'RAG DB must contain knowledge_chunks.');
  pass(`RAG DB has ${documentCount} approved completed documents and ${chunkCount} chunks.`);
}

async function seedPassengerAccount() {
  const identityUsersTable = 'vietride_identity.users';
  const userExists = psqlValue(
    IDENTITY_DB_URL,
    `select exists(select 1 from ${identityUsersTable} where lower(email) = lower(:'email'));`,
    { email: TEST_EMAIL },
  ) === 't';

  if (!userExists) {
    const response = await fetchWithTimeout(REGISTER_URL, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Idempotency-Key': randomUUID(),
      },
      body: JSON.stringify({
        email: TEST_EMAIL,
        password: TEST_PASSWORD,
        displayName: 'RAG Passenger Test',
        phone: TEST_PHONE,
      }),
    }, REQUEST_TIMEOUT_MS);
    const text = await response.text();
    assert(
      response.status === 201 || response.status === 409,
      `register expected 201 or duplicate 409, got ${response.status}: ${text}`,
    );
    pass(response.status === 201 ? 'Created passenger test account via Gateway register.' : 'Passenger test account already exists at register time.');
  } else {
    pass('Passenger test account already exists in Identity DB.');
  }

  psqlExec(
    IDENTITY_DB_URL,
    `update ${identityUsersTable}
       set status = 'ACTIVE',
           role = 'PASSENGER',
           phone = :'phone',
           operator_id = null,
           failed_login_attempts = 0,
           last_failed_login_at = null,
           deleted_at = null,
           updated_at = now()
     where lower(email) = lower(:'email');`,
    { email: TEST_EMAIL, phone: TEST_PHONE },
  );
  pass('Passenger test account normalized in Identity DB.');
  return userExists;
}

async function login(userExistedBeforeSeed) {
  const response = await fetchWithTimeout(LOGIN_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: TEST_EMAIL, password: TEST_PASSWORD }),
  }, REQUEST_TIMEOUT_MS);
  const body = await parseJsonResponse(response, 'login');
  if (response.status !== 200 && userExistedBeforeSeed) {
    fail(`login expected 200, got ${response.status}. Test user already existed; RAG_TEST_PASSWORD may not match its current password. Body: ${JSON.stringify(body)}`);
  }
  assert(response.status === 200, `login expected 200, got ${response.status}: ${JSON.stringify(body)}`);
  const accessToken = body.data?.accessToken ?? body.accessToken;
  assert(accessToken, 'login response must include data.accessToken.');
  pass('Login through Gateway returned a real Identity accessToken.');
  return accessToken;
}

async function testNegativeAuth() {
  const response = await fetchWithTimeout(CHAT_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message: TEST_QUESTION }),
  }, REQUEST_TIMEOUT_MS);
  assert(response.status === 401, `RAG chat without token expected 401, got ${response.status}.`);
  pass('RAG chat without bearer token returned 401.');
}

async function testRagChat(accessToken) {
  const response = await fetchWithTimeout(CHAT_URL, {
    method: 'POST',
    headers: {
      Authorization: `Bearer ${accessToken}`,
      'Content-Type': 'application/json',
      'Idempotency-Key': randomUUID(),
    },
    body: JSON.stringify({ message: TEST_QUESTION }),
  }, CHAT_TIMEOUT_MS);
  const text = await response.text();
  assert(response.status === 200, `RAG chat expected 200, got ${response.status}: ${text}`);
  const events = parseSse(text);
  const error = events.find((event) => event.event === 'error');
  if (error) {
    fail(`RAG SSE returned error event: ${JSON.stringify(error.data)}`);
  }
  assert(events.some((event) => event.event === 'token' && event.data?.content), 'RAG SSE must include at least one token event with content.');
  const done = events.find((event) => event.event === 'done');
  assert(done, 'RAG SSE must include done event.');
  assert(Array.isArray(done.data?.citations), 'RAG done event must include citations array.');
  assert(done.data.citations.length > 0, 'RAG done citations must not be empty.');
  assert(!Object.hasOwn(done.data, 'citedChunkIds'), 'RAG done event must not expose citedChunkIds.');
  done.data.citations.forEach((citation) => {
    assert(typeof citation.title === 'string' && citation.title.length > 0, 'Citation title is required.');
    assert(citation.section === null || typeof citation.section === 'string', 'Citation section must be string or null.');
    assert(!UUID_PATTERN.test(citation.title), 'Citation title must not expose a UUID.');
    assert(citation.section === null || !UUID_PATTERN.test(citation.section), 'Citation section must not expose a UUID.');
  });
  pass(`RAG chat returned SSE token + done with ${done.data.citations.length} friendly citations.`);
  return done.data;
}

function verifyPersistedCitedChunks(assistantMessageId, citations) {
  const rows = psqlRows(
    RAG_DATABASE_URL,
    `select c.id::text, c.document_title, c.section_header
       from vietride_rag.rag_messages m
       join vietride_rag.knowledge_chunks c on c.id = any(m.cited_chunk_ids)
      where m.id = :'assistantMessageId'::uuid
      order by c.document_title, c.section_header, c.id;`,
    { assistantMessageId },
  );
  assert(rows.length > 0, 'Assistant message must retain internal citedChunkIds for audit.');
  const friendlyKeys = new Set(citations.map((citation) => JSON.stringify([citation.title, citation.section])));
  rows.forEach(([, title, section]) => {
    assert(friendlyKeys.has(JSON.stringify([title, section || null])), `Missing friendly citation for ${title}.`);
  });
  pass('Internal citedChunkIds remain auditable without being exposed to the client.');
}

async function fetchWithTimeout(url, init, timeoutMs) {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } finally {
    clearTimeout(timeout);
  }
}

async function parseJsonResponse(response, label) {
  const text = await response.text();
  try {
    return JSON.parse(text);
  } catch {
    fail(`${label} response was not valid JSON: ${text}`);
  }
}

function parseSse(text) {
  const events = [];
  let currentEvent = 'message';
  let dataLines = [];

  for (const line of text.split(/\r?\n/)) {
    if (line === '') {
      if (dataLines.length > 0) {
        events.push({ event: currentEvent, data: parseSseData(dataLines.join('\n')) });
      }
      currentEvent = 'message';
      dataLines = [];
      continue;
    }
    if (line.startsWith('event:')) currentEvent = line.slice('event:'.length).trim();
    if (line.startsWith('data:')) dataLines.push(line.slice('data:'.length).trimStart());
  }

  if (dataLines.length > 0) {
    events.push({ event: currentEvent, data: parseSseData(dataLines.join('\n')) });
  }
  return events;
}

function parseSseData(data) {
  try {
    return JSON.parse(data);
  } catch {
    return data;
  }
}

function psqlValue(dbUrl, sql, variables = {}) {
  return psqlExec(dbUrl, sql, variables, ['--tuples-only', '--no-align']).trim();
}

function psqlRows(dbUrl, sql, variables = {}) {
  const output = psqlExec(dbUrl, sql, variables, ['--tuples-only', '--csv']);
  return output
    .trim()
    .split('\n')
    .filter(Boolean)
    .map(parseCsvLine);
}

function psqlExec(dbUrl, sql, variables = {}, outputArgs = []) {
  const effectiveDbUrl = useDockerPsql ? rewriteLocalhostForDocker(dbUrl) : dbUrl;
  const args = [effectiveDbUrl, '--set', 'ON_ERROR_STOP=1'];
  for (const [key, value] of Object.entries(variables)) {
    args.push('--variable', `${key}=${value}`);
  }
  args.push(...outputArgs);

  try {
    if (useDockerPsql) {
      return execFileSync('docker', ['run', '--rm', '-i', PSQL_DOCKER_IMAGE, 'psql', ...args], {
        encoding: 'utf8',
        input: sql,
        stdio: ['pipe', 'pipe', 'pipe'],
      });
    }
    return execFileSync(PSQL_BIN, args, {
      encoding: 'utf8',
      input: sql,
      stdio: ['pipe', 'pipe', 'pipe'],
    });
  } catch (error) {
    const stderr = sanitizeSecret(String(error.stderr ?? error.message ?? error));
    fail(`psql command failed: ${stderr}\nIf DB is only available through Navicat GUI, use DB_MODE=manual and seed the test account manually.`);
  }
}

function rewriteLocalhostForDocker(dbUrl) {
  try {
    const parsed = new URL(dbUrl);
    if (parsed.hostname === '127.0.0.1' || parsed.hostname === 'localhost' || parsed.hostname === '::1') {
      parsed.hostname = 'host.docker.internal';
    }
    return parsed.toString();
  } catch {
    return dbUrl;
  }
}

function parseCsvLine(line) {
  const values = [];
  let value = '';
  let quoted = false;

  for (let index = 0; index < line.length; index += 1) {
    const char = line[index];
    if (quoted) {
      if (char === '"' && line[index + 1] === '"') {
        value += '"';
        index += 1;
      } else if (char === '"') {
        quoted = false;
      } else {
        value += char;
      }
    } else if (char === '"') {
      quoted = true;
    } else if (char === ',') {
      values.push(value);
      value = '';
    } else {
      value += char;
    }
  }

  values.push(value);
  return values;
}

function sanitizeSecret(text) {
  return text
    .replaceAll(IDENTITY_DB_URL ?? '', '<IDENTITY_DB_URL>')
    .replaceAll(RAG_DATABASE_URL ?? '', '<RAG_DATABASE_URL>');
}

function loadEnvFile(filePath) {
  const resolvedPath = resolve(filePath);
  if (!existsSync(resolvedPath)) return;

  const content = readFileSync(resolvedPath, 'utf8');
  for (const rawLine of content.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;

    const separatorIndex = line.indexOf('=');
    if (separatorIndex <= 0) continue;

    const key = line.slice(0, separatorIndex).trim();
    if (Object.prototype.hasOwnProperty.call(process.env, key)) continue;

    process.env[key] = unquoteEnvValue(line.slice(separatorIndex + 1).trim());
  }
}

function unquoteEnvValue(value) {
  if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
    return value.slice(1, -1);
  }
  return value;
}

function assert(condition, message) {
  if (!condition) fail(message);
}

function pass(message) {
  console.log(`[PASS] ${message}`);
}

function fail(message) {
  console.error(`[FAIL] ${message}`);
  process.exit(1);
}

main().catch((error) => {
  const message = error instanceof Error ? error.message : String(error);
  fail(`Unexpected script error: ${sanitizeSecret(message)}`);
});
