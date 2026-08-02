import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import test from 'node:test';

const root = process.cwd();
const harnessPath = path.join(root, 'scripts', 'run-day41-43-e2e.mjs');
const harness = fs.readFileSync(harnessPath, 'utf8');

function read(relativePath) {
  return fs.readFileSync(path.join(root, relativePath), 'utf8');
}

function collectFiles(relativeDirectory, extensions) {
  const files = [];
  const visit = (directory) => {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(fullPath);
      else if (extensions.has(path.extname(entry.name))) files.push(fullPath);
    }
  };
  visit(path.join(root, relativeDirectory));
  return files;
}

test('Day 41 asserts exact non-empty tenant B rows for all six XLSX reports', () => {
  const expected = [
    ['bookings', 'booking', 'Bookings'],
    ['parcels', 'parcel', 'Parcels'],
    ['revenue', 'payment', 'Revenue'],
    ['occupancy', 'trip', 'Occupancy'],
    ['cancellation', 'booking', 'Cancellations'],
    ['refunds', 'payment', 'Refunds'],
  ];
  for (const report of expected) {
    const tuple = report.map((value) => `'${value}'`).join(',\\s*');
    assert.match(harness, new RegExp(tuple), `missing ${report.join('/')}`);
  }
  assert.match(harness, /workbook\.rows === expectedIds\.length \+ 1/);
  assert.match(harness, /tenant B workbook is missing/);
  assert.match(harness, /leaked tenant A aggregate/);
  assert.match(harness, /\[bookingB, cancellationB\]/);
  assert.match(harness, /\[bookingRevenueB, parcelRevenueB, cancellationRevenueB\]/);
  assert.match(harness, /\[refundB\]/);
  assert.match(harness, /Day 41 tenant B booking refund',now\(\)-interval '31 days'/);
  assert.match(harness, /\[tripB\]/);
  assert.equal((harness.match(/assertExactTenantWorkbook\(tenantWorkbook/g) ?? []).length, 1);
});

test('Day 41 tenant B fixture is isolated from the 100k benchmark distribution', () => {
  assert.equal((harness.match(/% 18 \+ 3/g) ?? []).length, 4);
  assert.equal((harness.match(/% 20 \+ 1/g) ?? []).length, 0);
  for (const id of [
    'routeB',
    'vehicleB',
    'driverTenantB',
    'tripB',
    'bookingB',
    'cancellationB',
    'parcelB',
    'bookingRevenueB',
    'parcelRevenueB',
    'refundB',
    'cancellationRevenueB',
  ]) {
    assert.match(harness, new RegExp(`const ${id} =`));
  }
  assert.match(harness, /\('\$\{tripB\}'.*'\$\{driverTenantB\}'/);
  assert.doesNotMatch(harness, /\('\$\{tripB\}'.*'\$\{driverB\}'/);
});

test('Day 42 uses exact [now-28d, now+1d) boundaries and retains the 92-day case', () => {
  assert.match(harness, /const range = reportDateRange\(28, platformNow\)/);
  assert.match(harness, /platformNow - 28 \* 24 \* 60 \* 60 \* 1000/);
  assert.match(harness, /platformNow \+ 24 \* 60 \* 60 \* 1000/);
  assert.match(harness, /rangeDurationDays === 29/);
  assert.match(harness, /const threeMonthRange = reportDateRange\(91\)/);
  assert.match(harness, /threeMonthRangeDays === 92/);
});

test('Day 42 obsolete Payment aggregate seam has zero contract, registry, DI, and caller inventory', () => {
  const registeredSurfaces = [
    'VietRide_API_Contract_v1.md',
    'BACKEND_SOURCE_OF_TRUTH.md',
    'docs/api/postman/vietride.postman_collection.json',
    'apps/gateway/src/config/routes.ts',
  ]
    .map(read)
    .join('\n');
  assert.ok(!registeredSurfaces.includes('internal/v1/reports/platform/aggregate'));

  const bookingClientInterface = path.join(
    root,
    'apps/booking/src/VietRide.Booking.Application/Abstractions/ServiceClients/IPlatformReportAggregateClient.cs',
  );
  const bookingClient = path.join(
    root,
    'apps/booking/src/VietRide.Booking.Infrastructure/Http/PlatformReportAggregateClient.cs',
  );
  assert.ok(!fs.existsSync(bookingClientInterface));
  assert.ok(!fs.existsSync(bookingClient));

  const bookingSources = collectFiles('apps/booking/src', new Set(['.cs']))
    .map((file) => fs.readFileSync(file, 'utf8'))
    .join('\n');
  assert.ok(!bookingSources.includes('IPlatformReportAggregateClient'));
  assert.ok(!bookingSources.includes('PlatformReportAggregateClient'));

  const paymentController = read(
    'apps/payment/src/VietRide.Payment.Api/Controllers/InternalPlatformReportAggregateController.cs',
  );
  assert.ok(!paymentController.includes('[HttpGet("aggregate")]'));
  assert.ok(paymentController.includes('[HttpGet("ledger")]'));
});
