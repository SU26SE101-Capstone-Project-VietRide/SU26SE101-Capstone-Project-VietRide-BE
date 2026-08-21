import { execFileSync } from 'node:child_process';
import { createHmac, randomUUID } from 'node:crypto';
import fs from 'node:fs';

const gateway = process.env.PARCEL_RELIABILITY_E2E_GATEWAY ?? 'http://localhost:3000';
const postgresContainer = process.env.PARCEL_RELIABILITY_E2E_POSTGRES ?? 'vietride_postgres';
const env = loadEnv('.env');
const postgresUser = env.POSTGRES_USER ?? 'vietride';
const runTag = `${Date.now().toString(36)}${process.pid.toString(36)}`.toLowerCase();
const password = `Parcel!${runTag}Aa1`;
const phoneEntropy = randomUUID().replaceAll('-', '').slice(0, 13);
const shortTag = (BigInt(`0x${phoneEntropy}`) % 10_000_000n).toString().padStart(7, '0');
const evidenceBase = `https://e2e.vietride.local/${runTag}`;
const state = { ids: {}, tokens: {}, resources: {}, checks: [] };

function loadEnv(file) {
  const result = {};
  for (const rawLine of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith('#')) continue;
    const separator = line.indexOf('=');
    if (separator < 1) continue;
    const key = line.slice(0, separator).trim();
    let value = line.slice(separator + 1).trim();
    if ((value.startsWith('"') && value.endsWith('"')) || (value.startsWith("'") && value.endsWith("'"))) {
      value = value.slice(1, -1);
    }
    result[key] = value.replaceAll('\\n', '\n');
  }
  return result;
}

function assert(condition, message) {
  if (!condition) throw new Error(message);
}

function pass(label, detail = '') {
  state.checks.push({ label, detail });
  console.log(`PASS | ${label}${detail ? ` | ${detail}` : ''}`);
}

function sql(database, statement) {
  return execFileSync(
    'docker',
    [
      'exec',
      postgresContainer,
      'psql',
      '-v',
      'ON_ERROR_STOP=1',
      '-U',
      postgresUser,
      '-d',
      database,
      '-qAt',
      '-c',
      statement,
    ],
    { encoding: 'utf8', maxBuffer: 16 * 1024 * 1024 },
  ).trim();
}

const identitySql = (statement) => sql('vietride_identity', statement);
const tripSql = (statement) => sql('vietride_trip', statement);
const parcelSql = (statement) => sql('vietride_parcel', statement);
const paymentSql = (statement) => sql('vietride_payment', statement);

async function request(method, path, options = {}) {
  const headers = { accept: 'application/json', 'x-request-id': randomUUID(), ...options.headers };
  if (options.token) headers.authorization = `Bearer ${options.token}`;
  if (options.key) headers['idempotency-key'] = options.key;
  if (options.body !== undefined) headers['content-type'] = 'application/json';
  const response = await fetch(`${gateway}${path}`, {
    method,
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  });
  const text = await response.text();
  let json = null;
  if (text) {
    try {
      json = JSON.parse(text);
    } catch {
      json = { raw: text };
    }
  }
  return { status: response.status, headers: response.headers, json, text };
}

function apiData(result, expectedStatus, label) {
  assert(
    result.status === expectedStatus,
    `${label}: expected HTTP ${expectedStatus}, got ${result.status} ${result.json?.error?.code ?? ''} ${result.json?.error?.message ?? result.text}`,
  );
  assert(result.json?.success === true, `${label}: missing success ApiResponse envelope`);
  assert(result.json?.statusCode === expectedStatus, `${label}: ApiResponse statusCode mismatch`);
  assert(result.json?.meta?.traceId, `${label}: missing meta.traceId`);
  return result.json.data;
}

function expectError(result, expectedStatus, expectedCode, label) {
  assert(result.status === expectedStatus, `${label}: expected HTTP ${expectedStatus}, got ${result.status}: ${result.text}`);
  assert(result.json?.success === false, `${label}: missing error ApiResponse envelope`);
  assert(result.json?.error?.code === expectedCode, `${label}: expected ${expectedCode}, got ${result.json?.error?.code}: ${result.text}`);
  pass(label, `${expectedStatus} ${expectedCode}`);
  return result.json.error;
}

function hasErrorField(error, field) {
  return Array.isArray(error?.fields) && error.fields.some((item) => item.field === field);
}

