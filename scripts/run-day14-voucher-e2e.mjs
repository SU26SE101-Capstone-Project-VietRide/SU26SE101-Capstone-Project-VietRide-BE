// Day-14 voucher-checkout E2E harness — drives the full voucher flow through the Gateway (:3000)
// against the Docker stack. Mirrors run-day13-newman-local.js: mints dev RS256 user tokens from
// the Identity Development signing key and relies on the Booking DevTrip/DevPayment stubs
// (BOOKING_TRIP_USE_DEV_STUB / BOOKING_PAYMENT_USE_DEV_STUB = true in docker-compose) so checkout
// runs without a real Trip/Payment setup.
//
// Usage: node scripts/run-day14-voucher-e2e.mjs   (stack must be up: docker compose --profile app up -d)
//
// Covers: admin create voucher, checkout discount, OPERATOR_FUNDED-without-consent rejection,
// usage-limit boundary, operator self-create + branch-(a) apply, role gate, oversight list,
// and the consent accept/reject lifecycle (branch-(b) apply, revoke, precondition, OPERATOR_STAFF block).

import fs from 'node:fs';
import path from 'node:path';
import { execFileSync, spawnSync } from 'node:child_process';
import { SignJWT, importPKCS8 } from 'jose';

const ROOT = process.cwd();
const BASE = process.env.GATEWAY_BASE_URL || 'http://localhost:3000';
const OP_ID = '11111111-1111-4111-8111-111111111111'; // DevTripServiceClient operatorId
const TRIP = '00000000-0000-4000-8000-000000000013'; // DevTripServiceClient single-trip (no return route)
const PICKUP = '44444444-4444-4444-8444-444444444444';

const app = JSON.parse(
  fs.readFileSync(
    path.join(ROOT, 'apps/identity/src/VietRide.Identity.Api/appsettings.Development.json'),
    'utf8',
  ),
);
const key = await importPKCS8(
  process.env.USER_JWT_PRIVATE_KEY || app.IdentityJwt.PrivateKey,
  'RS256',
);
const kid = process.env.USER_JWT_KID || app.IdentityJwt.Kid;

async function token({ sub, role, operatorId }) {
  const claims = { role, email: `${role}@test.local`, hasPhone: 'true' };
  if (operatorId) claims.operatorId = operatorId;
  return new SignJWT(claims)
    .setProtectedHeader({ alg: 'RS256', kid })
    .setIssuer('vietride-identity')
    .setAudience('vietride-api')
    .setSubject(sub)
    .setIssuedAt()
    .setExpirationTime('15m')
    .sign(key);
}
const guid = () => crypto.randomUUID();

async function call(method, p, tok, body, idem) {
  const headers = { 'Content-Type': 'application/json', Authorization: `Bearer ${tok}` };
  if (idem) headers['Idempotency-Key'] = guid();
  const res = await fetch(`${BASE}${p}`, {
    method,
    headers,
    body: body ? JSON.stringify(body) : undefined,
  });
  let json = null;
  try {
    json = await res.json();
  } catch {
    /* no body */
  }
  const result = { status: res.status, json };
  if (method === 'POST' && (p === '/v1/admin/vouchers' || p === '/v1/operator/vouchers') && json?.data?.id)
    voucherIds.add(json.data.id);
  if (method === 'POST' && p === '/v1/bookings' && json?.data?.id) bookingIds.add(json.data.id);
  return result;
}

const past = new Date(Date.now() - 86_400_000).toISOString();
const future = new Date(Date.now() + 30 * 86_400_000).toISOString();
const results = [];
const voucherIds = new Set();
const bookingIds = new Set();
const log = (label, ok, detail) => {
  results.push(ok);
  console.log(`${ok ? 'PASS' : 'FAIL'} | ${label} | ${detail}`);
};

function psql(sql) {
  return execFileSync(
    'docker',
    ['exec', 'vietride_postgres', 'psql', '-v', 'ON_ERROR_STOP=1', '-U', 'vietride', '-d', 'vietride_booking', '-Atc', sql],
    { encoding: 'utf8' },
  ).trim();
}

const sqlIds = (ids) => [...ids].map((id) => `'${id}'`).join(', ');

