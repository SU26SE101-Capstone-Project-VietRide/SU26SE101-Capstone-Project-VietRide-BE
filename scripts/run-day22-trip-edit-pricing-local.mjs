// Day-22 Trip edit/pricing verification. Public API requests always use Gateway.
// Direct database access is limited to isolated fixture setup, bounded evidence reads,
// and cleanup. Runtime tokens and idempotency keys are never printed or persisted.
import { execFileSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const gatewayBaseUrl = (process.env.GATEWAY_BASE_URL || 'http://localhost:3000').replace(/\/$/, '');
const rabbitManagementBaseUrl = (
  process.env.RABBITMQ_MGMT_BASE_URL || 'http://localhost:15672'
).replace(/\/$/, '');
const args = new Set(process.argv.slice(2));
const helpRequested = args.has('--help') || args.has('-h');
const staticOnly = args.has('--static-only');
const skipDay21 = args.has('--skip-day21');
const skipTargeted = args.has('--skip-targeted');
const fullMatrix = args.has('--full-matrix');
const allowedArgs = new Set([
  '--help',
  '-h',
  '--static-only',
  '--skip-day21',
  '--skip-targeted',
  '--full-matrix',
]);

for (const argument of args) {
  if (!allowedArgs.has(argument)) throw new Error(`Unknown argument: ${argument}`);
}

if (staticOnly && (skipDay21 || skipTargeted || fullMatrix)) {
  throw new Error('--static-only cannot be combined with runtime skip flags or --full-matrix');
}
if (fullMatrix && (skipDay21 || skipTargeted)) {
  throw new Error(
    '--full-matrix is a close-out run and cannot skip targeted or Day-21 verification',
  );
}

if (helpRequested) {
  console.log(`Usage: node scripts/run-day22-trip-edit-pricing-local.mjs [options]

Options:
  --static-only     Diagnostic Task-22.0 checks only; always finishes DEFERRED
  --skip-targeted   Diagnostic runtime run without focused Day-22 regressions; never close-out PASS
  --skip-day21      Diagnostic runtime run without required Day-21 regression; never close-out PASS
  --full-matrix     Close-out run: every .NET build/format/test and full TS build/lint/test matrix
  --help, -h        Show this help

Default: static checks, isolated Gateway middleware/API checks, live duplicate Parcel cancellation,
focused Day-22 regressions, route-change Notification regression, Day-21 lifecycle regression,
and verified cleanup.`);
  process.exit(0);
}

const ids = Object.freeze({
  operator: crypto.randomUUID(),
  admin: crypto.randomUUID(),
  passenger: crypto.randomUUID(),
  platformWallet: crypto.randomUUID(),
  otherAdmin: crypto.randomUUID(),
  staff: crypto.randomUUID(),
  driver: crypto.randomUUID(),
  assistant: crypto.randomUUID(),
  originStation: crypto.randomUUID(),
  destinationStation: crypto.randomUUID(),
  vehicleType: crypto.randomUUID(),
  route: crypto.randomUUID(),
  vehicle: crypto.randomUUID(),
  trip: crypto.randomUUID(),
  otherTrip: crypto.randomUUID(),
  schedule: crypto.randomUUID(),
  otherSchedule: crypto.randomUUID(),
  parcel: crypto.randomUUID(),
  parcelCancellationEvent: crypto.randomUUID(),
});
const runTag = ids.trip.replaceAll('-', '').slice(0, 10).toUpperCase();
const idempotencyKeys = new Set();
const bookingIds = new Set();
let platformWalletBaseline;
const applicationContainers = Object.freeze([
  'vietride_gateway',
  'vietride_identity',
  'vietride_trip',
  'vietride_booking',
  'vietride_payment',
  'vietride_parcel',
  'vietride_tracking',
  'vietride_notification',
  'vietride_rag',
]);

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function run(command, commandArgs, options = {}) {
  console.log(`RUN  | ${command} ${commandArgs.join(' ')}`);
  execFileSync(command, commandArgs, { cwd: root, stdio: 'inherit', ...options });
}

function capture(command, commandArgs, options = {}) {
  return execFileSync(command, commandArgs, { cwd: root, encoding: 'utf8', ...options }).trim();
}

function executableFilesOnPath(names) {
  const pathEntries = (process.env.PATH ?? '')
    .split(path.delimiter)
    .map((entry) => entry.trim().replace(/^"|"$/g, ''))
    .filter(Boolean);
  const extensions =
    process.platform === 'win32'
      ? (process.env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD').split(';').filter(Boolean)
      : [''];
  const results = [];

  for (const directory of pathEntries) {
    for (const name of names) {
      const hasExtension = path.extname(name).length > 0;
      for (const extension of hasExtension ? [''] : extensions) {
        const candidate = path.resolve(directory, `${name}${extension.toLowerCase()}`);
        try {
          if (fs.statSync(candidate).isFile()) results.push(candidate);
        } catch {
          // A PATH entry can disappear between environment capture and resolution.
        }
      }
    }
  }
  return results;
}

function realPathIfAvailable(candidate) {
  try {
    return fs.realpathSync(candidate);
  } catch {
    return candidate;
  }
}

function resolveNpxCli() {
  const npmExecutables = executableFilesOnPath(['npm', 'npm.cmd']);
  const npmCliPaths = [process.env.npm_execpath, ...npmExecutables]
    .filter((candidate) => typeof candidate === 'string' && candidate.trim().length > 0)
    .flatMap((candidate) => {
      const resolved = path.resolve(candidate);
      return [resolved, realPathIfAvailable(resolved)];
    });
  const prefixes = [process.env.npm_config_prefix, ...npmExecutables.map(path.dirname)]
    .filter((candidate) => typeof candidate === 'string' && candidate.trim().length > 0)
    .map((candidate) => path.resolve(candidate));
  const candidates = [
    ...npmCliPaths.map((candidate) => path.join(path.dirname(candidate), 'npx-cli.js')),
    path.join(path.dirname(process.execPath), 'node_modules', 'npm', 'bin', 'npx-cli.js'),
    path.join(
      path.dirname(process.execPath),
      '..',
      'lib',
      'node_modules',
      'npm',
      'bin',
      'npx-cli.js',
    ),
    path.join(path.dirname(process.execPath), '..', 'share', 'nodejs', 'npm', 'bin', 'npx-cli.js'),
    path.join(root, 'node_modules', 'npm', 'bin', 'npx-cli.js'),
    '/usr/share/nodejs/npm/bin/npx-cli.js',
    '/usr/lib/node_modules/npm/bin/npx-cli.js',
    ...prefixes.flatMap((prefix) => [
      path.join(prefix, 'node_modules', 'npm', 'bin', 'npx-cli.js'),
      path.join(prefix, 'lib', 'node_modules', 'npm', 'bin', 'npx-cli.js'),
      path.join(prefix, 'share', 'nodejs', 'npm', 'bin', 'npx-cli.js'),
    ]),
  ].map((candidate) => path.resolve(candidate));
  const uniqueCandidates = [
    ...new Map(
      candidates.map((candidate) => {
        const normalized = path.normalize(candidate);
        const key = process.platform === 'win32' ? normalized.toLowerCase() : normalized;
        return [key, normalized];
      }),
    ).values(),
  ];
  const resolved = uniqueCandidates.find((candidate) => {
    try {
      return fs.statSync(candidate).isFile();
    } catch {
      return false;
    }
  });
  assert(
    resolved,
    `Unable to resolve npm's npx-cli.js from npm_execpath, npm_config_prefix, PATH, Node, or known system locations. Checked: ${uniqueCandidates.join(', ')}`,
  );
  return resolved;
}

function runNpx(commandArgs) {
  run(process.execPath, [resolveNpxCli(), ...commandArgs], { shell: false });
}

async function poll(label, probe, predicate, timeoutMs = 45_000) {
  const deadline = Date.now() + timeoutMs;
  let value;
  while (Date.now() < deadline) {
    value = await probe();
    if (predicate(value)) {
      console.log(`PASS | ${label}`);
      return value;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  throw new Error(`${label} timed out; last observed value=${JSON.stringify(value)}`);
}

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), 'utf8');
}

function normalizeStatement(value) {
  return value.normalize('NFC').replace(/[`*]/g, '').replace(/\s+/g, ' ').trim();
}

function contractStatements(content) {
  return content
    .split(/\r?\n\s*\r?\n|(?=^#{1,6}\s)/m)
    .map(normalizeStatement)
    .filter(Boolean);
}

function contractSections(content) {
  const sections = [];
  let heading = '';
  let body = [];
  const flush = () => {
    const section = normalizeStatement([heading, ...body].join('\n'));
    if (section) sections.push(section);
  };
  for (const line of content.split(/\r?\n/)) {
    if (/^#{1,6}\s/.test(line)) {
      flush();
      heading = line;
      body = [];
    } else {
      body.push(line);
    }
  }
  flush();
  return sections;
}

function assertNoAffirmativeStatement(statements, label, candidate, negated) {
  const contradictions = statements.filter(
    (statement) => candidate(statement) && !negated(statement),
  );
  assert(contradictions.length === 0, `${label}: ${contradictions.join(' | ')}`);
}

function resetResultsDirectory(relativeDirectory) {
  const absoluteDirectory = path.join(root, relativeDirectory);
  fs.rmSync(absoluteDirectory, { recursive: true, force: true });
  fs.mkdirSync(absoluteDirectory, { recursive: true });
  return absoluteDirectory;
}

function trxFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return trxFiles(entryPath);
    return entry.isFile() && entry.name.toLowerCase().endsWith('.trx') ? [entryPath] : [];
  });
}

function assertTrxExecuted(relativeDirectory, label, expectedFileCount) {
  const directory = path.join(root, relativeDirectory);
  const files = trxFiles(directory);
  assert(files.length > 0, `${label}: dotnet test produced no TRX file`);
  if (expectedFileCount !== undefined) {
    assert(
      files.length === expectedFileCount,
      `${label}: expected ${expectedFileCount} TRX files but found ${files.length}`,
    );
  }
  let executed = 0;
  let failed = 0;
  for (const file of files) {
    const xml = fs.readFileSync(file, 'utf8');
    const countersTag = xml.match(/<Counters\b[^>]*\/>/i)?.[0];
    assert(countersTag, `${label}: ${path.relative(root, file)} has no TRX Counters element`);
    const attributes = Object.fromEntries(
      [...countersTag.matchAll(/(\w+)="(\d+)"/g)].map((match) => [match[1], Number(match[2])]),
    );
    assert(Number.isInteger(attributes.executed), `${label}: TRX executed count is missing`);
    assert(Number.isInteger(attributes.failed), `${label}: TRX failed count is missing`);
    executed += attributes.executed;
    failed += attributes.failed;
  }
  assert(executed > 0, `${label}: test filter matched zero executed tests`);
  assert(failed === 0, `${label}: TRX reports ${failed} failed test(s)`);
  console.log(`PASS | ${label} | ${executed} test(s) executed, 0 failed`);
}

function testProjectsInSolution(solution) {
  const solutionDirectory = path.dirname(path.join(root, solution));
  const projects = capture('dotnet', ['sln', solution, 'list'])
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.toLowerCase().endsWith('.csproj'))
    .map((project) => path.resolve(solutionDirectory, project.replaceAll('\\', path.sep)))
    .filter((project) =>
      /<IsTestProject>\s*true\s*<\/IsTestProject>/i.test(fs.readFileSync(project, 'utf8')),
    );
  assert(projects.length > 0, `${solution}: solution contains no declared test project`);
  return projects;
}

function testProjectResultsDirectory(resultsDirectory, project) {
  return path.join(resultsDirectory, path.basename(project, '.csproj'));
}

function withIdentityIntegrationSerialCollections(project, operation) {
  if (path.basename(project) !== 'VietRide.Identity.IntegrationTests.csproj') {
    return operation();
  }

  const projectXml = fs.readFileSync(project, 'utf8');
  const targetFramework = projectXml.match(
    /<TargetFramework>\s*([^<]+?)\s*<\/TargetFramework>/i,
  )?.[1];
  assert(
    targetFramework,
    `${project}: TargetFramework is required for temporary xUnit configuration`,
  );
  const configPath = path.join(
    path.dirname(project),
    'bin',
    'Release',
    targetFramework,
    'VietRide.Identity.IntegrationTests.xunit.runner.json',
  );
  const previous = fs.existsSync(configPath) ? fs.readFileSync(configPath) : null;
  fs.mkdirSync(path.dirname(configPath), { recursive: true });
  fs.writeFileSync(
    configPath,
    `${JSON.stringify({ parallelizeTestCollections: false, maxParallelThreads: 1 }, null, 2)}\n`,
  );
  try {
    return operation();
  } finally {
    if (previous === null) fs.rmSync(configPath, { force: true });
    else fs.writeFileSync(configPath, previous);
  }
}

function hasOnlyTransientIdentityDatabaseSetupFailures(resultsDirectory, testProjects) {
  const files = [];
  for (const project of testProjects) {
    const projectFiles = trxFiles(
      path.join(root, testProjectResultsDirectory(resultsDirectory, project)),
    );
    if (projectFiles.length !== 1) return false;
    files.push(projectFiles[0]);
  }
  let reportedFailures = 0;
  const failedResults = [];

  for (const file of files) {
    const xml = fs.readFileSync(file, 'utf8');
    const countersTag = xml.match(/<Counters\b[^>]*\/>/i)?.[0];
    const failed = Number(countersTag?.match(/\bfailed="(\d+)"/i)?.[1] ?? Number.NaN);
    if (!Number.isInteger(failed)) return false;
    reportedFailures += failed;
    failedResults.push(
      ...xml.matchAll(
        /<UnitTestResult\b(?=[^>]*\boutcome="Failed")[^>]*>[\s\S]*?<\/UnitTestResult>/gi,
      ),
    );
  }

  return (
    reportedFailures > 0 &&
    failedResults.length === reportedFailures &&
    failedResults.every(
      (match) =>
        match[0].includes('Timeout during reading attempt') ||
        (match[0].includes('42P04:') && match[0].includes('already exists')),
    )
  );
}

function runFullSolutionTestAttempt(resultsDirectory, testProjects) {
  let firstError;
  for (const project of testProjects) {
    const projectResultsDirectory = testProjectResultsDirectory(resultsDirectory, project);
    resetResultsDirectory(projectResultsDirectory);
    try {
      withIdentityIntegrationSerialCollections(project, () =>
        run('dotnet', [
          'test',
          project,
          '--no-build',
          '--configuration',
          'Release',
          '--logger',
          'trx;LogFileName=test-results.trx',
          '--results-directory',
          projectResultsDirectory,
        ]),
      );
    } catch (error) {
      firstError ??= error;
    }
  }
  if (firstError) throw firstError;
}

function runFullSolutionTests(solution, resultsDirectory) {
  const testProjects = testProjectsInSolution(solution);
  try {
    runFullSolutionTestAttempt(resultsDirectory, testProjects);
  } catch (error) {
    const mayRetry =
      solution === 'apps/identity/VietRide.Identity.sln' &&
      hasOnlyTransientIdentityDatabaseSetupFailures(resultsDirectory, testProjects);
    if (!mayRetry) throw error;

    console.log('RUN  | retry Identity full tests once after transient database setup contention');
    resetResultsDirectory(resultsDirectory);
    runFullSolutionTestAttempt(resultsDirectory, testProjects);
  }
  return testProjects.length;
}

function psql(database, sql) {
  return capture('docker', [
    'exec',
    'vietride_postgres',
    'psql',
    '-v',
    'ON_ERROR_STOP=1',
    '-U',
    'vietride',
    '-d',
    database,
    '-Atc',
    sql,
  ]);
}

function uuidKey() {
  const key = crypto.randomUUID();
  idempotencyKeys.add(key);
  return key;
}

function idempotencyRedisKeys(key) {
  const keyHash = crypto.createHash('sha256').update(key, 'utf8').digest('hex').toUpperCase();
  return [
    `trip:idem:${key}`,
    `booking:idem:${key}`,
    `idempotency:${key}`,
    `trip:idem:v2:response:${keyHash}`,
    `trip:idem:v2:processing:${keyHash}`,
    `booking:idem:v2:response:${keyHash}`,
    `booking:idem:v2:processing:${keyHash}`,
  ];
}

function parseJson(text, label) {
  try {
    return text ? JSON.parse(text) : null;
  } catch {
    throw new Error(`${label} returned non-JSON content`);
  }
}

async function request(method, pathname, { token, key, body } = {}) {
  const headers = { 'X-Request-Id': crypto.randomUUID() };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (key !== undefined) headers['Idempotency-Key'] = key;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const response = await fetch(`${gatewayBaseUrl}${pathname}`, {
    method,
    headers,
    body: body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body),
  });
  const text = await response.text();
  return { status: response.status, body: parseJson(text, pathname), raw: text };
}

function expect(result, status, code, label) {
  assert(result.status === status, `${label}: expected HTTP ${status}, got ${result.status}`);
  if (code)
    assert(
      result.body?.error?.code === code,
      `${label}: expected ${code}, got ${result.body?.error?.code}`,
    );
  if (status >= 400) {
    assert(
      result.body?.success === false,
      `${label}: error response is not in the ADR-0004 envelope`,
    );
    assert(
      typeof result.body?.meta?.traceId === 'string',
      `${label}: error response has no traceId`,
    );
  }
  console.log(`PASS | ${label} | HTTP ${status}${code ? ` ${code}` : ''}`);
}

function expectSameReplay(first, second, label) {
  assert(
    first.status === second.status && first.raw === second.raw,
    `${label}: replay was not byte-for-byte stable`,
  );
  console.log(`PASS | ${label}`);
}

function staticArtifactChecks() {
  const collectionPath = 'docs/api/postman/vietride.postman_collection.json';
  const environmentPath = 'docs/api/postman/vietride.local.postman_environment.json';
  JSON.parse(read(collectionPath));
  JSON.parse(read(environmentPath));
  console.log('PASS | Postman collection and environment parse as JSON');

  const taskCommit = capture('git', [
    'log',
    '--format=%H',
    '--grep=^docs: freeze day 22 trip edit contracts$',
    '-n',
    '1',
  ]);
  assert(taskCommit, 'Task 22.0 contract-freeze commit was not found');
  const taskFiles = capture('git', ['diff-tree', '--no-commit-id', '--name-only', '-r', taskCommit])
    .split(/\r?\n/)
    .filter(Boolean);
  const allowed = new Set([
    'VietRide_API_Contract_v1.md',
    'BACKEND_SOURCE_OF_TRUTH.md',
    'SU26SE101_VIETRIDE_technical_context_v7.md',
    'db-schema/trip-route-vehicle/schema.sql',
    'docs/handoff/day-22-plan.md',
  ]);
  assert(
    taskFiles.every((file) => allowed.has(file)),
    `Task 22.0 changed an out-of-bound artifact: ${taskFiles.filter((file) => !allowed.has(file)).join(', ')}`,
  );
  assert(
    !taskFiles.some((file) => /(^|\/)AGENTS\.md$/i.test(file)),
    'Task 22.0 contains AGENTS.md',
  );
  assert(!capture('git', ['ls-files', 'AGENTS.md']), 'Ignored local AGENTS.md is tracked');
  const staged = capture('git', ['diff', '--cached', '--name-only']);
  assert(!/(^|\r?\n)AGENTS\.md(\r?\n|$)/i.test(staged), 'AGENTS.md is staged');
  console.log('PASS | Task 22.0 artifact boundary excludes ignored local AGENTS.md');

  const schemaPatch = capture('git', [
    'show',
    '--format=',
    '--unified=0',
    taskCommit,
    '--',
    'db-schema/trip-route-vehicle/schema.sql',
  ]);
  const changedSchemaLines = schemaPatch
    .split(/\r?\n/)
    .filter((line) => /^[+-]/.test(line) && !/^(?:\+\+\+|---)/.test(line));
  const approvedSchemaPatch = [
    "-    'Static baseline. NEVER updated after Trip generate. Dynamic ETA lives in Redis only.';",
    "+    'Static planned baseline. An approved pre-departure Route edit or DriverSchedule ALL_PENDING cascade may recompute it; GPS/Tracking dynamic ETA never updates this column.';",
  ];
  assert(
    JSON.stringify(changedSchemaLines) === JSON.stringify(approvedSchemaPatch),
    `Task 22.0 schema patch differs from the complete two-line whitelist: ${changedSchemaLines.join(' | ')}`,
  );
  assert(
    !/^[+-][^+-].*\b(CREATE|ALTER|DROP|ADD\s+(?:COLUMN|CONSTRAINT)|DROP\s+(?:COLUMN|CONSTRAINT))\b/im.test(
      schemaPatch,
    ),
    'Task 22.0 schema patch contains a non-comment schema operation',
  );
  console.log(
    'PASS | Task 22.0 schema diff is comment-only and contains no DDL/column/constraint change',
  );

  const contractPaths = [
    'VietRide_API_Contract_v1.md',
    'BACKEND_SOURCE_OF_TRUTH.md',
    'SU26SE101_VIETRIDE_technical_context_v7.md',
  ];
  const contracts = contractPaths.map(read).join('\n');
  const statements = [
    ...contractStatements(contracts),
    ...contractPaths.flatMap((contractPath) => contractSections(read(contractPath))),
  ];
  const hasAll = (statement, patterns) => patterns.every((pattern) => pattern.test(statement));
  const isNegated = (statement) =>
    /\b(?:no|not|never|without|non-authoritative|forbidden|reject(?:s|ed)?|absent)\b|không|chỉ\s+đọc/i.test(
      statement,
    );

  assertNoAffirmativeStatement(
    statements,
    'Affirmative floor-to-1000 wording remains',
    (statement) => /floor/i.test(statement) && /1[,.]?000/.test(statement),
    isNegated,
  );
  assertNoAffirmativeStatement(
    statements,
    'New TEMPLATE_SNAPSHOT creation/generation is still claimed',
    (statement) =>
      hasAll(statement, [
        /TEMPLATE_SNAPSHOT/i,
        /creat|generat|persist|write|insert|copy|snapshot row|tạo|ghi/i,
      ]),
    (statement) =>
      /\b(?:no|not|never|without|does not|do not)\b.{0,100}(?:creat|generat|persist|write|insert|copy)|(?:creat|generat|persist|write|insert|copy).{0,100}\b(?:no|not|never)\b|không.{0,60}(?:tạo|ghi|copy)|chỉ.{0,60}(?:legacy|readable|đọc)/i.test(
        statement,
      ),
  );
  assertNoAffirmativeStatement(
    statements,
    'TEMPLATE_SNAPSHOT authority under explicit pricingAt remains',
    (statement) =>
      hasAll(statement, [
        /TEMPLATE_SNAPSHOT/i,
        /pricingAt/i,
        /authoritative|precedence|priority|ưu tiên|resolve|resolution/i,
      ]),
    (statement) =>
      /non-authoritative|not authoritative|không.{0,30}(?:authoritative|ưu tiên)|legacy.{0,80}(?:omit|without)|omit(?:ted)? pricingAt|without (?:it|pricingAt)/i.test(
        statement,
      ),
  );
  assertNoAffirmativeStatement(
    statements,
    'A non-override flow still creates MANUAL_OVERRIDE',
    (statement) =>
      /(?:MANUAL_OVERRIDE.{0,180}(?:creat|persist|write|insert|tạo|ghi)|(?:creat|persist|write|insert|tạo|ghi).{0,180}MANUAL_OVERRIDE)/i.test(
        statement,
      ) &&
      /non-override|automatic|generation|template resolution|legacy|không phải override/i.test(
        statement,
      ),
    (statement) =>
      /only.{0,100}(?:explicit|operator|per-Trip).{0,80}(?:creat|persist|write)|chỉ.{0,100}(?:explicit|operator|override).{0,80}(?:tạo|ghi)|(?:do not|does not|never|không).{0,80}(?:creat|persist|write|tạo|ghi)/i.test(
        statement,
      ),
  );
  assertNoAffirmativeStatement(
    statements,
    'Trip PATCH still permits departureDateTime',
    (statement) =>
      hasAll(statement, [
        /Trip PATCH/i,
        /departureDateTime/i,
        /allow|edit|accept|permit|cho phép|nhận/i,
      ]),
    isNegated,
  );
  assertNoAffirmativeStatement(
    statements,
    'Schedule facts are still claimed for non-CONFIRMED Bookings',
    (statement) =>
      hasAll(statement, [
        /non-?CONFIRMED|status khác CONFIRMED/i,
        /schedule_change_(?:informational|required)/i,
        /publish|emit|produce|tạo|phát/i,
      ]),
    isNegated,
  );
  assertNoAffirmativeStatement(
    statements,
    'Day-22 ownership still replaces existing route-change behavior',
    (statement) =>
      hasAll(statement, [
        /Day-?22|ownership/i,
        /route[_ -]?change/i,
        /replace|alter|disable|remove|thay thế|loại bỏ/i,
      ]),
    isNegated,
  );

  const normalizedContracts = normalizeStatement(contracts);
  const required = [
    /Money\.FromRaw.{0,120}pass-through/i,
    /MANUAL_OVERRIDE.{0,400}active.{0,240}RouteStopFareTemplate.{0,400}Trip\.baseFare/i,
    /Day 22.{0,160}(?:no new|does not create|không tạo).{0,100}TEMPLATE_SNAPSHOT/i,
    /departureDateTime.{0,240}ALL_PENDING|ALL_PENDING.{0,240}departureDateTime/i,
    /schedule_change_informational/i,
    /physical duplicate jobs/i,
    /does not replace or alter the existing.{0,140}route_changed/i,
  ];
  for (const pattern of required)
    assert(
      pattern.test(normalizedContracts),
      `Required Task-22 contract statement is missing: ${pattern}`,
    );
  console.log(
    'PASS | Task 22.0 contracts contain the approved pricing/edit/ownership/Hangfire rules and no known stale contradiction',
  );
}

function cleanupRedis() {
  if (idempotencyKeys.size === 0) return;
  const redisKeys = [...idempotencyKeys].flatMap(idempotencyRedisKeys);
  execFileSync('docker', ['exec', 'vietride_redis', 'redis-cli', 'DEL', ...redisKeys], {
    encoding: 'utf8',
  });
}

function ownedOutboxPredicate() {
  return `event_type IN ('trip.trip.vehicle_swapped', 'trip.trip.route_changed', 'trip.trip.schedule_changed', 'trip.trip.cancelled')
    AND (payload->>'tripId' IN ('${ids.trip}', '${ids.otherTrip}')
      OR payload->>'driverScheduleId' IN ('${ids.schedule}', '${ids.otherSchedule}'))`;
}

function cleanup() {
  const operations = [
    () =>
      psql(
        'vietride_parcel',
        `
      DELETE FROM vietride_parcel.outbox_events
      WHERE payload->>'parcelId' = '${ids.parcel}'
         OR payload->>'tripId' = '${ids.trip}';
      DELETE FROM vietride_parcel.parcel_stats WHERE operator_id = '${ids.operator}';
      DELETE FROM vietride_parcel.parcels WHERE id = '${ids.parcel}';`,
      ),
    () =>
      psql(
        'vietride_booking',
        `
      DELETE FROM vietride_booking.booking_stats_processed_events
      WHERE booking_id IN (
        SELECT id FROM vietride_booking.bookings
        WHERE passenger_user_id = '${ids.passenger}' OR trip_id = '${ids.trip}'
      );
      DELETE FROM vietride_booking.booking_stats WHERE operator_id = '${ids.operator}';
      DELETE FROM vietride_booking.outbox_events
      WHERE payload->>'userId' = '${ids.passenger}'
         OR payload->>'tripId' = '${ids.trip}'
         OR payload->>'bookingId' IN (
           SELECT id::text FROM vietride_booking.bookings
           WHERE passenger_user_id = '${ids.passenger}' OR trip_id = '${ids.trip}'
         );
      DELETE FROM vietride_booking.booking_status_history
      WHERE booking_id IN (
        SELECT id FROM vietride_booking.bookings
        WHERE passenger_user_id = '${ids.passenger}' OR trip_id = '${ids.trip}'
      );
      DELETE FROM vietride_booking.bookings
      WHERE passenger_user_id = '${ids.passenger}' OR trip_id = '${ids.trip}';`,
      ),
    () => {
      psql(
        'vietride_payment',
        `
      DELETE FROM vietride_payment.refund_failure_logs
      WHERE user_id = '${ids.passenger}'
         OR booking_id IN (${[...bookingIds].map((id) => `'${id}'`).join(',') || 'NULL'});
      DELETE FROM vietride_payment.outbox_events
      WHERE payload->>'userId' = '${ids.passenger}'
         OR payload->>'bookingId' IN (${[...bookingIds].map((id) => `'${id}'`).join(',') || 'NULL'})
         OR payload->>'referenceId' IN (${[...bookingIds].map((id) => `'${id}'`).join(',') || 'NULL'});
      DELETE FROM vietride_payment.wallet_transactions WHERE user_id = '${ids.passenger}';
      DELETE FROM vietride_payment.platform_wallet_transactions
      WHERE reference_id IN (${[...bookingIds].map((id) => `'${id}'`).join(',') || 'NULL'});
      DELETE FROM vietride_payment.payments
      WHERE user_id = '${ids.passenger}'
         OR reference_id IN (${[...bookingIds].map((id) => `'${id}'`).join(',') || 'NULL'});
      DELETE FROM vietride_payment.wallets WHERE user_id = '${ids.passenger}';`,
      );
      if (platformWalletBaseline?.createdByRun) {
        psql(
          'vietride_payment',
          `DELETE FROM vietride_payment.platform_wallets WHERE id = '${platformWalletBaseline.id}';`,
        );
      } else if (platformWalletBaseline) {
        psql(
          'vietride_payment',
          `UPDATE vietride_payment.platform_wallets
           SET balance = ${platformWalletBaseline.balance},
               row_version = ${platformWalletBaseline.rowVersion},
               updated_at = '${platformWalletBaseline.updatedAt}'::timestamptz
           WHERE id = '${platformWalletBaseline.id}';`,
        );
      }
    },
    () =>
      psql(
        'vietride_notification',
        `
      DELETE FROM vietride_notification.email_deliveries
      WHERE notification_id IN (
        SELECT id FROM vietride_notification.notifications WHERE user_id = '${ids.passenger}'
      );
      DELETE FROM vietride_notification.notifications WHERE user_id = '${ids.passenger}';`,
      ),
    () =>
      psql(
        'vietride_trip',
        `
      DELETE FROM vietride_trip.outbox_events WHERE ${ownedOutboxPredicate()};
      DELETE FROM vietride_trip.trip_audit_logs WHERE trip_id IN ('${ids.trip}', '${ids.otherTrip}');
      DELETE FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id IN ('${ids.schedule}', '${ids.otherSchedule}');
      DELETE FROM vietride_trip.trips WHERE id IN ('${ids.trip}', '${ids.otherTrip}');
      DELETE FROM vietride_trip.driver_schedules WHERE id IN ('${ids.schedule}', '${ids.otherSchedule}');
      DELETE FROM vietride_trip.routes WHERE id = '${ids.route}';
      DELETE FROM vietride_trip.vehicles WHERE id = '${ids.vehicle}';
      DELETE FROM vietride_trip.stations WHERE id IN ('${ids.originStation}', '${ids.destinationStation}');
      DELETE FROM vietride_trip.vehicle_types WHERE id = '${ids.vehicleType}';`,
      ),
    () =>
      psql(
        'vietride_identity',
        `
      DELETE FROM vietride_identity.users WHERE id IN ('${ids.admin}', '${ids.passenger}');
      DELETE FROM vietride_identity.operators WHERE id = '${ids.operator}';`,
      ),
    cleanupRedis,
  ];
  const errors = [];
  for (const operation of operations) {
    try {
      operation();
    } catch (error) {
      errors.push(error);
    }
  }
  if (errors.length) throw new AggregateError(errors, 'Day-22 cleanup failed');
}

function assertClean() {
  const notificationRemaining = Number(
    psql(
      'vietride_notification',
      `SELECT count(*) FROM vietride_notification.notifications WHERE user_id = '${ids.passenger}'`,
    ),
  );
  assert(
    notificationRemaining === 0,
    `Day-22 cleanup left ${notificationRemaining} owned Notification rows`,
  );
  const bookingCounts = psql(
    'vietride_booking',
    `
    SELECT
      (SELECT count(*) FROM vietride_booking.bookings
       WHERE passenger_user_id = '${ids.passenger}' OR trip_id = '${ids.trip}') || '|' ||
      (SELECT count(*) FROM vietride_booking.booking_stats WHERE operator_id = '${ids.operator}') || '|' ||
      (SELECT count(*) FROM vietride_booking.outbox_events
       WHERE payload->>'userId' = '${ids.passenger}' OR payload->>'tripId' = '${ids.trip}')`,
  );
  assert(
    bookingCounts === '0|0|0',
    `Day-22 cleanup left owned Booking rows: ${bookingCounts}`,
  );
  const paymentCounts = psql(
    'vietride_payment',
    `
    SELECT
      (SELECT count(*) FROM vietride_payment.wallets WHERE user_id = '${ids.passenger}') || '|' ||
      (SELECT count(*) FROM vietride_payment.wallet_transactions WHERE user_id = '${ids.passenger}') || '|' ||
      (SELECT count(*) FROM vietride_payment.payments WHERE user_id = '${ids.passenger}') || '|' ||
      (SELECT count(*) FROM vietride_payment.refund_failure_logs WHERE user_id = '${ids.passenger}') || '|' ||
      (SELECT count(*) FROM vietride_payment.outbox_events WHERE payload->>'userId' = '${ids.passenger}')`,
  );
  assert(
    paymentCounts === '0|0|0|0|0',
    `Day-22 cleanup left owned Payment rows: ${paymentCounts}`,
  );
  if (platformWalletBaseline?.createdByRun) {
    assert(
      psql(
        'vietride_payment',
        `SELECT count(*) FROM vietride_payment.platform_wallets WHERE id = '${platformWalletBaseline.id}'`,
      ) === '0',
      'Day-22 cleanup left the runner-created PlatformWallet',
    );
  } else if (platformWalletBaseline) {
    assert(
      psql(
        'vietride_payment',
        `SELECT balance::text || '|' || row_version::text
         FROM vietride_payment.platform_wallets WHERE id = '${platformWalletBaseline.id}'`,
      ) ===
        `${platformWalletBaseline.balance}|${platformWalletBaseline.rowVersion}`,
      'Day-22 cleanup did not restore the pre-run PlatformWallet state',
    );
  }
  const identityCounts = psql(
    'vietride_identity',
    `
    SELECT
      (SELECT count(*) FROM vietride_identity.users WHERE id IN ('${ids.admin}', '${ids.passenger}')) || '|' ||
      (SELECT count(*) FROM vietride_identity.operators WHERE id = '${ids.operator}')`,
  );
  assert(identityCounts === '0|0', `Day-22 cleanup left owned Identity rows: ${identityCounts}`);
  const parcelCounts = psql(
    'vietride_parcel',
    `
    SELECT
      (SELECT count(*) FROM vietride_parcel.parcels WHERE id = '${ids.parcel}') || '|' ||
      (SELECT count(*) FROM vietride_parcel.parcel_stats WHERE operator_id = '${ids.operator}') || '|' ||
      (SELECT count(*) FROM vietride_parcel.outbox_events
       WHERE payload->>'parcelId' = '${ids.parcel}'
          OR payload->>'tripId' = '${ids.trip}')`,
  );
  const parcelLabels = ['parcels', 'parcel_stats', 'parcel_outbox_events'];
  const parcelRemaining = parcelCounts.split('|').map(Number);
  assert(
    parcelRemaining.length === parcelLabels.length && parcelRemaining.every((count) => count === 0),
    `Day-22 cleanup left owned Parcel rows: ${parcelLabels.map((label, index) => `${label}=${parcelRemaining[index]}`).join(', ')}`,
  );
  const counts = psql(
    'vietride_trip',
    `
    SELECT
      (SELECT count(*) FROM vietride_trip.trips WHERE id IN ('${ids.trip}', '${ids.otherTrip}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.driver_schedules WHERE id IN ('${ids.schedule}', '${ids.otherSchedule}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.routes WHERE id = '${ids.route}') || '|' ||
      (SELECT count(*) FROM vietride_trip.vehicles WHERE id = '${ids.vehicle}') || '|' ||
      (SELECT count(*) FROM vietride_trip.stations WHERE id IN ('${ids.originStation}', '${ids.destinationStation}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.vehicle_types WHERE id = '${ids.vehicleType}') || '|' ||
      (SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id IN ('${ids.trip}', '${ids.otherTrip}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id IN ('${ids.schedule}', '${ids.otherSchedule}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.outbox_events WHERE ${ownedOutboxPredicate()})`,
  );
  const labels = [
    'trips',
    'driver_schedules',
    'routes',
    'vehicles',
    'stations',
    'vehicle_types',
    'trip_audit_logs',
    'driver_schedule_audit_logs',
    'outbox_events',
  ];
  const remaining = counts.split('|').map(Number);
  assert(
    remaining.length === labels.length && remaining.every((count) => count === 0),
    `Day-22 cleanup left owned rows: ${labels.map((label, index) => `${label}=${remaining[index]}`).join(', ')}`,
  );
  if (idempotencyKeys.size > 0) {
    const redisKeys = [...idempotencyKeys].flatMap(idempotencyRedisKeys);
    const redisRemaining = Number(
      capture('docker', ['exec', 'vietride_redis', 'redis-cli', 'EXISTS', ...redisKeys]),
    );
    assert(redisRemaining === 0, `Day-22 cleanup left ${redisRemaining} Redis idempotency keys`);
  }
}

function seed() {
  cleanup();
  psql(
    'vietride_identity',
    `
    INSERT INTO vietride_identity.operators
      (id, name, business_registration_number, tax_code, contact_email, contact_phone,
       registration_status, approved_at, cancellation_policy, is_active)
    VALUES
      ('${ids.operator}', 'Day 22 Operator ${runTag}', 'D22BR${runTag}', 'D22TAX${runTag}',
       'operator-${runTag.toLowerCase()}@day22.local', '0900000022', 'APPROVED', now(), '[]'::jsonb, true);
    INSERT INTO vietride_identity.users
      (id, email, display_name, role, status, operator_id)
    VALUES
      ('${ids.admin}', 'admin-${runTag.toLowerCase()}@day22.local', 'Day 22 Admin',
       'OPERATOR_ADMIN', 'ACTIVE', '${ids.operator}'),
      ('${ids.passenger}', 'passenger-${runTag.toLowerCase()}@day22.local', 'Day 22 Passenger',
       'PASSENGER', 'ACTIVE', NULL);`,
  );
  const existingPlatformWallet = psql(
    'vietride_payment',
    `SELECT id::text || '|' || balance::text || '|' || row_version::text || '|' || updated_at::text
     FROM vietride_payment.platform_wallets LIMIT 1`,
  );
  if (existingPlatformWallet) {
    const [id, balance, rowVersion, updatedAt] = existingPlatformWallet.split('|');
    platformWalletBaseline = {
      id,
      balance: Number(balance),
      rowVersion: Number(rowVersion),
      updatedAt,
      createdByRun: false,
    };
  } else {
    psql(
      'vietride_payment',
      `INSERT INTO vietride_payment.platform_wallets (id, balance, row_version)
       VALUES ('${ids.platformWallet}', 0, 0);`,
    );
    platformWalletBaseline = {
      id: ids.platformWallet,
      balance: 0,
      rowVersion: 0,
      updatedAt: psql(
        'vietride_payment',
        `SELECT updated_at::text FROM vietride_payment.platform_wallets WHERE id = '${ids.platformWallet}'`,
      ),
      createdByRun: true,
    };
  }
  psql(
    'vietride_payment',
    `INSERT INTO vietride_payment.wallets (user_id, balance, row_version)
     VALUES ('${ids.passenger}', 1000000, 0);`,
  );
  psql(
    'vietride_trip',
    `
    INSERT INTO vietride_trip.stations (id, name, slug, city, province)
    VALUES
      ('${ids.originStation}', 'Day 22 Origin ${runTag}', 'day22-origin-${runTag.toLowerCase()}', 'Ho Chi Minh City', 'Ho Chi Minh City'),
      ('${ids.destinationStation}', 'Day 22 Destination ${runTag}', 'day22-destination-${runTag.toLowerCase()}', 'Da Lat', 'Lam Dong');
    INSERT INTO vietride_trip.vehicle_types (id, code, display_name, default_seat_count, is_system_defined)
    VALUES ('${ids.vehicleType}', 'D22_${runTag}', 'Day 22 Standard ${runTag}', 2, false);
    INSERT INTO vietride_trip.routes
      (id, operator_id, name, origin_station_id, destination_station_id, base_fare, estimated_duration_minutes)
    VALUES ('${ids.route}', '${ids.operator}', 'Day 22 Route ${runTag}', '${ids.originStation}', '${ids.destinationStation}', 200000, 240);
    INSERT INTO vietride_trip.vehicles
      (id, operator_id, vehicle_type_id, license_plate, seat_layout_json, total_seats, status)
    VALUES ('${ids.vehicle}', '${ids.operator}', '${ids.vehicleType}', 'D22${runTag}',
      '{"version":1,"vehicleTypeCode":"STANDARD","totalSeats":2,"rows":1,"cols":2,"decks":1,"aisles":[],"seats":[{"seatNumber":"A01","row":1,"col":1,"deck":1,"seatType":"STANDARD","isEnabled":true},{"seatNumber":"A02","row":1,"col":2,"deck":1,"seatType":"VIP","isEnabled":true}]}', 2, 'ACTIVE');
    INSERT INTO vietride_trip.driver_schedules
      (id, operator_id, route_id, vehicle_id, driver_user_id, assistant_user_id, day_of_week, departure_time, valid_from, is_active)
    VALUES
      ('${ids.schedule}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.driver}', '${ids.assistant}', '[1,3,5]', '08:30:00', current_date, true),
      ('${ids.otherSchedule}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.otherAdmin}', NULL, '[2,4,6]', '09:30:00', current_date, false);
    INSERT INTO vietride_trip.trips
      (id, operator_id, route_id, vehicle_id, driver_user_id, assistant_user_id, departure_date_time, estimated_arrival_time, status, source, base_fare, notes)
    VALUES
      ('${ids.trip}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.driver}', '${ids.assistant}', now() + interval '10 days', now() + interval '10 days 4 hours', 'SCHEDULED', 'MANUAL', 200000, NULL),
      ('${ids.otherTrip}', '${ids.operator}', '${ids.route}', '${ids.vehicle}', '${ids.otherAdmin}', NULL, now() + interval '11 days', now() + interval '11 days 4 hours', 'SCHEDULED', 'MANUAL', 200000, NULL);
    INSERT INTO vietride_trip.trip_seats (trip_id, seat_number, seat_type, status)
    VALUES
      ('${ids.trip}', 'A01', 'STANDARD', 'AVAILABLE'),
      ('${ids.trip}', 'A02', 'VIP', 'AVAILABLE');`,
  );
  psql(
    'vietride_parcel',
    `
    INSERT INTO vietride_parcel.parcels
      (id, parcel_code, sender_user_id, recipient_name, recipient_phone, operator_id, trip_id,
       size_category, estimated_weight_kg, deposit_amount, status)
    VALUES
      ('${ids.parcel}', 'VRP-20260716-${runTag}', '${ids.admin}', 'Day 22 Recipient',
       '0900000022', '${ids.operator}', '${ids.trip}', 'SMALL', 1.00, 10000,
       'PENDING_PAYMENT');`,
  );
  console.log(
    'PASS | isolated Day-22 Identity, Wallet, Trip, DriverSchedule, and Parcel fixtures seeded',
  );
}

async function issueTokens() {
  const settings = JSON.parse(
    read('apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
  );
  const privateKey = await importPKCS8(
    process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey,
    'RS256',
  );
  const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
  async function token(sub, role, operatorId) {
    return new SignJWT({
      role,
      operatorId,
      email: `${role.toLowerCase()}@day22.local`,
      hasPhone: 'true',
    })
      .setProtectedHeader({ alg: 'RS256', kid })
      .setIssuer('vietride-identity')
      .setAudience('vietride-api')
      .setSubject(sub)
      .setIssuedAt()
      .setExpirationTime('15m')
      .sign(privateKey);
  }
  const values = await Promise.all([
    token(ids.admin, 'OPERATOR_ADMIN', ids.operator),
    token(ids.otherAdmin, 'OPERATOR_ADMIN', ids.operator),
    token(ids.staff, 'OPERATOR_STAFF', ids.operator),
    token(ids.passenger, 'PASSENGER'),
  ]);
  console.log('PASS | short-lived Day-22 JWTs generated at runtime (redacted)');
  return { admin: values[0], otherAdmin: values[1], staff: values[2], passenger: values[3] };
}

function rabbitCredentials() {
  const rabbitEnvironment = JSON.parse(
    capture('docker', ['inspect', '--format', '{{json .Config.Env}}', 'vietride_rabbitmq']),
  );
  const environment = Object.fromEntries(
    rabbitEnvironment.map((entry) => entry.split(/=(.*)/s).slice(0, 2)),
  );
  const username = environment.RABBITMQ_DEFAULT_USER;
  const password = environment.RABBITMQ_DEFAULT_PASS;
  assert(username && password, 'RabbitMQ management credentials are unavailable');
  return Buffer.from(`${username}:${password}`).toString('base64');
}

async function rabbitRequest(pathname, authorization, init = {}) {
  const response = await fetch(`${rabbitManagementBaseUrl}${pathname}`, {
    ...init,
    headers: {
      Authorization: `Basic ${authorization}`,
      ...(init.headers ?? {}),
    },
  });
  assert(
    response.ok,
    `RabbitMQ management request ${pathname} failed with HTTP ${response.status}`,
  );
  return response.json();
}

function requiredRabbitCounter(value, label) {
  assert(
    typeof value === 'number' && Number.isFinite(value),
    `RabbitMQ management API did not expose required ${label}; delivery safety cannot be proven`,
  );
  return value;
}

function rabbitCounterOrZero(value, label) {
  if (value === undefined || value === null) return 0;
  return requiredRabbitCounter(value, label);
}

async function rabbitQueueState(queue, authorization) {
  const pathname = `/api/queues/%2F/${encodeURIComponent(queue)}`;
  const response = await fetch(`${rabbitManagementBaseUrl}${pathname}`, {
    headers: { Authorization: `Basic ${authorization}` },
  });
  assert(
    response.ok,
    `RabbitMQ management request ${pathname} failed with HTTP ${response.status}`,
  );
  return response.json();
}

function targetQueueSnapshot(queue, state) {
  return {
    queue,
    messages: requiredRabbitCounter(state.messages, `${queue}.messages`),
    ready: requiredRabbitCounter(state.messages_ready, `${queue}.messages_ready`),
    unacknowledged: requiredRabbitCounter(
      state.messages_unacknowledged,
      `${queue}.messages_unacknowledged`,
    ),
    acknowledged: rabbitCounterOrZero(state.message_stats?.ack, `${queue}.message_stats.ack`),
    redelivered: rabbitCounterOrZero(
      state.message_stats?.redeliver,
      `${queue}.message_stats.redeliver`,
    ),
  };
}

function dlqSnapshot(queue, state) {
  const snapshot = {
    queue,
    consumers: requiredRabbitCounter(state.consumers, `${queue}.consumers`),
    messages: requiredRabbitCounter(state.messages, `${queue}.messages`),
    ready: requiredRabbitCounter(state.messages_ready, `${queue}.messages_ready`),
    unacknowledged: requiredRabbitCounter(
      state.messages_unacknowledged,
      `${queue}.messages_unacknowledged`,
    ),
    published:
      typeof state.message_stats?.publish === 'number' ? state.message_stats.publish : null,
  };
  assert(snapshot.consumers === 0, `${queue} has consumers, so DLQ drainage cannot be ruled out`);
  return snapshot;
}

function assertNoDeliveryFailure(baseline, current, delivery) {
  assert(
    current.redelivered === baseline.redelivered,
    `Parcel cancellation delivery ${delivery} was redelivered`,
  );
  for (const counter of ['messages', 'ready', 'unacknowledged']) {
    assert(
      current.dlq[counter] === baseline.dlq[counter],
      `Parcel cancellation delivery ${delivery} increased or drained ${baseline.dlq.queue}.${counter}`,
    );
  }
  if (baseline.dlq.published !== null || current.dlq.published !== null) {
    assert(
      current.dlq.published === baseline.dlq.published,
      `Parcel cancellation delivery ${delivery} increased ${baseline.dlq.queue}.message_stats.publish`,
    );
  }
}

async function parcelCancelledQueueEvidence(authorization) {
  const bindings = await rabbitRequest('/api/bindings/%2F', authorization);
  const queue = 'parcel.trip-cancelled';
  assert(
    bindings.some(
      (binding) =>
        binding.source === 'vietride.events' &&
        binding.routing_key === 'trip.trip.cancelled' &&
        binding.destination_type === 'queue' &&
        binding.destination === queue,
    ),
    `${queue} is not bound to trip.trip.cancelled on vietride.events`,
  );
  const state = await rabbitQueueState(queue, authorization);
  const deadLetterExchange = state.arguments?.['x-dead-letter-exchange'];
  const deadLetterRoutingKey = state.arguments?.['x-dead-letter-routing-key'];
  assert(
    typeof deadLetterExchange === 'string' && typeof deadLetterRoutingKey === 'string',
    `${queue} does not expose its dead-letter topology; rejection safety cannot be proven`,
  );
  const dlq = `${queue}.dlq`;
  assert(
    bindings.some(
      (binding) =>
        binding.source === deadLetterExchange &&
        binding.routing_key === deadLetterRoutingKey &&
        binding.destination_type === 'queue' &&
        binding.destination === dlq,
    ),
    `${dlq} is not bound to ${deadLetterExchange} with ${deadLetterRoutingKey}; rejection safety cannot be proven`,
  );
  return { queue, dlq };
}

async function parcelDeliverySnapshot(evidence, authorization) {
  const target = targetQueueSnapshot(
    evidence.queue,
    await rabbitQueueState(evidence.queue, authorization),
  );
  const dlq = dlqSnapshot(evidence.dlq, await rabbitQueueState(evidence.dlq, authorization));
  return { ...target, dlq };
}

async function publishParcelCancellation(authorization, delivery) {
  const payload = JSON.stringify({
    tripId: ids.trip,
    eventId: ids.parcelCancellationEvent,
    occurredAt: new Date().toISOString(),
  });
  const confirmation = await rabbitRequest(
    '/api/exchanges/%2F/vietride.events/publish',
    authorization,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        properties: {
          content_type: 'application/json',
          message_id: ids.parcelCancellationEvent,
        },
        routing_key: 'trip.trip.cancelled',
        payload,
        payload_encoding: 'string',
      }),
    },
  );
  assert(confirmation?.routed === true, `Parcel cancellation delivery ${delivery} was not routed`);
}

async function proveParcelCancellationIdempotency() {
  const authorization = rabbitCredentials();
  const evidence = await parcelCancelledQueueEvidence(authorization);
  for (let delivery = 1; delivery <= 2; delivery += 1) {
    const baseline = await parcelDeliverySnapshot(evidence, authorization);
    assert(
      baseline.messages === 0 && baseline.ready === 0 && baseline.unacknowledged === 0,
      `${evidence.queue} must be drained before Parcel cancellation delivery ${delivery}`,
    );
    await publishParcelCancellation(authorization, delivery);
    await poll(
      `trip.trip.cancelled delivery ${delivery} ACKed by Parcel without redelivery or dead-lettering`,
      async () => parcelDeliverySnapshot(evidence, authorization),
      (state) => {
        assertNoDeliveryFailure(baseline, state, delivery);
        return (
          state.messages === 0 &&
          state.ready === 0 &&
          state.unacknowledged === 0 &&
          state.acknowledged >= baseline.acknowledged + 1
        );
      },
    );
  }
  await poll(
    'Parcel cancellation is logically idempotent across duplicate EventId delivery',
    async () =>
      psql(
        'vietride_parcel',
        `
        SELECT
          (SELECT status::text || ':' || COALESCE(rejection_reason, '')
           FROM vietride_parcel.parcels WHERE id = '${ids.parcel}') || '|' ||
          (SELECT count(*) FROM vietride_parcel.outbox_events
           WHERE event_type = 'parcel.parcel.rejected'
             AND payload->>'parcelId' = '${ids.parcel}') || '|' ||
          (SELECT COALESCE(sum(total_rejected), 0)
           FROM vietride_parcel.parcel_stats WHERE operator_id = '${ids.operator}')`,
      ),
    (state) => state === 'REJECTED:TRIP_CANCELLED|1|1',
  );
  console.log(
    'PASS | one PENDING_PAYMENT -> REJECTED transition, one rejection Outbox event, and one rejected stat increment',
  );
}

async function liveGatewayChecks() {
  seed();
  const health = await fetch(`${gatewayBaseUrl}/health`);
  assert(health.ok, `Gateway health failed with HTTP ${health.status}`);
  console.log('PASS | Gateway health');
  const tokens = await issueTokens();
  const tripPath = `/v1/operator/trips/${ids.trip}`;

  expect(
    await request('PATCH', tripPath, { body: '{"unknown":true}' }),
    401,
    'AUTH_TOKEN_INVALID',
    'authentication precedes idempotency and MVC',
  );
  expect(
    await request('PATCH', tripPath, { token: tokens.staff, body: '{}' }),
    403,
    'FORBIDDEN',
    'authorization precedes idempotency and MVC',
  );
  expect(
    await request('PATCH', tripPath, { token: tokens.admin, body: '{}' }),
    422,
    'VALIDATION_ERROR',
    'missing key precedes MVC body validation',
  );
  expect(
    await request('PATCH', tripPath, { token: tokens.admin, key: 'not-a-uuid-v4', body: '{}' }),
    422,
    'VALIDATION_ERROR',
    'malformed key precedes MVC body validation',
  );

  const mvcKey = uuidKey();
  const invalidBody = await request('PATCH', tripPath, {
    token: tokens.admin,
    key: mvcKey,
    body: '{"departureDateTime":"2030-01-01T00:00:00Z"}',
  });
  expect(invalidBody, 422, 'VALIDATION_ERROR', 'unknown Trip PATCH field rejected by MVC');
  const invalidReplay = await request('PATCH', tripPath, {
    token: tokens.admin,
    key: mvcKey,
    body: '{"departureDateTime":"2030-01-01T00:00:00Z"}',
  });
  expectSameReplay(invalidBody, invalidReplay, 'reserved MVC 422 replayed');
  expect(
    await request('PATCH', tripPath, {
      token: tokens.admin,
      key: mvcKey,
      body: '{"departureDateTime":"2031-01-01T00:00:00Z"}',
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'changed invalid body mismatches before MVC',
  );
  expect(
    await request('PATCH', `/v1/operator/trips/${ids.otherTrip}`, {
      token: tokens.admin,
      key: mvcKey,
      body: '{"departureDateTime":"2030-01-01T00:00:00Z"}',
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'cross-path key reuse mismatches before MVC',
  );
  expect(
    await request('PATCH', tripPath, {
      token: tokens.otherAdmin,
      key: mvcKey,
      body: '{"departureDateTime":"2030-01-01T00:00:00Z"}',
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'cross-subject key reuse mismatches before MVC',
  );

  const noOpKey = uuidKey();
  const noOp = await request('PATCH', tripPath, {
    token: tokens.admin,
    key: noOpKey,
    body: { notes: null },
  });
  expect(noOp, 200, null, 'Trip PATCH same-value no-op');
  assert(
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id='${ids.trip}'`,
    ) === '0',
    'Trip no-op wrote an audit row',
  );
  assert(
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.outbox_events WHERE payload->>'tripId'='${ids.trip}'`,
    ) === '0',
    'Trip no-op wrote an Outbox row',
  );
  console.log('PASS | Trip no-op has no audit/event side effect');

  async function createWalletBooking(seatNumber, expectedFare, label) {
    const result = await request('POST', '/v1/bookings', {
      token: tokens.passenger,
      key: uuidKey(),
      body: {
        tripId: ids.trip,
        pickup: { stationId: ids.originStation },
        dropoff: { stationId: ids.destinationStation },
        seats: [{ seatNumber }],
        paymentMethod: 'WALLET',
      },
    });
    expect(result, 201, null, label);
    const bookingId = result.body?.data?.bookingId;
    assert(typeof bookingId === 'string', `${label}: response has no bookingId`);
    bookingIds.add(bookingId);
    assert(result.body?.data?.status === 'CONFIRMED', `${label}: booking is not CONFIRMED`);
    assert(result.body?.data?.totalAmount === expectedFare, `${label}: response fare mismatch`);
    assert(
      psql(
        'vietride_booking',
        `SELECT base_fare::text || '|' || discount_amount::text || '|' || total_amount::text || '|' || status::text
         FROM vietride_booking.bookings WHERE id = '${bookingId}'`,
      ) === `${expectedFare}|0|${expectedFare}|CONFIRMED`,
      `${label}: immutable Booking fare snapshot was not persisted exactly`,
    );
    return bookingId;
  }

  const oldBookingId = await createWalletBooking('A01', 200000, 'booking captures pre-edit fare');
  assert(
    psql(
      'vietride_payment',
      `SELECT balance::text FROM vietride_payment.wallets WHERE user_id = '${ids.passenger}'`,
    ) === '800000',
    'pre-edit booking did not debit the passenger wallet by 200000',
  );
  console.log('PASS | pre-edit Booking and Wallet persist the original 200000 VND snapshot');

  const updateKey = uuidKey();
  const updated = await request('PATCH', tripPath, {
    token: tokens.admin,
    key: updateKey,
    body: '{"notes":"  Day 22 verified  ","baseFare":211111}',
  });
  expect(updated, 200, null, 'canonical Trip PATCH scalar update');
  assert(
    psql(
      'vietride_trip',
      `SELECT base_fare::text || '|' || notes FROM vietride_trip.trips WHERE id='${ids.trip}'`,
    ) === '211111|Day 22 verified',
    'Trip scalar update was not normalized/persisted exactly',
  );
  assert(
    psql(
      'vietride_trip',
      `SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id='${ids.trip}' AND action='TRIP_EDITED'`,
    ) === '1',
    'Trip update did not write exactly one audit',
  );
  console.log('PASS | Trip update persists to-the-dong fare, trimmed notes, and one audit');

  const newBookingId = await createWalletBooking(
    'A02',
    211111,
    'booking captures post-edit fare',
  );
  assert(
    psql(
      'vietride_booking',
      `SELECT base_fare::text || '|' || total_amount::text || '|' || status::text
       FROM vietride_booking.bookings WHERE id = '${oldBookingId}'`,
    ) === '200000|200000|CONFIRMED',
    'Trip fare edit mutated the pre-existing Booking snapshot',
  );
  assert(
    psql(
      'vietride_payment',
      `SELECT balance::text FROM vietride_payment.wallets WHERE user_id = '${ids.passenger}'`,
    ) === '588889',
    'post-edit booking did not debit the new 211111 VND fare',
  );
  console.log('PASS | post-edit Booking uses 211111 VND while the old snapshot remains 200000 VND');

  const cancelled = await request('POST', `/v1/bookings/${oldBookingId}/cancel`, {
    token: tokens.passenger,
    key: uuidKey(),
    body: { reason: 'USER_INITIATED' },
  });
  expect(cancelled, 200, null, 'cancel pre-edit booking');
  assert(cancelled.body?.data?.refundAmount === 200000, 'cancellation preview repriced the old Booking');
  await poll(
    'refund completes from the immutable pre-edit Booking total',
    async () => {
      const bookingState = psql(
        'vietride_booking',
        `
        SELECT
          (SELECT status::text || '|' || base_fare::text || '|' || total_amount::text
           FROM vietride_booking.bookings WHERE id = '${oldBookingId}') || '|' ||
          (SELECT status::text || '|' || base_fare::text || '|' || total_amount::text
           FROM vietride_booking.bookings WHERE id = '${newBookingId}') || '|' ||
          (SELECT count(*) FROM vietride_booking.outbox_events
           WHERE event_type = 'booking.booking.cancelled'
             AND payload->>'bookingId' = '${oldBookingId}'
             AND payload->>'refundAmount' = '200000') || '|' ||
          (SELECT count(*) FROM vietride_booking.outbox_events
           WHERE event_type = 'booking.booking.refunded'
             AND payload->>'bookingId' = '${oldBookingId}'
             AND payload->>'amount' = '200000')`,
      );
      const paymentState = psql(
        'vietride_payment',
        `
        SELECT
          (SELECT balance::text FROM vietride_payment.wallets WHERE user_id = '${ids.passenger}') || '|' ||
          (SELECT count(*) FROM vietride_payment.wallet_transactions
           WHERE user_id = '${ids.passenger}' AND type = 'CREDIT'
             AND reference_type = 'BOOKING_REFUND' AND reference_id = '${oldBookingId}'
             AND amount = 200000) || '|' ||
          (SELECT count(*) FROM vietride_payment.platform_wallet_transactions
           WHERE type = 'DEBIT' AND reference_type = 'BOOKING_REFUND'
             AND reference_id = '${oldBookingId}' AND amount = 200000)`,
      );
      return `${bookingState}|${paymentState}`;
    },
    (state) =>
      state ===
      'REFUNDED|200000|200000|CONFIRMED|211111|211111|1|1|788889|1|1',
  );
  console.log(
    'PASS | old Booking refunded exactly 200000 VND; new Booking remains confirmed at 211111 VND',
  );

  const schedulePath = `/v1/operator/driver-schedules/${ids.schedule}`;
  const scheduleStateBefore = psql(
    'vietride_trip',
    `
    SELECT json_build_object(
      'vehicleId', vehicle_id,
      'driverUserId', driver_user_id,
      'assistantUserId', assistant_user_id,
      'departureTime', departure_time,
      'dayOfWeek', day_of_week,
      'isActive', is_active)::text
    FROM vietride_trip.driver_schedules WHERE id='${ids.schedule}'`,
  );
  const scheduleEffectsBefore = psql(
    'vietride_trip',
    `
    SELECT
      (SELECT count(*) FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id='${ids.schedule}') || '|' ||
      (SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id IN ('${ids.trip}', '${ids.otherTrip}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.outbox_events WHERE ${ownedOutboxPredicate()})`,
  );
  const queryKey = uuidKey();
  const queryBody = '{"departureTime":"08:30:00"}';
  const ordered = await request('PATCH', `${schedulePath}?probe=x&applyTo=FUTURE_ONLY`, {
    token: tokens.admin,
    key: queryKey,
    body: queryBody,
  });
  expect(ordered, 200, null, 'DriverSchedule same-value no-op');
  const reordered = await request('PATCH', `${schedulePath}?applyTo=FUTURE_ONLY&probe=x`, {
    token: tokens.admin,
    key: queryKey,
    body: queryBody,
  });
  expectSameReplay(ordered, reordered, 'query key reorder canonicalized');
  const scheduleStateAfter = psql(
    'vietride_trip',
    `
    SELECT json_build_object(
      'vehicleId', vehicle_id,
      'driverUserId', driver_user_id,
      'assistantUserId', assistant_user_id,
      'departureTime', departure_time,
      'dayOfWeek', day_of_week,
      'isActive', is_active)::text
    FROM vietride_trip.driver_schedules WHERE id='${ids.schedule}'`,
  );
  const scheduleEffectsAfter = psql(
    'vietride_trip',
    `
    SELECT
      (SELECT count(*) FROM vietride_trip.driver_schedule_audit_logs WHERE driver_schedule_id='${ids.schedule}') || '|' ||
      (SELECT count(*) FROM vietride_trip.trip_audit_logs WHERE trip_id IN ('${ids.trip}', '${ids.otherTrip}')) || '|' ||
      (SELECT count(*) FROM vietride_trip.outbox_events WHERE ${ownedOutboxPredicate()})`,
  );
  assert(
    scheduleStateAfter === scheduleStateBefore,
    'DriverSchedule no-op changed persisted schedule state',
  );
  assert(
    scheduleEffectsAfter === scheduleEffectsBefore,
    'DriverSchedule no-op wrote a schedule audit, Trip audit, or Outbox row',
  );
  console.log('PASS | DriverSchedule no-op preserves persisted state and all audit/event counts');
  expect(
    await request('PATCH', `${schedulePath}?applyTo=ALL_PENDING&probe=x`, {
      token: tokens.admin,
      key: queryKey,
      body: queryBody,
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'changed applyTo mismatches',
  );

  const emptyKey = uuidKey();
  const emptyQuery = await request('PATCH', `${schedulePath}?applyTo=FUTURE_ONLY&marker=`, {
    token: tokens.admin,
    key: emptyKey,
    body: queryBody,
  });
  expect(emptyQuery, 200, null, 'empty query value accepted');
  expect(
    await request('PATCH', `${schedulePath}?applyTo=FUTURE_ONLY`, {
      token: tokens.admin,
      key: emptyKey,
      body: queryBody,
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'empty query differs from absent',
  );

  const repeatedKey = uuidKey();
  const repeated = await request('PATCH', `${schedulePath}?applyTo=FUTURE_ONLY&tag=a&tag=b`, {
    token: tokens.admin,
    key: repeatedKey,
    body: queryBody,
  });
  expect(repeated, 200, null, 'repeated query values accepted');
  const reorderedRepeated = await request(
    'PATCH',
    `${schedulePath}?applyTo=FUTURE_ONLY&tag=b&tag=a`,
    { token: tokens.admin, key: repeatedKey, body: queryBody },
  );
  expectSameReplay(repeated, reorderedRepeated, 'idempotency v2 canonicalizes repeated values');

  const crewPathKey = uuidKey();
  const crewBody = { driverUserId: ids.driver, assistantUserId: ids.assistant };
  expect(
    await request('PATCH', `${schedulePath}?applyTo=ALL_PENDING`, {
      token: tokens.admin,
      key: crewPathKey,
      body: crewBody,
    }),
    200,
    null,
    'canonical crew-equivalent no-op',
  );
  expect(
    await request('PATCH', `${schedulePath}/crew?applyTo=ALL_PENDING`, {
      token: tokens.admin,
      key: crewPathKey,
      body: crewBody,
    }),
    422,
    'IDEMPOTENCY_KEY_MISMATCH',
    'deprecated crew alias differs only by request path',
  );

  const scheduleMvcKey = uuidKey();
  const invalidSchedule = await request('PATCH', `${schedulePath}?applyTo=ALL_PENDING`, {
    token: tokens.admin,
    key: scheduleMvcKey,
    body: { vehicleId: null },
  });
  expect(invalidSchedule, 422, 'VALIDATION_ERROR', 'ALL_PENDING null vehicle rejected');
  const invalidScheduleReplay = await request('PATCH', `${schedulePath}?applyTo=ALL_PENDING`, {
    token: tokens.admin,
    key: scheduleMvcKey,
    body: { vehicleId: null },
  });
  expectSameReplay(invalidSchedule, invalidScheduleReplay, 'reserved body/query 422 replayed');

  const invalidApplyToKey = uuidKey();
  const invalidApplyTo = await request('PATCH', `${schedulePath}?applyTo=NOT_A_SCOPE`, {
    token: tokens.admin,
    key: invalidApplyToKey,
    body: queryBody,
  });
  expect(
    invalidApplyTo,
    422,
    'VALIDATION_ERROR',
    'invalid applyTo rejected by reserved query/MVC path',
  );
  const invalidApplyToReplay = await request('PATCH', `${schedulePath}?applyTo=NOT_A_SCOPE`, {
    token: tokens.admin,
    key: invalidApplyToKey,
    body: queryBody,
  });
  expectSameReplay(
    invalidApplyTo,
    invalidApplyToReplay,
    'reserved invalid applyTo 422 replayed exactly',
  );
  await proveParcelCancellationIdempotency();
}

function focusedRegressionMatrix() {
  const dotnetFilters = [
    [
      'apps/trip/tests/VietRide.Trip.UnitTests/VietRide.Trip.UnitTests.csproj',
      'FullyQualifiedName~EditTrip|FullyQualifiedName~TripVehicleSwap|FullyQualifiedName~GetTripSnapshotPricing|FullyQualifiedName~UpdateDriverSchedule|FullyQualifiedName~TripGenerationService',
    ],
    [
      'apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj',
      'FullyQualifiedName~EditTripEndpoint|FullyQualifiedName~UpdateDriverScheduleEndpoint|FullyQualifiedName~TripVehicleSwapService|FullyQualifiedName~TripStopFareSource|FullyQualifiedName~RouteStopFareTemplate|FullyQualifiedName~GetTripSnapshotRelational',
    ],
    [
      'apps/booking/tests/VietRide.Booking.UnitTests/VietRide.Booking.UnitTests.csproj',
      'FullyQualifiedName~CreateBookingCommandHandlerTests|FullyQualifiedName~CreateRoundTripBookingCommandHandlerTests|FullyQualifiedName~TripServiceClientTests|FullyQualifiedName~CancelBooking|FullyQualifiedName~CancellationRefundCalculator|FullyQualifiedName~HandleVehicleSwap|FullyQualifiedName~HandleScheduleChange|FullyQualifiedName~HandleTripCancelled|FullyQualifiedName~PendingActionRealert',
    ],
    [
      'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj',
      'FullyQualifiedName~TripVehicleSwapped|FullyQualifiedName~TripScheduleChanged|FullyQualifiedName~TripCancelled|FullyQualifiedName~BookingHangfire',
    ],
    [
      'apps/payment/tests/VietRide.Payment.UnitTests/VietRide.Payment.UnitTests.csproj',
      'FullyQualifiedName~BookingCancelledIntegrationEventHandlerTests|FullyQualifiedName~RefundFailureRetryJobTests|FullyQualifiedName~MarkPaymentRefundedCommandHandlerTests|FullyQualifiedName~RefundToWalletCommandHandlerTests',
    ],
    [
      'apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj',
      'FullyQualifiedName~BookingCancelledConsumerRegistrationTests|FullyQualifiedName~InternalWalletRefundEndpointTests',
    ],
  ];
  for (const [project, filter] of dotnetFilters) {
    const projectName = path.basename(project, '.csproj');
    const resultsDirectory = `TestResults/day22/focused/${projectName}`;
    resetResultsDirectory(resultsDirectory);
    run('dotnet', [
      'test',
      project,
      '--configuration',
      'Release',
      '--filter',
      filter,
      '--logger',
      'trx;LogFileName=test-results.trx',
      '--results-directory',
      resultsDirectory,
      '-p:NuGetAudit=false',
    ]);
    assertTrxExecuted(resultsDirectory, `${projectName} focused filter`);
  }
  runNpx([
    'jest',
    '--config',
    'libs/shared/contracts/jest.config.cts',
    '--runInBand',
    'libs/shared/contracts/src/events/__tests__/day22-trip-edit-events.spec.ts',
  ]);
  runNpx(['nx', 'test', 'notification', '--runInBand']);
  runNpx(['nx', 'run', 'notification:test:e2e', '--runInBand']);
  console.log('PASS | focused Day-22 and existing route-change Notification regression matrix');
}

function applicationContainerState(container) {
  return JSON.parse(capture('docker', ['inspect', '--format', '{{json .State}}', container]));
}

async function startApplicationContainers(containers) {
  const failures = [];
  for (const container of [...containers].reverse()) {
    try {
      run('docker', ['start', container]);
    } catch (error) {
      failures.push(`${container}: ${error.message}`);
    }
  }
  assert(failures.length === 0, `Unable to restart application containers: ${failures.join('; ')}`);
  await poll(
    'application containers healthy after the full matrix',
    () => containers.map(applicationContainerState),
    (states) =>
      states.every((state) => state.Running && !state.Paused && state.Health?.Status === 'healthy'),
    120_000,
  );
  console.log(`PASS | restored ${containers.length} application containers after the full matrix`);
}

async function stopRunningApplicationContainers() {
  const stoppedByThisRun = [];
  try {
    for (const container of applicationContainers) {
      const state = applicationContainerState(container);
      if (state.Running && !state.Paused) {
        run('docker', ['stop', '--timeout', '10', container]);
        stoppedByThisRun.push(container);
      }
    }
  } catch (error) {
    try {
      await startApplicationContainers(stoppedByThisRun);
    } catch (restoreError) {
      throw new AggregateError(
        [error, restoreError],
        'Stopping application containers failed and one or more stopped containers could not be restored',
      );
    }
    throw error;
  }
  console.log(
    `PASS | stopped ${stoppedByThisRun.length} application containers for the full matrix`,
  );
  return stoppedByThisRun;
}

async function fullBuildTestMatrix() {
  const stoppedContainers = await stopRunningApplicationContainers();
  let matrixError;
  let restoreError;
  try {
    const solutions = [
      'libs/dotnet/VietRide.Libs.sln',
      'apps/identity/VietRide.Identity.sln',
      'apps/trip/VietRide.Trip.sln',
      'apps/booking/VietRide.Booking.sln',
      'apps/payment/VietRide.Payment.sln',
      'apps/parcel/VietRide.Parcel.sln',
    ];
    for (const solution of solutions) {
      const solutionName = path.basename(solution, '.sln');
      const resultsDirectory = `TestResults/day22/full/${solutionName}`;
      resetResultsDirectory(resultsDirectory);
      run('dotnet', ['restore', solution]);
      run('dotnet', ['build', solution, '--no-restore', '--configuration', 'Release']);
      run('dotnet', [
        'format',
        solution,
        '--verify-no-changes',
        '--no-restore',
        '--verbosity',
        'minimal',
      ]);
      const testProjectCount = runFullSolutionTests(solution, resultsDirectory);
      assertTrxExecuted(resultsDirectory, `${solutionName} full test hook`, testProjectCount);
    }
    runNpx(['nx', 'run-many', '--target=build', '--all', '--parallel=3', '--exclude=VietRide.*']);
    runNpx(['nx', 'run-many', '--target=lint', '--all', '--parallel=3', '--exclude=VietRide.*']);
    runNpx([
      'nx',
      'run-many',
      '--target=test',
      '--all',
      '--parallel=3',
      '--exclude=VietRide.*',
      '--ci',
      '--passWithNoTests',
    ]);
    run('git', ['diff', '--check']);
    console.log('PASS | full .NET/TS/static matrix');
  } catch (error) {
    matrixError = error;
  } finally {
    try {
      await startApplicationContainers(stoppedContainers);
    } catch (error) {
      restoreError = error;
    }
  }
  if (matrixError && restoreError) {
    throw new AggregateError(
      [matrixError, restoreError],
      'The full matrix failed and one or more stopped application containers could not be restored',
    );
  }
  if (matrixError) throw matrixError;
  if (restoreError) throw restoreError;
}

let runError;
let cleanupError;
try {
  staticArtifactChecks();
  if (!staticOnly) {
    await liveGatewayChecks();
    if (!skipTargeted) focusedRegressionMatrix();
    if (!skipDay21) run('node', ['scripts/run-day21-trip-lifecycle-local.mjs']);
    if (fullMatrix) await fullBuildTestMatrix();
  }
} catch (error) {
  runError = error;
} finally {
  if (!staticOnly) {
    try {
      cleanup();
      assertClean();
      console.log('PASS | Day-22 fixture cleanup verified');
    } catch (error) {
      cleanupError = error;
      console.error(`FAIL | Day-22 fixture cleanup | ${error.message}`);
    }
  }
}

if (runError) throw runError;
if (cleanupError) throw cleanupError;
if (staticOnly) {
  console.log(
    'DIAGNOSTIC/DEFERRED | Static gate passed; runtime, regression, and full matrix were not executed',
  );
} else if (skipTargeted || skipDay21) {
  const skipped = [
    skipTargeted ? 'focused Day-22 regressions' : null,
    skipDay21 ? 'Day-21 regression' : null,
  ]
    .filter(Boolean)
    .join(' and ');
  console.log(
    `DIAGNOSTIC/DEFERRED | Runtime diagnostics passed with ${skipped} skipped; this is not close-out evidence`,
  );
} else if (!fullMatrix) {
  console.log(
    'DEFERRED | Gateway, focused, and Day-21 phases passed; --full-matrix is still required for close-out',
  );
} else {
  console.log('PASS | Day-22 close-out verification completed with the full matrix');
}