async function poll(action, predicate, label, timeoutMs = 120_000) {
  const deadline = Date.now() + timeoutMs;
  let last;
  while (Date.now() < deadline) {
    try {
      last = await action();
      if (predicate(last)) return last;
    } catch (error) {
      last = error;
    }
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error(`${label} timed out; last=${last instanceof Error ? last.message : JSON.stringify(last)}`);
}

function phone(prefix) {
  return `+84${prefix}${shortTag}`.slice(0, 12);
}

function localDate(offsetDays = 0) {
  const formatter = new Intl.DateTimeFormat('en-CA', {
    timeZone: 'Asia/Ho_Chi_Minh',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  });
  const base = new Date(Date.now() + offsetDays * 86_400_000);
  return formatter.format(base);
}

function localTimePlus(minutes) {
  const parts = new Intl.DateTimeFormat('en-GB', {
    timeZone: 'Asia/Ho_Chi_Minh',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).formatToParts(new Date(Date.now() + minutes * 60_000));
  const map = Object.fromEntries(parts.map((part) => [part.type, part.value]));
  return `${map.hour}:${map.minute}:00`;
}

function isoDayOfWeek(dateText) {
  const day = new Date(`${dateText}T12:00:00+07:00`).getUTCDay();
  return day === 0 ? 7 : day;
}

function signVnPay(parameters) {
  const canonical = Object.entries(parameters)
    .filter(([key]) => key !== 'vnp_SecureHash' && key !== 'vnp_SecureHashType')
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`)
    .join('&');
  assert(env.VNPAY_HASH_SECRET, 'VNPAY_HASH_SECRET is required');
  return createHmac('sha512', env.VNPAY_HASH_SECRET).update(canonical).digest('hex');
}

function successfulIpn(paymentRedirectUrl, transactionNo) {
  const redirect = new URL(paymentRedirectUrl);
  const parameters = {
    vnp_Amount: redirect.searchParams.get('vnp_Amount'),
    vnp_BankCode: 'NCB',
    vnp_PayDate: new Date().toISOString().replace(/[-:TZ.]/g, '').slice(0, 14),
    vnp_ResponseCode: '00',
    vnp_TransactionNo: transactionNo,
    vnp_TransactionStatus: '00',
    vnp_TmnCode: redirect.searchParams.get('vnp_TmnCode'),
    vnp_TxnRef: redirect.searchParams.get('vnp_TxnRef'),
  };
  parameters.vnp_SecureHash = signVnPay(parameters);
  return parameters;
}

async function login(email) {
  const result = await request('POST', '/v1/auth/login', { body: { email, password } });
  const data = apiData(result, 200, `login ${email}`);
  assert(data.accessToken, `login ${email}: accessToken missing`);
  return data.accessToken;
}

function latestVerificationCode(userId, purpose) {
  return identitySql(
    `SELECT code FROM vietride_identity.email_verification_tokens WHERE user_id='${userId}' AND purpose='${purpose}' AND used_at IS NULL ORDER BY created_at DESC LIMIT 1;`,
  );
}

async function createPassenger(label, prefix) {
  const email = `${label}.${runTag}@e2e.vietride.local`;
  const created = apiData(
    await request('POST', '/v1/auth/register', {
      key: randomUUID(),
      body: { email, password, displayName: `E2E ${label}`, phone: phone(prefix) },
    }),
    201,
    `register ${label}`,
  );
  const code = latestVerificationCode(created.userId, 'REGISTRATION');
  assert(code, `registration OTP missing for ${label}`);
  apiData(
    await request('POST', '/v1/auth/verify-email', {
      key: randomUUID(),
      body: { email, code, purpose: 'REGISTRATION' },
    }),
    200,
    `verify ${label}`,
  );
  const token = await login(email);
  return { userId: created.userId, email, token };
}

async function setInitialPassword(userId) {
  const code = latestVerificationCode(userId, 'SET_INITIAL_PASSWORD');
  assert(code, `initial-password token missing for ${userId}`);
  apiData(
    await request('POST', '/v1/auth/set-initial-password', { key: randomUUID(), body: { token: code, password } }),
    200,
    `set initial password ${userId}`,
  );
}

async function createOperator(systemToken, suffix, prefix) {
  const email = `operator.${suffix}.${runTag}@e2e.vietride.local`;
  const result = apiData(
    await request('POST', '/v1/admin/operators', {
      token: systemToken,
      key: randomUUID(),
      body: {
        name: `Parcel Reliability ${suffix} ${runTag}`,
        contactEmail: email,
        contactPhone: phone(prefix),
        businessRegistrationNumber: `BR-${suffix}-${runTag}`,
        taxCode: `TAX-${suffix}-${runTag}`,
        addressStreet: '1 Nguyen Hue',
        addressWard: 'Phuong Sai Gon',
        addressProvince: 'Thanh pho Ho Chi Minh',
        representativeName: `Operator ${suffix} Admin`,
        representativePhone: phone(prefix + 1),
      },
    }),
    201,
    `create operator ${suffix}`,
  );
  await setInitialPassword(result.adminUser.userId);
  return {
    operatorId: result.operator.operatorId,
    adminUserId: result.adminUser.userId,
    email,
    token: await login(email),
  };
}

async function createOperatorUser(operator, role, label, prefix) {
  const email = `${label}.${runTag}@e2e.vietride.local`;
  const data = apiData(
    await request('POST', '/v1/operator/users', {
      token: operator.token,
      key: randomUUID(),
      body: { email, phone: phone(prefix), displayName: `E2E ${label}`, role },
    }),
    201,
    `create ${role}`,
  );
  await setInitialPassword(data.userId);
  return { userId: data.userId, email, token: await login(email) };
}

async function enableParcelSubscription(systemToken, operator) {
  const plan = apiData(
    await request('POST', '/v1/admin/subscription-plans', {
      token: systemToken,
      key: randomUUID(),
      body: {
        name: `Parcel E2E ${runTag}`,
        description: 'Live E2E plan for Parcel Reliability v2',
        pricePerMonth: 1000,
        pricePerYear: 12000,
        maxVehicles: 10,
        maxDrivers: 10,
        maxAssistants: 10,
        maxOperatorUsers: 10,
        maxRoutes: 10,
        maxTripsPerMonth: 100,
        enableParcel: true,
        enableShuttle: false,
        enableRag: false,
        isActive: true,
      },
    }),
    201,
    'create Parcel-enabled subscription plan',
  );
  const upgradeKey = randomUUID();
  const upgrade = apiData(
    await request('POST', '/v1/operator/subscription/upgrade', {
      token: operator.token,
      key: upgradeKey,
      body: { planId: plan.planId, billingPeriod: 'MONTHLY', paymentMethod: 'VNPAY' },
    }),
    202,
    'start operator subscription upgrade',
  );
  const replay = apiData(
    await request('POST', '/v1/operator/subscription/upgrade', {
      token: operator.token,
      key: upgradeKey,
      body: { planId: plan.planId, billingPeriod: 'MONTHLY', paymentMethod: 'VNPAY' },
    }),
    202,
    'replay subscription upgrade',
  );
  assert(replay.paymentId === upgrade.paymentId, 'subscription idempotency replay changed paymentId');
  const ipn = successfulIpn(upgrade.paymentRedirectUrl, `91${Date.now()}`);
  const ipnResponse = await request('POST', `/v1/payments/subscription-vnpay-ipn?${new URLSearchParams(ipn)}`);
  assert(ipnResponse.status === 200, `subscription IPN failed: ${ipnResponse.text}`);
  await poll(
    () => request('GET', '/v1/operator/subscription', { token: operator.token }),
    (result) => result.json?.data?.plan?.planId === plan.planId && result.json?.data?.plan?.modules?.enableParcel === true,
    'wait for Parcel subscription activation',
  );
  pass('operator subscription upgrade + VNPay IPN + replay', plan.planId);
  return plan;
}

async function topUpPassenger(passenger, amount) {
  const key = randomUUID();
  const topUp = apiData(
    await request('POST', '/v1/wallet/top-up', {
      token: passenger.token,
      key,
      body: { amount, method: 'VNPAY', paymentReturnMode: 'MOBILE_SDK' },
    }),
    201,
    'create passenger wallet top-up',
  );
  const replay = apiData(
    await request('POST', '/v1/wallet/top-up', {
      token: passenger.token,
      key,
      body: { amount, method: 'VNPAY', paymentReturnMode: 'MOBILE_SDK' },
    }),
    201,
    'replay passenger wallet top-up',
  );
  assert(replay.topUpRequestId === topUp.topUpRequestId, 'top-up idempotency replay changed resource');
  const ipn = successfulIpn(topUp.paymentRedirectUrl, `92${Date.now()}`);
  const response = await request('POST', `/v1/payments/vnpay-topup-ipn?${new URLSearchParams(ipn)}`);
  assert(response.status === 200 && response.json?.RspCode === '00', `top-up IPN failed: ${response.text}`);
  const wallet = await poll(
    () => request('GET', '/v1/wallet', { token: passenger.token }),
    (result) => result.json?.data?.balance >= amount,
    'wait for wallet top-up',
  );
  pass('passenger wallet top-up + VNPay IPN + replay', `${wallet.json.data.balance} VND`);
}

async function createTripResources(operator, driver, assistant) {
  const createStation = async (name, code, latitude, longitude) => {
    const createResult = await request('POST', '/v1/operator/stations', {
      token: operator.token,
      key: randomUUID(),
      body: {
        name: `${name} ${runTag}`,
        latitude,
        longitude,
        addressStreet: `${name} E2E street`,
        supportsShuttle: false,
        locationCode: code,
      },
    });
    if (createResult.status === 201) return apiData(createResult, 201, `create station ${name}`);
    const duplicate = apiData(createResult, 200, `detect nearby station ${name}`);
    assert(duplicate.warning?.code === 'STATION_DUPLICATE_NEARBY', `unexpected station warning for ${name}`);
    const stationId = duplicate.nearbyStations?.[0]?.id;
    assert(stationId, `nearby station id missing for ${name}`);
    const linkResult = await request('POST', '/v1/operator/stations', {
      token: operator.token,
      key: randomUUID(),
      body: { stationId },
    });
    assert([200, 201].includes(linkResult.status), `link nearby station ${name}: unexpected HTTP ${linkResult.status}`);
    return apiData(linkResult, linkResult.status, `link nearby station ${name}`);
  };
  const origin = await createStation('Origin', '27460', 10.75, 106.62);
  const destination = await createStation('Destination', '28789', 10.03, 105.78);
  const createStop = async (name, code, latitude, longitude) => apiData(
    await request('POST', '/v1/operator/stops', {
      token: operator.token,
      key: randomUUID(),
      body: {
        name: `${name} ${runTag}`,
        latitude,
        longitude,
        description: `${name} Parcel E2E`,
        address: `${name} address`,
        locationCode: code,
      },
    }),
    201,
    `create stop ${name}`,
  );
  const wrongStop = await createStop('Wrong Stop', '31186', 10.04, 105.75);
  const targetStop = await createStop('Target Stop', '29551', 10.12, 105.66);
  const route = apiData(
    await request('POST', '/v1/operator/routes/full', {
      token: operator.token,
      key: randomUUID(),
      body: {
        name: `Parcel Reliability Route ${runTag}`,
        originStationId: origin.stationId,
        destinationStationId: destination.stationId,
        returnRouteId: null,
        baseFare: 100000,
        isActive: true,
        pathPolyline: null,
        manualMetrics: { totalDistanceKm: 120, estimatedDurationMinutes: 180 },
        stops: [
          { stopId: wrongStop.id, orderIndex: 1, estimatedDurationFromOriginMinutes: 60, distanceFromOriginKm: 40, allowPickup: true, allowDropoff: true },
          { stopId: targetStop.id, orderIndex: 2, estimatedDurationFromOriginMinutes: 120, distanceFromOriginKm: 80, allowPickup: true, allowDropoff: true },
        ],
      },
    }),
    201,
    'create route with wrong/target stops',
  );
  const vehicleTypes = apiData(
    await request('GET', '/v1/vehicle-types', { token: operator.token }),
    200,
    'list vehicle types',
  );
  const vehicleType = Array.isArray(vehicleTypes) ? vehicleTypes[0] : vehicleTypes.items?.[0];
  assert(vehicleType?.vehicleTypeId ?? vehicleType?.id, 'no vehicle type available');
  const vehicleTypeId = vehicleType.vehicleTypeId ?? vehicleType.id;
  const vehicle = apiData(
    await request('POST', '/v1/operator/vehicles', {
      token: operator.token,
      key: randomUUID(),
      body: {
        vehicleTypeId,
        licensePlate: `51B-${runTag.slice(-5).toUpperCase()}`,
        seatLayoutJson: {
          version: 1,
          vehicleTypeCode: 'BUS',
          totalSeats: 4,
          rows: 2,
          cols: 2,
          decks: 1,
          aisles: [],
          seats: [
            { seatNumber: 'A01', row: 1, col: 1, deck: 1, type: 'STANDARD', isWindow: true, isAisle: false, disabled: false },
            { seatNumber: 'A02', row: 1, col: 2, deck: 1, type: 'STANDARD', isWindow: true, isAisle: false, disabled: false },
            { seatNumber: 'B01', row: 2, col: 1, deck: 1, type: 'STANDARD', isWindow: true, isAisle: false, disabled: false },
            { seatNumber: 'B02', row: 2, col: 2, deck: 1, type: 'STANDARD', isWindow: true, isAisle: false, disabled: false },
          ],
        },
        totalSeats: 4,
        maxCargoWeightKg: 500,
        maxCargoVolumeM3: 10,
        imageUrls: null,
      },
    }),
    201,
    'create cargo vehicle',
  );
  const today = localDate();
  const days = Array.from({ length: 7 }, (_, index) => index + 1);
  const schedule = apiData(
    await request('POST', '/v1/operator/driver-schedules', {
      token: operator.token,
      body: {
        routeId: route.id,
        vehicleId: vehicle.id,
        driverUserId: driver.userId,
        assistantUserId: assistant.userId,
        dayOfWeek: days,
        departureTime: localTimePlus(180),
        validFrom: today,
        validUntil: localDate(6),
        isActive: true,
        baseFare: 100000,
      },
    }),
    201,
    'create active driver schedule',
  );
  const tripsResult = await poll(
    () => request('GET', `/v1/operator/trips?from=${today}&to=${localDate(6)}&page=1&pageSize=20`, { token: operator.token }),
    (result) => (result.json?.data?.items?.filter((trip) => trip.route?.routeId === route.id).length ?? 0) >= 2,
    'wait for generated real trips',
  );
  const generated = tripsResult.json.data.items
    .filter((trip) => trip.route?.routeId === route.id)
    .sort((left, right) => Date.parse(left.departureAt) - Date.parse(right.departureAt));
  pass('Hangfire generated real trips from schedule', `${generated.length} trips`);
  return {
    originStationId: origin.stationId,
    destinationStationId: destination.stationId,
    wrongStopId: wrongStop.id,
    targetStopId: targetStop.id,
    routeId: route.id,
    vehicleId: vehicle.id,
    scheduleId: schedule.id,
    sourceTripId: generated[0].tripId,
    targetTripId: generated[1].tripId,
    sourceDate: generated[0].departureAt.slice(0, 10),
  };
}

async function setCompensationPolicy(operator) {
  const policy = apiData(
    await request('PUT', '/v1/operator/policies/parcel-compensation', {
      token: operator.token,
      key: randomUUID(),
      body: {
        compensationRatePercent: 50,
        maxCompensationVnd: 30000000,
        noProofFallbackMultiplier: 4,
        claimWindowDays: 30,
        searchSlaHours: 72,
        decisionSlaBusinessDays: 7,
        payoutSlaBusinessDays: 3,
        belowDefaultAcknowledged: false,
      },
    }),
    200,
    'set operator compensation policy',
  );
  assert(policy.compensationRatePercent === 50 && policy.maxCompensationVnd === 30000000, 'policy values drifted');
  assert(policy.platformDefaultPolicy && policy.effectiveForNewParcelsOnly === true, 'policy FE metadata missing');
  pass('operator compensation policy read model', `v${policy.policyVersion ?? policy.version}`);
  return policy;
}

async function createRouteFare(operator, trip) {
  apiData(
    await request('POST', '/v1/operator/parcel-route-fares', {
      token: operator.token,
      key: randomUUID(),
      body: {
        routeId: trip.routeId,
        sizeCategory: 'SMALL',
        // Keep enough money in this trip's own pre-settlement holding to pay the
        // 12m/50% claim, while the 80m/capped claim still becomes FUNDING_PENDING.
        priceVnd: 3000000,
        effectiveFrom: new Date(Date.now() - 60_000).toISOString(),
        effectiveUntil: null,
      },
    }),
    201,
    'create Parcel route fare',
  );
}

async function quoteParcel(sender, trip) {
  const params = new URLSearchParams({
    originStationId: trip.originStationId,
    destinationStationId: trip.destinationStationId,
    departureDate: trip.sourceDate,
    lengthCm: '20',
    widthCm: '20',
    heightCm: '20',
    estimatedWeightKg: '2',
    page: '1',
    pageSize: '20',
  });
  const result = apiData(
    await request('GET', `/v1/parcels/available-trips?${params}`, { token: sender.token }),
    200,
    'quote available Parcel trips',
  );
  const quote = result.items.find((item) => item.tripId === trip.sourceTripId);
  assert(quote?.quoteToken, `quote for source trip ${trip.sourceTripId} missing`);
  return quote;
}

async function createParcel(sender, recipient, trip, quote, label, declaredValueVnd) {
  const key = randomUUID();
  const body = {
    tripId: trip.sourceTripId,
    dropoffStopId: trip.targetStopId,
    bookingId: null,
    itemName: `Parcel ${label} ${runTag}`,
    description: `Parcel Reliability ${label} live E2E`,
    sizeCategory: quote.estimatedSizeCategory,
    lengthCm: 20,
    widthCm: 20,
    heightCm: 20,
    estimatedWeightKg: 2,
    photoUrl: null,
    recipient: { fullName: 'E2E Recipient', phoneNumber: phone(95), email: recipient.email },
    deliveryMethod: 'TERMINAL_PICKUP',
    paymentMethod: 'WALLET',
    voucherCode: null,
    quoteToken: quote.quoteToken,
    declaredValueVnd,
    quantity: 1,
  };
  const created = apiData(
    await request('POST', '/v1/parcels', { token: sender.token, key, body }),
    201,
    `create Parcel ${label}`,
  );
  const replay = apiData(
    await request('POST', '/v1/parcels', { token: sender.token, key, body }),
    201,
    `replay Parcel ${label}`,
  );
  assert(replay.parcelId === created.parcelId, `Parcel ${label} idempotency replay changed ID`);
  return { ...created, label, declaredValueVnd };
}

async function parcelDetail(parcelId, token) {
  return apiData(await request('GET', `/v1/parcels/${parcelId}`, { token }), 200, `get Parcel ${parcelId}`);
}

async function waitParcelStatus(parcelId, token, expected) {
  const result = await poll(
    () => request('GET', `/v1/parcels/${parcelId}`, { token }),
    (response) => response.json?.data?.status === expected,
    `wait Parcel ${parcelId} status ${expected}`,
  );
  return result.json.data;
}

async function payAndCheckIn(parcel, sender, assistant, trip, shouldLoad) {
  apiData(
    await request('POST', `/v1/parcels/${parcel.parcelId}/deposit-payment`, {
      token: sender.token,
      key: randomUUID(),
      body: { paymentMethod: 'WALLET' },
    }),
    200,
    `pay deposit ${parcel.label}`,
  );
  await waitParcelStatus(parcel.parcelId, sender.token, 'RESERVED');
  const checkedIn = apiData(
    await request('POST', `/v1/assistant/parcels/${parcel.parcelId}/check-in`, {
      token: assistant.token,
      key: randomUUID(),
      body: { tripId: trip.sourceTripId, parcelCode: parcel.parcelCode, photoUrls: [] },
    }),
    200,
    `check-in ${parcel.label}`,
  );
  assert(checkedIn.parcelState && checkedIn.currentCustody && Array.isArray(checkedIn.availableActions), `${parcel.label}: screen-ready mutation response missing`);
  const reweighed = apiData(
    await request('POST', `/v1/assistant/parcels/${parcel.parcelId}/reweigh`, {
      token: assistant.token,
      key: randomUUID(),
      body: { actualLengthCm: 20, actualWidthCm: 20, actualHeightCm: 20, actualWeightKg: 2 },
    }),
    200,
    `reweigh ${parcel.label}`,
  );
  if (reweighed.status === 'PENDING_FINAL_PAYMENT') {
    apiData(
      await request('POST', `/v1/parcels/${parcel.parcelId}/final-payment`, {
        token: sender.token,
        key: randomUUID(),
        body: { paymentMethod: 'WALLET' },
      }),
      200,
      `pay final ${parcel.label}`,
    );
    await waitParcelStatus(parcel.parcelId, sender.token, 'READY_TO_LOAD');
  } else {
    assert(reweighed.status === 'READY_TO_LOAD', `${parcel.label}: unexpected reweigh status ${reweighed.status}`);
  }
  if (shouldLoad) {
    const loaded = apiData(
      await request('POST', `/v1/assistant/parcels/${parcel.parcelId}/load`, {
        token: assistant.token,
        key: randomUUID(),
        body: { tripId: trip.sourceTripId, parcelCode: parcel.parcelCode },
      }),
      200,
      `load ${parcel.label}`,
    );
    assert(
      loaded.parcelState?.status === 'LOADED' && loaded.currentCustody,
      `${parcel.label}: load screen model missing: ${JSON.stringify(loaded)}`,
    );
  }
}

async function beginTrip(operator, driver, tripId) {
  apiData(
    await request('POST', `/v1/operator/trips/${tripId}/boarding`, { token: operator.token, key: randomUUID() }),
    200,
    `start boarding ${tripId}`,
  );
  apiData(
    await request('POST', `/v1/driver/trips/${tripId}/start`, { token: driver.token, key: randomUUID() }),
    200,
    `start trip ${tripId}`,
  );
}

async function arriveStop(driver, tripId, stopId) {
  return apiData(
    await request('POST', `/v1/driver/trips/${tripId}/stops/${stopId}/arrive`, { token: driver.token, key: randomUUID() }),
    200,
    `arrive stop ${stopId}`,
  );
}

async function departStop(driver, tripId, stopId) {
  return apiData(
    await request('POST', `/v1/driver/trips/${tripId}/stops/${stopId}/depart`, { token: driver.token, key: randomUUID() }),
    200,
    `depart stop ${stopId}`,
  );
}

async function deliverAndConfirm(parcel, assistant, recipient) {
  const delivered = apiData(
    await request('POST', `/v1/assistant/parcels/${parcel.parcelId}/deliver`, {
      token: assistant.token,
      key: randomUUID(),
      body: { photoUrls: [] },
    }),
    200,
    `deliver ${parcel.label}`,
  );
  assert(delivered.parcelState && delivered.availableActions, `${parcel.label}: deliver mutation screen model missing`);
  // Delivery tokens are deliberately stored only as SHA-256 hashes. Exercise the documented
  // crew verification fallback instead of weakening production token storage for this test.
  apiData(
    await request('POST', `/v1/assistant/parcels/${parcel.parcelId}/confirm-delivery`, {
      token: assistant.token,
      key: randomUUID(),
      body: { confirmNote: `Recipient ${recipient.userId} verified in person by E2E crew` },
    }),
    200,
    `crew verifies recipient for ${parcel.label}`,
  );
  await waitParcelStatus(parcel.parcelId, recipient.token, 'DELIVERY_CONFIRMED');
}

async function createLostClaim(parcel, sender, operator, provenLossVnd) {
  const incident = apiData(
    await request('POST', `/v1/parcels/${parcel.parcelId}/incidents`, {
      token: sender.token,
      key: randomUUID(),
      body: { incidentType: 'MISSING', description: `Lost claim ${parcel.label}`, evidenceUrls: [`${evidenceBase}/${parcel.label}.jpg`] },
    }),
    201,
    `report incident ${parcel.label}`,
  );
  parcelSql(`UPDATE vietride_parcel.parcel_incidents SET search_deadline=now()-interval '1 minute' WHERE id='${incident.incidentId}';`);
  const lost = apiData(
    await request('POST', `/v1/operator/parcel-incidents/${incident.incidentId}/declare-lost`, {
      token: operator.token,
      key: randomUUID(),
      body: { note: 'Search SLA exhausted by E2E test clock', resolutionCode: 'NOT_FOUND_AFTER_SEARCH' },
    }),
    200,
    `declare lost ${parcel.label}`,
  );
  assert(lost.incident?.status === 'LOST_CONFIRMED', `${parcel.label}: incident not LOST_CONFIRMED`);
  const outstandingTasks = Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_search_tasks WHERE incident_id='${incident.incidentId}' AND status IN ('OPEN','IN_PROGRESS');`));
  const failedTasks = Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_search_tasks WHERE incident_id='${incident.incidentId}' AND status='FAILED' AND completed_at IS NOT NULL;`));
  const lostLegs = Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcel.parcelId}' AND status='LOST' AND ended_at IS NOT NULL;`));
  assert(outstandingTasks === 0 && failedTasks > 0, `${parcel.label}: lost incident left non-terminal search tasks`);
  assert(lostLegs === 1, `${parcel.label}: active/planned transit leg was not marked LOST`);
  const claim = apiData(
    await request('POST', `/v1/parcels/${parcel.parcelId}/claims`, { token: sender.token, key: randomUUID() }),
    201,
    `submit claim ${parcel.label}`,
  );
  const withEvidence = apiData(
    await request('POST', `/v1/parcels/${parcel.parcelId}/claims/${claim.claimId}/evidence`, {
      token: sender.token,
      key: randomUUID(),
      body: { evidenceType: 'INVOICE', reference: `${evidenceBase}/${parcel.label}-invoice.pdf`, note: `Invoice ${provenLossVnd}` },
    }),
    201,
    `add claim evidence ${parcel.label}`,
  );
  assert(withEvidence.evidence?.length >= 1 && Array.isArray(withEvidence.availableActions), `${parcel.label}: evidence mutation did not return updated claim`);
  return { incident, claim: withEvidence, provenLossVnd };
}