function cleanup() {
  if (bookingIds.size) {
    const ids = sqlIds(bookingIds);
    psql(`DELETE FROM vietride_booking.tickets WHERE booking_id IN (${ids});
DELETE FROM vietride_booking.passengers WHERE booking_id IN (${ids});
DELETE FROM vietride_booking.booking_status_history WHERE booking_id IN (${ids});
DELETE FROM vietride_booking.bookings WHERE id IN (${ids});`);
  }
  if (voucherIds.size) {
    const ids = sqlIds(voucherIds);
    psql(`DELETE FROM vietride_booking.operator_voucher_consents WHERE voucher_id IN (${ids});
DELETE FROM vietride_booking.voucher_usages WHERE voucher_id IN (${ids});
DELETE FROM vietride_booking.vouchers WHERE id IN (${ids});`);
  }
}

function assertClean() {
  if (bookingIds.size) {
    const count = psql(`SELECT count(*) FROM vietride_booking.bookings WHERE id IN (${sqlIds(bookingIds)});`);
    if (count !== '0') throw new Error(`Day-14 fixture cleanup failed: bookings=${count}`);
  }
  if (voucherIds.size) {
    const count = psql(`SELECT count(*) FROM vietride_booking.vouchers WHERE id IN (${sqlIds(voucherIds)});`);
    if (count !== '0') throw new Error(`Day-14 fixture cleanup failed: vouchers=${count}`);
  }
}

function readBookingMode() {
  const output = execFileSync(
    'docker',
    ['inspect', '--format', '{{range .Config.Env}}{{println .}}{{end}}', 'vietride_booking'],
    { encoding: 'utf8' },
  );
  return output.split(/\r?\n/).some((value) => value === 'BOOKING_TRIP_USE_DEV_STUB=true') ? 'stub' : 'real';
}

function composeBookingMode(mode) {
  const stub = mode === 'stub' ? 'true' : 'false';
  const result = spawnSync('docker', [
    'compose', '--env-file', '.env', '-f', 'infra/docker/docker-compose.yml', '--profile', 'app',
    'up', '-d', '--force-recreate', 'booking', 'gateway',
  ], {
    cwd: ROOT,
    env: {
      ...process.env,
      BOOKING_TRIP_USE_DEV_STUB: stub,
      BOOKING_PAYMENT_USE_DEV_STUB: stub,
      BOOKING_IDENTITY_USE_DEV_STUB: stub,
    },
    stdio: 'inherit',
  });
  if (result.error || result.status !== 0)
    throw new Error(`Day-14 could not start Booking/Gateway in ${mode} mode.`);
}

async function waitForGateway() {
  for (let attempt = 1; attempt <= 30; attempt += 1) {
    try {
      const response = await fetch(`${BASE}/health`);
      if (response.ok) return;
    } catch {
      // Gateway is still accepting the recreated container's socket.
    }
    await new Promise((resolve) => setTimeout(resolve, 1_000));
  }
  throw new Error('Day-14 Gateway did not become healthy after Booking stub-mode recreation.');
}

const adminVoucher = (over = {}) => ({
  name: 'E2E Voucher',
  type: 'FIXED_AMOUNT',
  value: 50_000,
  minOrderAmount: 0,
  maxDiscountAmount: null,
  totalUsageLimit: null,
  perUserLimit: null,
  validFrom: past,
  validUntil: future,
  applicableOperatorIds: null,
  applicableRouteIds: null,
  fundingType: 'VIETRIDE_FUNDED',
  ...over,
});
const booking = (seat, voucherCode) => {
  const b = {
    tripId: TRIP,
    pickup: { stationId: PICKUP },
    seats: [
      {
        seatNumber: seat,
        passenger: { fullName: 'E2E Pax', phoneNumber: '0900000001', idNumber: '012345678901' },
      },
    ],
    paymentMethod: 'WALLET',
  };
  if (voucherCode) b.voucherCode = voucherCode;
  return b;
};

const admin = await token({ sub: guid(), role: 'SYSTEM_ADMIN' });
const operatorAdmin = await token({ sub: guid(), role: 'OPERATOR_ADMIN', operatorId: OP_ID });

