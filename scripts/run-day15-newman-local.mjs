import { execFileSync, spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { SignJWT, importPKCS8 } from 'jose';

const root = process.cwd();
const settings = JSON.parse(fs.readFileSync(path.join(root, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'), 'utf8'));
const key = await importPKCS8(process.env.USER_JWT_PRIVATE_KEY || settings.IdentityJwt.PrivateKey, 'RS256');
const kid = process.env.USER_JWT_KID || settings.IdentityJwt.Kid;
const passengerId = crypto.randomUUID();
execFileSync(
  'docker',
  [
    'exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride',
    '-d', 'vietride_payment', '-c',
    `INSERT INTO vietride_payment.wallets (user_id, balance, currency)
     VALUES ('${passengerId}', 0, 'VND') ON CONFLICT (user_id) DO NOTHING`,
  ],
  { stdio: 'inherit' },
);
const accessToken = await new SignJWT({ role: 'PASSENGER', email: 'day15@e2e.local', hasPhone: 'true' })
  .setProtectedHeader({ alg: 'RS256', kid })
  .setIssuer('vietride-identity').setAudience('vietride-api').setSubject(passengerId)
  .setIssuedAt().setExpirationTime('15m').sign(key);
const secret = process.env.VNPAY_HASH_SECRET || 'sandbox-hash-secret-for-local-dev-only';
const args = [
  '--yes', 'newman', 'run', 'docs/api/postman/vietride.postman_collection.json',
  '-e', 'docs/api/postman/vietride.local.postman_environment.json',
  '--folder', process.platform === 'win32' ? '"Payment - Wallet top-up (Day 15)"' : 'Payment - Wallet top-up (Day 15)',
  '--env-var', `accessToken=${accessToken}`,
  '--env-var', `vnpayHashSecret=${secret}`,
  '--env-var', 'day15TopUpAmountVnd=100000',
];
const result = spawnSync('npx', args, { cwd: root, shell: process.platform === 'win32', stdio: 'inherit' });
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
