const { PrismaClient } = require('../../apps/tracking/src/generated/tracking-prisma-client');
const Redis = require('ioredis');

const tripId = process.env.TRIP_ID || '11111111-1111-4111-8111-111111111111';
const stopId = process.env.STOP_ID || '22222222-2222-4222-8222-222222222222';
const startTime = process.env.START_TIME || '2026-06-03T09:00:00.000Z';
const endTime = process.env.END_TIME || '2026-06-03T11:00:00.000Z';

const redisUrl = process.env.REDIS_URL || 'redis://localhost:6379';

function trackingLatestKey(id) {
  return `tracking:latest:${id}`;
}

function trackingEtaKey(id, sid) {
  return `tracking:eta:${id}:${sid}`;
}

async function main() {
  if (!process.env.TRACKING_DATABASE_URL) {
    throw new Error('TRACKING_DATABASE_URL is required, for example postgresql://postgres:postgres@localhost:5432/vietride_tracking');
  }

  const prisma = new PrismaClient();
  const redis = new Redis(redisUrl, { lazyConnect: true });

  const points = [
    {
      tripId,
      latitude: '10.7626220',
      longitude: '106.6601720',
      speedKmh: '32.50',
      headingDeg: '90.00',
      recordedAt: new Date(startTime),
    },
    {
      tripId,
      latitude: '10.7630000',
      longitude: '106.6610000',
      speedKmh: '34.00',
      headingDeg: '95.00',
      recordedAt: new Date(endTime),
    },
  ];

  const latest = {
    tripId,
    latitude: 10.763,
    longitude: 106.661,
    speedKmh: 34,
    headingDeg: 95,
    recordedAt: endTime,
  };

  const eta = {
    tripId,
    stopId,
    etaMinutes: 12,
    estimatedArrivalTime: '2026-06-03T10:13:00.000Z',
    distanceMeters: 8500,
    updatedAt: '2026-06-03T10:01:00.000Z',
  };

  try {
    await redis.connect();
    await prisma.gpsTrail.deleteMany({
      where: {
        tripId,
        recordedAt: {
          gte: new Date(startTime),
          lte: new Date(endTime),
        },
      },
    });
    await prisma.gpsTrail.createMany({ data: points });

    await redis.set(trackingLatestKey(tripId), JSON.stringify(latest), 'EX', 300);
    await redis.set(trackingEtaKey(tripId, stopId), JSON.stringify(eta), 'EX', 300);

    console.log('Seeded Tracking Postman data');
    console.log(`tripId=${tripId}`);
    console.log(`stopId=${stopId}`);
    console.log(`Redis ${trackingLatestKey(tripId)}`);
    console.log(`Redis ${trackingEtaKey(tripId, stopId)}`);
    console.log(`gps_trails rows=${points.length}`);
    console.log('');
    console.log('Postman 03 still requires accessToken and downstream tracking authorization for tripId.');
  } finally {
    await redis.quit().catch(() => undefined);
    await prisma.$disconnect();
  }
}

main().catch((error) => {
  console.error(error.message);
  process.exitCode = 1;
});