let runError;
let previousBookingMode;
try {
  previousBookingMode = readBookingMode();
  composeBookingMode('stub');
  await waitForGateway();
// 1. Admin create VIETRIDE_FUNDED voucher
let r = await call('POST', '/v1/admin/vouchers', admin, adminVoucher(), true);
const code1 = r.json?.data?.code;
log(
  '1. Admin create VIETRIDE_FUNDED voucher',
  r.status === 201 && r.json?.data?.ownerOperatorId === null && !!code1,
  `status=${r.status} code=${code1}`,
);

// 2. Passenger checkout with voucher reduces total (200000 - 50000 = 150000)
let pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('A01', code1), true);
log(
  '2. Booking with voucher reduces total',
  r.status === 201 &&
    r.json?.data?.discountAmount === 50_000 &&
    r.json?.data?.totalAmount === 150_000,
  `status=${r.status} discount=${r.json?.data?.discountAmount} total=${r.json?.data?.totalAmount}`,
);

// 3. Admin OPERATOR_FUNDED voucher (no consent) -> rejected at checkout
r = await call(
  'POST',
  '/v1/admin/vouchers',
  admin,
  adminVoucher({
    name: 'OpFunded',
    type: 'PERCENT_OFF',
    value: 10,
    applicableOperatorIds: [OP_ID],
    fundingType: 'OPERATOR_FUNDED',
  }),
  true,
);
const code2 = r.json?.data?.code;
log(
  '3a. Admin create OPERATOR_FUNDED voucher (PENDING consent)',
  r.status === 201 && !!code2,
  `status=${r.status} code=${code2}`,
);
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('A02', code2), true);
log(
  '3b. OPERATOR_FUNDED without consent -> VOUCHER_NOT_APPLICABLE',
  r.status === 422 && r.json?.error?.code === 'VOUCHER_NOT_APPLICABLE',
  `status=${r.status} error=${r.json?.error?.code}`,
);

// 4. Usage-limit boundary
r = await call(
  'POST',
  '/v1/admin/vouchers',
  admin,
  adminVoucher({ name: 'Limit1', value: 30_000, totalUsageLimit: 1 }),
  true,
);
const code3 = r.json?.data?.code;
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('A03', code3), true);
log(
  '4a. Nth use succeeds',
  r.status === 201 && r.json?.data?.discountAmount === 30_000,
  `status=${r.status} discount=${r.json?.data?.discountAmount}`,
);
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('A04', code3), true);
log(
  '4b. N+1th use -> VOUCHER_USAGE_LIMIT_REACHED',
  r.status === 422 && r.json?.error?.code === 'VOUCHER_USAGE_LIMIT_REACHED',
  `status=${r.status} error=${r.json?.error?.code}`,
);

// 5. Operator self-create + branch-(a) apply without consent
r = await call(
  'POST',
  '/v1/operator/vouchers',
  operatorAdmin,
  {
    name: 'Operator Owned',
    type: 'FIXED_AMOUNT',
    value: 20_000,
    minOrderAmount: 0,
    maxDiscountAmount: null,
    totalUsageLimit: null,
    perUserLimit: null,
    validFrom: past,
    validUntil: future,
    applicableRouteIds: null,
  },
  true,
);
const code4 = r.json?.data?.code;
log(
  '5a. Operator self-create voucher',
  r.status === 201 &&
    r.json?.data?.ownerOperatorId === OP_ID &&
    r.json?.data?.fundingType === 'OPERATOR_FUNDED',
  `status=${r.status} owner=${r.json?.data?.ownerOperatorId} funding=${r.json?.data?.fundingType}`,
);
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('A05', code4), true);
log(
  '5b. Operator-owned voucher applies WITHOUT consent (branch a)',
  r.status === 201 && r.json?.data?.discountAmount === 20_000,
  `status=${r.status} discount=${r.json?.data?.discountAmount}`,
);

// 6. Role gates + oversight list
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('GET', '/v1/admin/vouchers', pax, null, false);
log(
  '6. PASSENGER GET /v1/admin/vouchers -> 403',
  r.status === 403,
  `status=${r.status} error=${r.json?.error?.code}`,
);
r = await call('GET', '/v1/admin/vouchers?fundingType=OPERATOR_FUNDED', admin, null, false);
const items = r.json?.data?.items ?? r.json?.data ?? [];
log(
  '7. Admin oversight GET list (SYSTEM_ADMIN)',
  r.status === 200 && Array.isArray(items) && items.length >= 1,
  `status=${r.status} count=${Array.isArray(items) ? items.length : 'n/a'}`,
);

