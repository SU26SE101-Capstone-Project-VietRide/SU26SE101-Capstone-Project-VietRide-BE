import net from 'node:net';

const DEFAULT_PORT_BASE = 34000;
const PORT_BLOCK_STEP = 20;
const MAX_DEFAULT_ATTEMPTS = 100;
const PORT_OFFSETS = Object.freeze({
  POSTGRES_PORT: 0,
  REDIS_PORT: 1,
  RABBITMQ_PORT: 2,
  RABBITMQ_MGMT_PORT: 3,
  IDENTITY_PORT: 4,
  TRIP_PORT: 5,
  BOOKING_PORT: 6,
  PAYMENT_PORT: 7,
  PARCEL_PORT: 8,
  NOTIFICATION_PORT: 9,
  GATEWAY_PORT: 10,
});

function parsePortBase(value) {
  const base = Number(value);
  const highestPort = base + Math.max(...Object.values(PORT_OFFSETS));
  if (!Number.isInteger(base) || base < 1024 || highestPort > 65535) {
    throw new Error(
      `PARCEL_SETTLEMENT_E2E_PORT_BASE must be an integer from 1024 to 65525; received '${value}'.`,
    );
  }
  return base;
}

export function buildParcelSettlementE2ePorts(base) {
  const validatedBase = parsePortBase(base);
  const env = Object.freeze(
    Object.fromEntries(
      Object.entries(PORT_OFFSETS).map(([name, offset]) => [name, String(validatedBase + offset)]),
    ),
  );
  return Object.freeze({
    base: validatedBase,
    env,
    urls: Object.freeze({
      identity: `http://localhost:${env.IDENTITY_PORT}`,
      trip: `http://localhost:${env.TRIP_PORT}`,
      payment: `http://localhost:${env.PAYMENT_PORT}`,
      parcel: `http://localhost:${env.PARCEL_PORT}`,
      gateway: `http://localhost:${env.GATEWAY_PORT}`,
    }),
  });
}

async function listen(port) {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.once('error', reject);
    server.listen({ host: '0.0.0.0', port, exclusive: true }, () => resolve(server));
  });
}

async function isPortBlockAvailable(portValues) {
  const servers = [];
  try {
    for (const port of portValues) {
      servers.push(await listen(port));
    }
    return true;
  } catch {
    return false;
  } finally {
    await Promise.all(
      servers.map(
        (server) =>
          new Promise((resolve) => {
            server.close(resolve);
          }),
      ),
    );
  }
}

export async function resolveParcelSettlementE2ePorts(
  env = process.env,
  checkPortBlock = isPortBlockAvailable,
) {
  const configuredBase = env.PARCEL_SETTLEMENT_E2E_PORT_BASE?.trim();
  const preferredBase = parsePortBase(configuredBase || DEFAULT_PORT_BASE);
  const attempts = configuredBase ? 1 : MAX_DEFAULT_ATTEMPTS;

  for (let attempt = 0; attempt < attempts; attempt += 1) {
    const candidateBase = preferredBase + attempt * PORT_BLOCK_STEP;
    if (candidateBase + Math.max(...Object.values(PORT_OFFSETS)) > 65535) break;
    const candidate = buildParcelSettlementE2ePorts(candidateBase);
    const ports = Object.values(candidate.env).map(Number);
    if (await checkPortBlock(ports)) return candidate;
  }

  const scope = configuredBase
    ? `configured block starting at ${preferredBase}`
    : `a free block starting at ${preferredBase}`;
  throw new Error(`Could not allocate ${scope}. Choose another PARCEL_SETTLEMENT_E2E_PORT_BASE.`);
}
