import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
const passengerId = process.env.DAY20_PASSENGER_ID || crypto.randomUUID();
function psql(sql) {
  return execFileSync(
    'docker',
    [
      'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
      '-d', 'vietride_payment', '-Atc', sql,
    ],
    { encoding: 'utf8' },
  );
}

function cleanup() {
  psql(`DELETE FROM vietride_payment.wallets WHERE user_id = '${passengerId}';`);
}

function assertClean() {
  const count = psql(`SELECT count(*) FROM vietride_payment.wallets WHERE user_id = '${passengerId}';`).trim();
  if (count !== '0') throw new Error(`Day-15 fixture cleanup failed: wallet rows=${count}`);
}

let runError;
try {
  const retainForDay16 = process.env.DAY15_RETAIN_FIXTURES === 'true';
  if (!retainForDay16) cleanup();
  psql(
    `INSERT INTO vietride_payment.wallets (user_id, balance, currency)
     VALUES ('${passengerId}', 0, 'VND') ON CONFLICT (user_id) DO NOTHING`,
  );
  const accessToken = process.env.DAY20_PASSENGER_ACCESS_TOKEN || await new SignJWT({ role: 'PASSENGER', email: 'day15@e2e.local', hasPhone: 'true' })
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(passengerId)
    .setIssuedAt().setExpirationTime('15m').sign(key);
  const secret = process.env.VNPAY_HASH_SECRET;
  if (!secret) throw new Error('VNPAY_HASH_SECRET is required for the VNPay E2E harness.');
  const args = [
    '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
    '-e', 'docs/api/postman/vietride.local.postman_environment.json',
    '--folder', process.platform === 'win32' ? '"Payment - Wallet top-up (Day 15)"' : 'Payment - Wallet top-up (Day 15)',
    '--env-var', `accessToken=${accessToken}`,
    '--env-var', `vnpayHashSecret=${secret}`,
    '--env-var', 'day15TopUpAmountVnd=100000',
  ];
  if (process.env.DAY15_FORCE_NEWMAN_FAILURE === 'true')
    throw new Error('Forced Day-15 Newman failure requested');
  const result = spawnSync('npx', args, { cwd: root, shell: process.platform === 'win32', stdio: 'inherit' });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Newman failed with status ${result.status ?? 1}`);
} catch (error) {
  runError = error;
} finally {
  if (process.env.DAY15_RETAIN_FIXTURES === 'true' && !runError) {
    console.log('PASS | D15 fixture handoff | wallet retained for the Day-16 journey');
  } else {
    cleanup();
    assertClean();
    console.log('PASS | D15 fixture cleanup | temporary wallet removed');
  }
}
if (runError) throw runError;