// 8. Consent lifecycle: accept -> branch-(b) apply -> revoke -> precondition -> staff block
r = await call(
  'POST',
  '/v1/admin/vouchers',
  admin,
  adminVoucher({
    name: 'Consent',
    value: 25_000,
    applicableOperatorIds: [OP_ID],
    fundingType: 'OPERATOR_FUNDED',
  }),
  true,
);
const code5 = r.json?.data?.code;
r = await call('GET', '/v1/operator/voucher-consents?status=PENDING', operatorAdmin, null, false);
const consents = r.json?.data?.items ?? r.json?.data ?? [];
const consent = Array.isArray(consents)
  ? consents.find((x) => x.code === code5 || x.voucherCode === code5)
  : null;
const consentId =
  consent?.id ??
  (Array.isArray(consents) && consents.length ? consents[consents.length - 1].id : null);
log(
  '8a. Operator lists PENDING consents (tenant-scoped)',
  r.status === 200 && !!consentId,
  `status=${r.status} count=${Array.isArray(consents) ? consents.length : 'n/a'}`,
);
r = await call(
  'POST',
  `/v1/operator/voucher-consents/${consentId}/accept`,
  operatorAdmin,
  {},
  true,
);
log(
  '8b. Accept PENDING->ACCEPTED (emits booking.voucher.consent_accepted)',
  r.status === 200 && r.json?.data?.status === 'ACCEPTED',
  `status=${r.status} consent=${r.json?.data?.status}`,
);
pax = await token({ sub: guid(), role: 'PASSENGER' });
r = await call('POST', '/v1/bookings', pax, booking('B01', code5), true);
log(
  '8c. OPERATOR_FUNDED applies after consent ACCEPTED (branch b)',
  r.status === 201 && r.json?.data?.discountAmount === 25_000,
  `status=${r.status} discount=${r.json?.data?.discountAmount}`,
);
r = await call(
  'POST',
  `/v1/operator/voucher-consents/${consentId}/reject`,
  operatorAdmin,
  { reason: 'revoke' },
  true,
);
log(
  '8d. Revoke ACCEPTED->REJECTED (emits booking.voucher.consent_rejected)',
  r.status === 200 && r.json?.data?.status === 'REJECTED',
  `status=${r.status} consent=${r.json?.data?.status}`,
);
r = await call(
  'POST',
  `/v1/operator/voucher-consents/${consentId}/reject`,
  operatorAdmin,
  { reason: 'again' },
  true,
);
log(
  '8e. Re-reject -> 409 CONSENT_ALREADY_REJECTED',
  r.status === 409 && r.json?.error?.code === 'CONSENT_ALREADY_REJECTED',
  `status=${r.status} error=${r.json?.error?.code}`,
);
const staff = await token({ sub: guid(), role: 'OPERATOR_STAFF', operatorId: OP_ID });
r = await call('POST', `/v1/operator/voucher-consents/${consentId}/accept`, staff, {}, true);
log(
  '8f. OPERATOR_STAFF accept -> 403 (fine-grained role in .NET)',
  r.status === 403,
  `status=${r.status} error=${r.json?.error?.code}`,
);

if (process.env.DAY14_FORCE_NEWMAN_FAILURE === 'true')
  throw new Error('Forced Day-14 Newman failure requested');
const passed = results.filter(Boolean).length;
console.log(`\n=== Day-14 voucher E2E: ${passed}/${results.length} passed ===`);
if (passed !== results.length) throw new Error(`Day-14 voucher E2E failed: ${passed}/${results.length} passed`);
} catch (error) {
  runError = error;
} finally {
  try {
    cleanup();
    assertClean();
    console.log('PASS | D14 fixture cleanup | temporary vouchers and bookings removed');
  } catch (cleanupError) {
    if (!runError) runError = cleanupError;
    else console.error(`FAIL | D14 fixture cleanup | ${cleanupError.message}`);
  }
  try {
    if (previousBookingMode && previousBookingMode !== 'stub') composeBookingMode(previousBookingMode);
    if (previousBookingMode) console.log(`PASS | D14 mode restore | ${previousBookingMode}`);
  } catch (restoreError) {
    if (!runError) runError = restoreError;
    else console.error(`FAIL | D14 mode restore | ${restoreError.message}`);
  }
}
if (runError) throw runError;
