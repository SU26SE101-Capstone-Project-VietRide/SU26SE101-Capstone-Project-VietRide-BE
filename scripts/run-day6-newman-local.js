const childProcess = require('node:child_process');
const fs = require('node:fs');
const http = require('node:http');
const path = require('node:path');
const { pathToFileURL } = require('node:url');

require('dotenv').config({ path: path.resolve(process.cwd(), '.env') });

const { Client } = require('pg');

const repoRoot = process.cwd();
const collectionPath = path.join(repoRoot, 'docs/api/postman/vietride.postman_collection.json');
const environmentPath = path.join(
  repoRoot,
  'docs/api/postman/vietride.local.postman_environment.json',
);
const appSettingsPath = path.join(
  repoRoot,
  'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json',
);

const harnessHost = '127.0.0.1';
const harnessPort = Number(process.env.DAY6_HARNESS_PORT || 3056);
const schemaName = 'vietride_identity';

function postgresConfig() {
  return {
    host: process.env.POSTGRES_HOST || '127.0.0.1',
    port: Number(process.env.POSTGRES_PORT || 5432),
    database: process.env.IDENTITY_DB || 'vietride_identity',
    user: process.env.POSTGRES_USER || 'vietride',
    password: process.env.POSTGRES_PASSWORD || 'vietride_dev',
  };
}

function jsonResponse(res, statusCode, body) {
  res.writeHead(statusCode, { 'content-type': 'application/json; charset=utf-8' });
  res.end(JSON.stringify(body));
}

function textResponse(res, statusCode, body) {
  res.writeHead(statusCode, { 'content-type': 'text/plain; charset=utf-8' });
  res.end(body);
}

function makePhone(prefix, tail) {
  return `+84${prefix}${tail}`;
}

function makeRunVariables() {
  const now = Date.now().toString();
  const suffix = `${now.slice(-8)}${Math.floor(Math.random() * 90 + 10)}`;
  const tail = suffix.slice(-7).padStart(7, '0');

  return {
    day6RunId: suffix,
    operatorSelfRegisterEmail: `operator-self-${suffix}@example.com`,
    operatorSelfRegisterPhone: makePhone('91', tail),
    operatorSelfRegisterPassword: 'Operator123!',
    operatorSelfRegisterName: `VietRide Self Register Operator ${suffix}`,
    operatorBusinessRegistrationNumber: `BRN-${suffix}-SELF`,
    operatorTaxCode: `TAX-${suffix}-SELF`,
    operatorRepresentativePhone: makePhone('92', tail),
    adminCreatedOperatorEmail: `operator-admin-${suffix}@example.com`,
    adminCreatedOperatorPhone: makePhone('93', tail),
    adminCreatedOperatorBusinessRegistrationNumber: `BRN-${suffix}-ADMIN`,
    adminCreatedOperatorTaxCode: `TAX-${suffix}-ADMIN`,
    adminCreatedOperatorRepresentativePhone: makePhone('94', tail),
    adminOperatorPassword: 'OperatorAdmin123!',
    operatorUserEmail: `operator-staff-${suffix}@example.com`,
    operatorUserPhone: makePhone('95', tail),
    operatorUserRole: 'OPERATOR_STAFF',
    operatorUserPassword: 'OperatorUser123!',
  };
}

function readDevJwtOptions() {
  const appSettings = JSON.parse(fs.readFileSync(appSettingsPath, 'utf8'));
  const identityJwt = appSettings.IdentityJwt || {};
  const privateKey = process.env.USER_JWT_PRIVATE_KEY || identityJwt.PrivateKey;
  const kid = process.env.USER_JWT_KID || identityJwt.Kid;

  if (!privateKey || !kid) {
    throw new Error(
      'IdentityJwt dev private key/kid not found. Run against Development config or set USER_JWT_PRIVATE_KEY/USER_JWT_KID.',
    );
  }

  return { privateKey, kid };
}

async function ensureSystemAdmin(client) {
  const active = await client.query(
    `select id, email, phone
       from ${schemaName}.users
      where role = 'SYSTEM_ADMIN'::user_role
        and status = 'ACTIVE'::user_status
        and deleted_at is null
      order by created_at asc
      limit 1`,
  );

  if (active.rows[0]) {
    return active.rows[0];
  }

  // Local dev DB only: create an ACTIVE SYSTEM_ADMIN row when the database has none
  // so the harness can mint a JWT whose sub satisfies ActivityLog FK constraints.
  const inserted = await client.query(
    `insert into ${schemaName}.users (email, password_hash, display_name, role, status)
     values ($1, null, $2, 'SYSTEM_ADMIN'::user_role, 'ACTIVE'::user_status)
     returning id, email, phone`,
    [`day6-local-harness-admin-${Date.now()}@example.test`, 'Day 6 Local Harness Admin'],
  );

  return inserted.rows[0];
}