async function main() {
  assert(env.SYSTEM_ADMIN_BOOTSTRAP_EMAIL && env.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD, 'System admin bootstrap credentials are missing');
  const systemLogin = await request('POST', '/v1/auth/login', {
    body: { email: env.SYSTEM_ADMIN_BOOTSTRAP_EMAIL, password: env.SYSTEM_ADMIN_BOOTSTRAP_PASSWORD },
  });
  const system = { token: apiData(systemLogin, 200, 'login system admin').accessToken };
  pass('system admin authenticated through Gateway');

  const sender = await createPassenger('sender', 90);
  const recipient = await createPassenger('recipient', 91);
  const operator = await createOperator(system.token, 'A', 92);
  const foreignOperator = await createOperator(system.token, 'B', 96);
  const driver = await createOperatorUser(operator, 'DRIVER', 'driver', 94);
  const assistant = await createOperatorUser(operator, 'ASSISTANT', 'assistant', 98);
  pass('real users/operator created and password login verified', `operator=${operator.operatorId}`);

  await enableParcelSubscription(system.token, operator);
  await topUpPassenger(sender, 50_000_000);
  const trip = await createTripResources(operator, driver, assistant);
  state.resources.trip = trip;
  await setCompensationPolicy(operator);
  await createRouteFare(operator, trip);
  const quote = await quoteParcel(sender, trip);

  const parcels = {};
  for (const [label, declaredValueVnd, shouldLoad] of [
    ['happy', 1_000_000, true],
    ['wrongStop', 2_000_000, true],
    ['missing', 3_000_000, true],
    ['recovered', 3_500_000, true],
    ['claim12m', 12_000_000, false],
    ['claim80m', 80_000_000, false],
    ['identityMismatch', 4_000_000, false],
  ]) {
    parcels[label] = await createParcel(sender, recipient, trip, quote, label, declaredValueVnd);
    await payAndCheckIn(parcels[label], sender, assistant, trip, shouldLoad);
  }
  pass('seven real Parcels created, wallet-paid and physically checked in');

  const senderList = apiData(await request('GET', '/v1/parcels/sent?page=1&pageSize=20', { token: sender.token }), 200, 'Passenger sent screen');
  const senderRow = senderList.items.find((item) => item.parcelId === parcels.happy.parcelId);
  assert(senderRow?.operator?.operatorId === operator.operatorId && senderRow.dropoffLocation && senderRow.reliability, 'Passenger sent row is not screen-ready');
  const receivedList = apiData(await request('GET', '/v1/parcels/received?page=1&pageSize=20', { token: recipient.token }), 200, 'Passenger received screen');
  assert(receivedList.items.some((item) => item.parcelId === parcels.happy.parcelId), 'recipient logical link missing from received list');
  const detail = await parcelDetail(parcels.happy.parcelId, sender.token);
  assert(detail.operator && detail.trip && detail.dropoffLocation && detail.compensationPolicySnapshot && detail.reliabilitySummary && Array.isArray(detail.availableActions), 'Passenger Parcel detail is not screen-ready');
  pass('Passenger sent/received/detail require one request per screen');

  const manifest = apiData(
    await request('GET', `/v1/assistant/trips/${trip.sourceTripId}/parcels?page=1&pageSize=50`, { token: assistant.token }),
    200,
    'Driver manifest screen',
  );
  assert(
    manifest.tripContext?.orderedStops?.length === 2 && manifest.summary && manifest.pagination && manifest.items.length >= 4,
    `Driver manifest screen model incomplete: ${JSON.stringify(manifest)}`,
  );
  assert(manifest.items.every((item) => item.dropoffLocation && item.currentCustody && item.identityCheckHints && Array.isArray(item.availableActions)), 'Driver manifest item enrichment incomplete');
  pass('Driver manifest screen-ready in one request', `${manifest.items.length} items`);

  const noQr = await request('POST', `/v1/assistant/parcels/${parcels.happy.parcelId}/unload`, {
    token: assistant.token,
    key: randomUUID(),
    body: { actualLocation: { kind: 'ROUTE_STOP', id: trip.wrongStopId }, photoUrls: [] },
  });
  expectError(noQr, 422, 'VALIDATION_ERROR', 'unload without QR rejected');

  await beginTrip(operator, driver, trip.sourceTripId);
  await waitParcelStatus(parcels.happy.parcelId, sender.token, 'IN_TRANSIT');
  pass('Trip started event moved loaded Parcels to IN_TRANSIT');
  await arriveStop(driver, trip.sourceTripId, trip.wrongStopId);
  const wrongQr = await request('POST', `/v1/assistant/parcels/${parcels.happy.parcelId}/unload`, {
    token: assistant.token,
    key: randomUUID(),
    body: { parcelCode: parcels.missing.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.wrongStopId }, photoUrls: [] },
  });
  const wrongQrError = expectError(wrongQr, 409, 'SCAN_IDENTITY_MISMATCH', 'QR of another Parcel rejected');
  assert(hasErrorField(wrongQrError, 'requiredAction'), 'QR mismatch did not include structured fields');
  const wrongLocation = await request('POST', `/v1/assistant/parcels/${parcels.happy.parcelId}/unload`, {
    token: assistant.token,
    key: randomUUID(),
    body: { parcelCode: parcels.happy.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.wrongStopId }, photoUrls: [] },
  });
  const wrongLocationError = expectError(wrongLocation, 409, 'PARCEL_CUSTODY_LOCATION_MISMATCH', 'correct QR at wrong stop rejected');
  assert(
    ['expectedStop', 'actualStop', 'requiredAction'].every((field) => hasErrorField(wrongLocationError, field)),
    `location mismatch structured fields missing: ${JSON.stringify(wrongLocationError.fields)}`,
  );

  const identityMismatchAction = apiData(
    await request('POST', `/v1/assistant/parcels/${parcels.identityMismatch.parcelId}/custody-exception`, {
      token: assistant.token,
      key: randomUUID(),
      body: {
        incidentType: 'PACKAGE_IDENTITY_MISMATCH',
        actualLocationType: 'VEHICLE',
        actualLocationId: trip.vehicleId,
        locationSnapshot: `VEHICLE:${trip.vehicleId}`,
        temporaryExceptionTag: `TMP-ID-${runTag}`,
        description: 'Physical package does not match label photo and weight',
        observedWeightKg: 7,
        evidenceUrls: [`${evidenceBase}/identity-mismatch.jpg`],
        reason: 'QR label belongs to another physical package',
        supervisorApprovalUserId: operator.adminUserId,
      },
    }),
    200,
    'report package identity mismatch',
  );
  assert(
    identityMismatchAction.activeIncident?.type === 'PACKAGE_IDENTITY_MISMATCH',
    `identity mismatch incident type drifted: ${JSON.stringify(identityMismatchAction)}`,
  );

  const wrongStopAction = apiData(
    await request('POST', `/v1/assistant/parcels/${parcels.wrongStop.parcelId}/custody-exception`, {
      token: assistant.token,
      key: randomUUID(),
      body: {
        incidentType: 'WRONG_STOP',
        actualLocationType: 'ROUTE_STOP',
        actualLocationId: trip.wrongStopId,
        locationSnapshot: `WRONG_STOP:${trip.wrongStopId}`,
        temporaryExceptionTag: null,
        description: 'Parcel was physically placed at the wrong stop',
        observedWeightKg: 2,
        evidenceUrls: [`${evidenceBase}/wrong-stop.jpg`],
        reason: 'Crew unloaded outside the normal QR flow',
        supervisorApprovalUserId: operator.adminUserId,
      },
    }),
    200,
    'record physical wrong-stop exception',
  );
  const wrongIncident = wrongStopAction.activeIncident;
  assert(wrongIncident?.status === 'SEARCHING', `wrong-stop incident did not enter SEARCHING: ${JSON.stringify(wrongStopAction)}`);

  const unidentified = apiData(
    await request('POST', '/v1/stations/parcels/unidentified', {
      token: operator.token,
      key: randomUUID(),
      body: {
        temporaryExceptionTag: `TMP-${runTag}`,
        tripId: trip.sourceTripId,
        locationType: 'ROUTE_STOP',
        locationId: trip.wrongStopId,
        locationSnapshot: `WRONG_STOP:${trip.wrongStopId}`,
        description: `Parcel identityMismatch ${runTag}`,
        observedWeightKg: 7,
        evidenceReferences: [`${evidenceBase}/unidentified.jpg`],
      },
    }),
    201,
    'register unidentified package',
  );
  const unidentifiedList = apiData(await request('GET', '/v1/operator/unidentified-packages?page=1&pageSize=20', { token: operator.token }), 200, 'list unidentified packages');
  assert(unidentifiedList.items.some((item) => item.packageId === unidentified.packageId), 'unidentified package missing from operator queue');
  const candidates = apiData(await request('GET', `/v1/operator/unidentified-packages/${unidentified.packageId}/match-candidates`, { token: operator.token }), 200, 'unidentified match candidates');
  assert(Array.isArray(candidates.items ?? candidates), 'unidentified candidates response invalid');
  const matched = apiData(
    await request('POST', `/v1/stations/parcels/unidentified/${unidentified.packageId}/match`, {
      token: operator.token,
      key: randomUUID(),
      body: { parcelId: parcels.identityMismatch.parcelId },
    }),
    200,
    'supervisor matches unidentified package',
  );
  assert(matched.matchedParcelId === parcels.identityMismatch.parcelId || matched.parcelId === parcels.identityMismatch.parcelId, 'unidentified package match failed');
  pass('unidentified package queue/candidates/manual match');

  apiData(
    await request('POST', `/v1/operator/parcel-incidents/${wrongIncident.incidentId}/mark-found`, {
      token: operator.token,
      key: randomUUID(),
      body: {
        actualLocationType: 'ROUTE_STOP',
        actualLocationId: trip.wrongStopId,
        locationSnapshot: `WRONG_STOP:${trip.wrongStopId}`,
        evidenceReferences: [`${evidenceBase}/found.jpg`],
        note: 'Found in wrong station cage',
      },
    }),
    200,
    'mark wrong-stop Parcel found',
  );
  const options = apiData(
    await request('GET', `/v1/operator/parcel-incidents/${wrongIncident.incidentId}/forwarding-options`, { token: operator.token }),
    200,
    'get forwarding options',
  );
  const optionItems = options.items ?? options;
  const targetOption = optionItems.find((item) => item.trip?.tripId === trip.targetTripId || item.tripId === trip.targetTripId);
  assert(targetOption?.canReserve, `target forwarding trip missing/unreservable: ${JSON.stringify(optionItems)}`);
  const forwarded = apiData(
    await request('POST', `/v1/operator/parcel-incidents/${wrongIncident.incidentId}/forward`, {
      token: operator.token,
      key: randomUUID(),
      body: { targetTripId: trip.targetTripId },
    }),
    200,
    'plan forwarding leg',
  );
  assert(
    forwarded.incident?.status === 'FORWARDING'
      && forwarded.forwardingSummary
      && forwarded.forwardingOperation?.targetTrip?.tripId === trip.targetTripId
      && forwarded.forwardingOperation?.cargoTransferStatus === 'AWAITING_CREW_CONFIRMATION',
    `forward response lacks forwarding operation detail: ${JSON.stringify(forwarded)}`,
  );
  const confirmedTransfer = apiData(
    await request('POST', `/v1/crew/parcels/${parcels.wrongStop.parcelId}/confirm-transfer`, {
      token: assistant.token,
      key: randomUUID(),
      body: { parcelCode: parcels.wrongStop.parcelCode },
    }),
    200,
    'crew confirms forwarding transfer',
  );
  assert(confirmedTransfer.tripId === trip.targetTripId, 'forwarding transfer did not activate target trip');
  assert(Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.wrongStop.parcelId}';`)) === 2, 'forwarding did not preserve old leg and create a new leg');
  pass('wrong-stop found → forwarding option → new leg → crew handoff');

  await departStop(driver, trip.sourceTripId, trip.wrongStopId);
  const staleUnload = await request('POST', `/v1/assistant/parcels/${parcels.happy.parcelId}/unload`, {
    token: assistant.token,
    key: randomUUID(),
    body: { parcelCode: parcels.happy.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.wrongStopId }, photoUrls: [] },
  });
  expectError(staleUnload, 409, 'PARCEL_CUSTODY_LOCATION_MISMATCH', 'unload at already-departed stop rejected');
  await arriveStop(driver, trip.sourceTripId, trip.targetStopId);
  const unloadedHappy = apiData(
    await request('POST', `/v1/assistant/parcels/${parcels.happy.parcelId}/unload`, {
      token: assistant.token,
      key: randomUUID(),
      body: { parcelCode: parcels.happy.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.targetStopId }, photoUrls: [] },
    }),
    200,
    'unload happy Parcel at target stop',
  );
  assert(unloadedHappy.parcelState?.status === 'UNLOADED' && unloadedHappy.createdCustodyEvent?.eventType === 'UNLOADED', 'unload mutation response incomplete');
  await deliverAndConfirm(parcels.happy, assistant, recipient);

  const reconciliation = apiData(
    await request('POST', `/v1/assistant/trips/${trip.sourceTripId}/stops/${trip.targetStopId}/reconcile`, {
      token: assistant.token,
      key: randomUUID(),
      body: {
        scannedParcelIds: [parcels.happy.parcelId],
        manualExceptionParcelIds: [],
        departureOverrideReason: 'Supervisor authorizes departure after vehicle/station search starts',
        supervisorApprovalUserId: operator.adminUserId,
      },
    }),
    200,
    'reconcile target stop',
  );
  const unresolvedMissing = reconciliation.unresolvedParcels.find((item) => item.parcelId === parcels.missing.parcelId);
  const unresolvedRecovered = reconciliation.unresolvedParcels.find((item) => item.parcelId === parcels.recovered.parcelId);
  assert(reconciliation.canDepart === true && reconciliation.requiresSupervisorApproval === false, 'supervisor reconciliation override not applied');
  assert(unresolvedMissing?.parcelCode && unresolvedMissing.expectedDropoff && unresolvedMissing.lastCustody && unresolvedMissing.incidentId && unresolvedMissing.recommendedAction, 'unresolved screen model incomplete');
  assert(unresolvedRecovered?.incidentId, 'recoverable Parcel was not included in reconciliation incident rows');
  pass('stop reconciliation returns actionable unresolved Parcel rows');

  const recoveredBeforeFound = apiData(
    await request('GET', `/v1/operator/parcel-incidents/${unresolvedRecovered.incidentId}`, { token: operator.token }),
    200,
    'read recoverable missing incident',
  );
  assert(recoveredBeforeFound.searchTasks.some((task) => ['OPEN', 'IN_PROGRESS'].includes(task.status)), 'recoverable incident has no active search task');
  apiData(
    await request('POST', `/v1/operator/parcel-incidents/${unresolvedRecovered.incidentId}/mark-found`, {
      token: operator.token,
      key: randomUUID(),
      body: {
        actualLocationType: 'VEHICLE',
        actualLocationId: trip.vehicleId,
        locationSnapshot: `VEHICLE:${trip.vehicleId}`,
        evidenceReferences: [`${evidenceBase}/recovered-on-vehicle.jpg`],
        note: 'Found during the same-vehicle cargo sweep',
      },
    }),
    200,
    'mark missing Parcel found on same vehicle',
  );
  const recoveredAfterFound = apiData(
    await request('GET', `/v1/operator/parcel-incidents/${unresolvedRecovered.incidentId}`, { token: operator.token }),
    200,
    'read found incident with terminal search tasks',
  );
  assert(recoveredAfterFound.incident.status === 'FOUND', 'recoverable incident did not enter FOUND');
  assert(
    recoveredAfterFound.searchTasks.every((task) => !['OPEN', 'IN_PROGRESS'].includes(task.status))
      && recoveredAfterFound.searchTasks.some((task) => task.status === 'CANCELLED'),
    `found incident left active search tasks: ${JSON.stringify(recoveredAfterFound.searchTasks)}`,
  );
  apiData(
    await request('POST', `/v1/operator/parcel-incidents/${unresolvedRecovered.incidentId}/resolve`, {
      token: operator.token,
      key: randomUUID(),
      body: { resolutionCode: 'FOUND_ON_SAME_VEHICLE', note: 'Cargo remains on the source Trip for normal delivery' },
    }),
    200,
    'resolve same-vehicle recovery incident',
  );
  const recoveredLegBeforeUnload = parcelSql(`SELECT status FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.recovered.parcelId}' ORDER BY sequence DESC LIMIT 1;`);
  assert(recoveredLegBeforeUnload === 'ACTIVE', `same-vehicle recovery should keep original leg ACTIVE, got ${recoveredLegBeforeUnload}`);
  apiData(
    await request('POST', `/v1/assistant/parcels/${parcels.recovered.parcelId}/unload`, {
      token: assistant.token,
      key: randomUUID(),
      body: { parcelCode: parcels.recovered.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.targetStopId }, photoUrls: [] },
    }),
    200,
    'unload recovered Parcel at correct stop',
  );
  await deliverAndConfirm(parcels.recovered, assistant, recipient);
  const recoveredLegAfterDelivery = parcelSql(`SELECT status || ':' || (ended_at IS NOT NULL)::text FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.recovered.parcelId}' ORDER BY sequence DESC LIMIT 1;`);
  assert(recoveredLegAfterDelivery === 'COMPLETED:true', `recovered Parcel leg not completed after delivery: ${recoveredLegAfterDelivery}`);
  assert(Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.recovered.parcelId}';`)) === 1, 'same-vehicle recovery created an unnecessary forwarding leg');
  pass('missing → searching → found on same vehicle → normal delivery without forwarding');

  await departStop(driver, trip.sourceTripId, trip.targetStopId);
  apiData(await request('POST', `/v1/driver/trips/${trip.sourceTripId}/destination/arrive`, { token: driver.token, key: randomUUID() }), 200, 'arrive source destination');
  apiData(await request('POST', `/v1/driver/trips/${trip.sourceTripId}/complete`, { token: driver.token, key: randomUUID() }), 200, 'complete source trip');

  await poll(
    () => request('GET', `/v1/operator/parcel-incidents/${unresolvedMissing.incidentId}`, { token: operator.token }),
    (result) => result.status === 200,
    'wait missing incident detail',
  );
  const incidentQueue = apiData(await request('GET', `/v1/operator/parcel-incidents?search=${parcels.missing.parcelCode}&page=1&pageSize=20`, { token: operator.token }), 200, 'operator incident queue');
  const incidentRow = incidentQueue.items.find((item) => item.incidentId === unresolvedMissing.incidentId);
  assert(incidentQueue.totalItems >= 1 && incidentRow?.parcel && incidentRow.trip && incidentRow.expectedDropoff && incidentRow.lastCustody && incidentRow.taskSummary && incidentRow.sla && Array.isArray(incidentRow.availableActions), 'incident queue row not screen-ready');
  const incidentDetail = apiData(await request('GET', `/v1/operator/parcel-incidents/${unresolvedMissing.incidentId}`, { token: operator.token }), 200, 'operator incident detail');
  assert(incidentDetail.parcel && incidentDetail.sender && incidentDetail.recipient && incidentDetail.trip && incidentDetail.expectedDropoff && Array.isArray(incidentDetail.searchTasks) && Array.isArray(incidentDetail.availableActions), 'incident detail not screen-ready');
  const foreignIncident = await request('GET', `/v1/operator/parcel-incidents/${unresolvedMissing.incidentId}`, { token: foreignOperator.token });
  expectError(foreignIncident, 403, 'FORBIDDEN', 'cross-tenant incident read blocked');

  const senderTrace = apiData(await request('GET', `/v1/parcels/${parcels.missing.parcelId}/trace`, { token: sender.token }), 200, 'sender Parcel trace');
  assert(senderTrace.parcelSummary && senderTrace.operator && senderTrace.trip && senderTrace.dropoffLocation && senderTrace.currentCustody && senderTrace.activeIncident && senderTrace.timeline?.items && Array.isArray(senderTrace.availableActions), 'sender trace is not screen-ready');
  const recipientTrace = apiData(await request('GET', `/v1/parcels/${parcels.missing.parcelId}/trace`, { token: recipient.token }), 200, 'recipient Parcel trace');
  assert(recipientTrace.claimSummary == null, 'recipient trace leaked sender claim');
  pass('Passenger tracking is complete in one request and claim privacy is enforced');

  const firstClaim = await createLostClaim(parcels.claim12m, sender, operator, 12_000_000);
  const secondClaim = await createLostClaim(parcels.claim80m, sender, operator, 80_000_000);
  apiData(
    await request('POST', `/v1/admin/operators/${operator.operatorId}/wallet/adjust`, {
      token: system.token,
      key: randomUUID(),
      body: { type: 'CREDIT', amount: 10_000_000, note: `Parcel Reliability payout funding ${runTag}` },
    }),
    200,
    'fund operator wallet for compensation',
  );
  const decide = async (entry) => apiData(
    await request('POST', `/v1/operator/claims/${entry.claim.claimId}/decision`, {
      token: operator.token,
      key: randomUUID(),
      body: { decision: 'APPROVE', provenDirectLossVnd: entry.provenLossVnd, reason: 'Valid invoice and operator process breach confirmed' },
    }),
    200,
    `approve claim ${entry.claim.claimId}`,
  );
  const approved12 = await decide(firstClaim);
  assert(approved12.claim?.cargoAwardVnd === 6_000_000, `12m claim cargo award expected 6m, got ${approved12.claim?.cargoAwardVnd}`);
  const paid12 = await poll(
    () => request('GET', `/v1/operator/claims/${firstClaim.claim.claimId}`, { token: operator.token }),
    (result) => result.json?.data?.claim?.status === 'PAID' || result.json?.data?.status === 'PAID',
    'wait first compensation payout',
  );
  pass('12m × 50% compensation paid', `${approved12.claim.totalAwardVnd} VND`);

  const settlementPage = await poll(
    () => request('GET', `/v1/admin/trip-settlements?tripId=${trip.sourceTripId}&page=1&pageSize=20`, { token: system.token }),
    (result) => result.status === 200 && result.json?.data?.items?.some((item) => item.tripId === trip.sourceTripId),
    'wait source trip settlement projection',
  );
  const sourceSettlement = settlementPage.json.data.items.find((item) => item.tripId === trip.sourceTripId);
  const settled = apiData(
    await request('POST', `/v1/admin/trip-settlements/${sourceSettlement.settlementId}/settle`, {
      token: system.token,
      key: randomUUID(),
    }),
    200,
    'manually settle source trip before second claim',
  );
  assert(settled.status === 'SETTLED', `source trip settlement expected SETTLED, got ${settled.status}`);
  const operatorWallet = apiData(
    await request('GET', '/v1/operator/wallet', { token: operator.token }),
    200,
    'read settled operator wallet',
  );
  const retainedOperatorFunds = 1_000_000;
  if (operatorWallet.balance > retainedOperatorFunds) {
    apiData(
      await request('POST', `/v1/admin/operators/${operator.operatorId}/wallet/adjust`, {
        token: system.token,
        key: randomUUID(),
        body: {
          type: 'DEBIT',
          amount: operatorWallet.balance - retainedOperatorFunds,
          note: `Leave insufficient post-settlement funding for Parcel claim ${runTag}`,
        },
      }),
      200,
      'reduce operator wallet below second claim award',
    );
  }
  pass('source trip settled and OperatorWallet prepared for insufficient-funding branch');

  const approved80 = await decide(secondClaim);
  assert(approved80.claim?.cargoAwardVnd === 30_000_000, `80m claim cargo award expected cap 30m, got ${approved80.claim?.cargoAwardVnd}`);
  const pending80 = await poll(
    () => request('GET', `/v1/operator/claims/${secondClaim.claim.claimId}`, { token: operator.token }),
    (result) => ['FUNDING_PENDING', 'PAID'].includes(result.json?.data?.claim?.status ?? result.json?.data?.status),
    'wait second compensation payout status',
  );
  const pending80Status = pending80.json?.data?.claim?.status ?? pending80.json?.data?.status;
  assert(pending80Status === 'FUNDING_PENDING', `80m capped claim expected FUNDING_PENDING, got ${pending80Status}`);
  const payoutCount = Number(paymentSql(`SELECT count(*) FROM vietride_payment.parcel_compensation_payouts WHERE claim_id='${firstClaim.claim.claimId}';`));
  assert(payoutCount === 1, `first claim payout duplicate count=${payoutCount}`);
  const decisionReplay = await request('POST', `/v1/operator/claims/${firstClaim.claim.claimId}/decision`, {
    token: operator.token,
    key: randomUUID(),
    body: { decision: 'APPROVE', provenDirectLossVnd: 12_000_000, reason: 'Duplicate decision must be rejected' },
  });
  expectError(decisionReplay, 409, 'PARCEL_CLAIM_ALREADY_DECIDED', 'duplicate claim decision rejected');
  pass('80m compensation capped at 30m and enters FUNDING_PENDING');

  const claimQueue = apiData(await request('GET', '/v1/operator/claims?page=1&pageSize=20', { token: operator.token }), 200, 'operator claim queue');
  const claimRow = claimQueue.items.find((item) => item.claimId === secondClaim.claim.claimId);
  assert(claimQueue.totalItems >= 2 && claimRow?.parcel && claimRow.sender && claimRow.incident && claimRow.policySnapshot && claimRow.fundingStatus && Array.isArray(claimRow.availableActions), 'claim queue not screen-ready');
  const foreignClaim = await request('GET', `/v1/operator/claims/${secondClaim.claim.claimId}`, { token: foreignOperator.token });
  expectError(foreignClaim, 403, 'FORBIDDEN', 'cross-tenant claim read blocked');

  tripSql(`
    BEGIN;
    UPDATE vietride_trip.trip_stops
    SET estimated_arrival_time = now() + interval '2 hours'
        + (estimated_arrival_time - (
            SELECT departure_date_time
            FROM vietride_trip.trips
            WHERE id='${trip.targetTripId}'
          ))
    WHERE trip_id='${trip.targetTripId}';
    UPDATE vietride_trip.resource_reservations
    SET planned_end_at = now() + interval '2 hours' + (planned_end_at - planned_start_at),
        planned_start_at = now() + interval '2 hours'
    WHERE trip_id='${trip.targetTripId}' AND status='RESERVED';
    UPDATE vietride_trip.trips
    SET estimated_arrival_time = now() + interval '2 hours'
        + (estimated_arrival_time - departure_date_time),
        departure_date_time = now() + interval '2 hours',
        updated_at = now()
    WHERE id='${trip.targetTripId}' AND status='SCHEDULED';
    COMMIT;
  `);
  pass('forwarding target trip clock moved into the real boarding window');

  await beginTrip(operator, driver, trip.targetTripId);
  await waitParcelStatus(parcels.wrongStop.parcelId, recipient.token, 'IN_TRANSIT');
  await arriveStop(driver, trip.targetTripId, trip.wrongStopId);
  await departStop(driver, trip.targetTripId, trip.wrongStopId);
  await arriveStop(driver, trip.targetTripId, trip.targetStopId);
  apiData(
    await request('POST', `/v1/assistant/parcels/${parcels.wrongStop.parcelId}/unload`, {
      token: assistant.token,
      key: randomUUID(),
      body: { parcelCode: parcels.wrongStop.parcelCode, actualLocation: { kind: 'ROUTE_STOP', id: trip.targetStopId }, photoUrls: [] },
    }),
    200,
    'unload forwarded Parcel at correct stop',
  );
  await deliverAndConfirm(parcels.wrongStop, assistant, recipient);
  apiData(
    await request('POST', `/v1/operator/parcel-incidents/${wrongIncident.incidentId}/resolve`, {
      token: operator.token,
      key: randomUUID(),
      body: { resolutionCode: 'FORWARDED_AND_DELIVERED', note: 'Forwarded Parcel delivered to correct stop' },
    }),
    200,
    'resolve forwarded wrong-stop incident',
  );
  pass('forwarded Parcel reached correct stop and recipient confirmed delivery');

  const happyLegLifecycle = parcelSql(`SELECT status || ':' || (started_at IS NOT NULL)::text || ':' || (ended_at IS NOT NULL)::text FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.happy.parcelId}' ORDER BY sequence DESC LIMIT 1;`);
  assert(happyLegLifecycle === 'COMPLETED:true:true', `happy Parcel leg lifecycle inconsistent: ${happyLegLifecycle}`);
  const forwardingLegLifecycle = parcelSql(`SELECT string_agg(status || ':' || (ended_at IS NOT NULL)::text, ',' ORDER BY sequence) FROM vietride_parcel.parcel_transit_legs WHERE parcel_id='${parcels.wrongStop.parcelId}';`);
  assert(forwardingLegLifecycle === 'FORWARDED:true,COMPLETED:true', `forwarding leg lifecycle inconsistent: ${forwardingLegLifecycle}`);
  const currentRunParcelIds = Object.values(parcels)
    .map((parcel) => `'${parcel.parcelId}'`)
    .join(',');
  const terminalIncidentTasks = Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_search_tasks task JOIN vietride_parcel.parcel_incidents incident ON incident.id=task.incident_id WHERE incident.parcel_id IN (${currentRunParcelIds}) AND incident.status IN ('RESOLVED','CLOSED','LOST_CONFIRMED') AND task.status IN ('OPEN','IN_PROGRESS');`));
  assert(terminalIncidentTasks === 0, `terminal incidents left ${terminalIncidentTasks} outstanding search tasks`);
  pass('transit-leg lifecycle and terminal search-task invariants are consistent');

  const custodyEvents = Number(parcelSql(`SELECT count(*) FROM vietride_parcel.parcel_custody_events WHERE parcel_id='${parcels.wrongStop.parcelId}';`));
  const duplicateKeys = Number(parcelSql(`SELECT count(*) FROM (SELECT idempotency_key FROM vietride_parcel.parcel_custody_events WHERE parcel_id='${parcels.wrongStop.parcelId}' AND idempotency_key IS NOT NULL GROUP BY idempotency_key HAVING count(*)>1) duplicates;`));
  assert(custodyEvents >= 6 && duplicateKeys === 0, 'custody append-only/idempotency invariant failed');
  const negativeWallets = Number(paymentSql('SELECT count(*) FROM vietride_payment.wallets WHERE balance < 0;'));
  assert(negativeWallets === 0, 'negative passenger wallet detected');
  pass('DB invariants: append-only custody, unique event keys, non-negative wallets');

  console.log(`PARCEL_RELIABILITY_E2E_RUN_TAG=${runTag}`);
  console.log(`PARCEL_RELIABILITY_E2E_CHECKS=${state.checks.length}`);
  console.log('PARCEL_RELIABILITY_E2E=PASS');
}

await main();