async function issueSystemAdminToken(client) {
  const { SignJWT, importPKCS8 } = await import('jose');
  const { privateKey, kid } = readDevJwtOptions();
  const admin = await ensureSystemAdmin(client);
  const key = await importPKCS8(privateKey, 'RS256');

  return new SignJWT({
    role: 'SYSTEM_ADMIN',
    email: admin.email,
    hasPhone: admin.phone ? 'true' : 'false',
  })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(admin.id)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}

async function latestEmailToken(client, email, purpose) {
  const result = await client.query(
    `select evt.code, evt.expires_at
       from ${schemaName}.email_verification_tokens evt
       join ${schemaName}.users u on u.id = evt.user_id
      where lower(u.email) = lower($1)
        and evt.purpose = $2::email_verification_purpose
        and evt.used_at is null
        and evt.expires_at > now()
      order by evt.created_at desc
      limit 1`,
    [email, purpose],
  );

  return result.rows[0] || null;
}

async function createServer(client) {
  const server = http.createServer(async (req, res) => {
    try {
      const url = new URL(req.url || '/', `http://${harnessHost}:${harnessPort}`);

      if (req.method === 'GET' && url.pathname === '/health') {
        jsonResponse(res, 200, { ok: true });
        return;
      }

      if (req.method === 'GET' && url.pathname === '/day6/bootstrap') {
        const systemAdminAccessToken = await issueSystemAdminToken(client);
        jsonResponse(res, 200, {
          systemAdminAccessToken,
          run: makeRunVariables(),
        });
        return;
      }

      if (req.method === 'GET' && url.pathname === '/identity/email-token') {
        const email = url.searchParams.get('email');
        const purpose = url.searchParams.get('purpose');

        if (!email || !purpose) {
          jsonResponse(res, 400, { error: 'email and purpose are required' });
          return;
        }

        const token = await latestEmailToken(client, email, purpose);
        if (!token) {
          jsonResponse(res, 404, { error: 'token not found', email, purpose });
          return;
        }

        jsonResponse(res, 200, { code: token.code, expiresAt: token.expires_at });
        return;
      }

      textResponse(res, 404, 'not found');
    } catch (error) {
      jsonResponse(res, 500, {
        error: error instanceof Error ? error.message : String(error),
      });
    }
  });

  await new Promise((resolve, reject) => {
    server.once('error', reject);
    server.listen(harnessPort, harnessHost, resolve);
  });

  return server;
}

function quoteCommandArg(value) {
  return `"${String(value).replace(/"/g, '""')}"`;
}

async function runNewman() {
  const args = [
    'newman',
    'run',
    collectionPath,
    '-e',
    environmentPath,
    '--folder',
    'Identity — Operator + Subscription (Day 6)',
    '--env-var',
    'localHarnessEnabled=true',
    '--env-var',
    `localHarnessUrl=http://${harnessHost}:${harnessPort}`,
  ];

  if (process.platform !== 'win32') {
    const child = childProcess.spawn('npx', args, { stdio: 'inherit' });
    return new Promise((resolve) => {
      child.on('exit', (code) => resolve(code || 0));
    });
  }

  const npxCmd = fs.existsSync('C:\\Program Files\\nodejs\\npx.cmd')
    ? 'C:\\Program Files\\nodejs\\npx.cmd'
    : 'npx.cmd';
  const command = [npxCmd, ...args].map(quoteCommandArg).join(' ');
  const child = childProcess.exec(command, { maxBuffer: 20 * 1024 * 1024 });
  child.stdout?.pipe(process.stdout);
  child.stderr?.pipe(process.stderr);

  return new Promise((resolve) => {
    child.on('exit', (code) => resolve(code || 0));
  });
}

async function main() {
  const client = new Client(postgresConfig());
  await client.connect();

  const server = await createServer(client);
  console.log(`Day-6 local Newman harness listening at http://${harnessHost}:${harnessPort}`);

  try {
    const exitCode = await runNewman();
    process.exitCode = exitCode;
  } finally {
    await new Promise((resolve) => server.close(resolve));
    await client.end();
  }
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
