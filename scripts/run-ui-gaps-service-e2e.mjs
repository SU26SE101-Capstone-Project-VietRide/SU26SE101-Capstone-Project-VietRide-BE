import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import process from 'node:process';

const dotnet = 'dotnet';
const nxCli = 'node_modules/nx/dist/bin/nx.js';

const commands = [
  {
    label: 'Identity UI-gap internal HTTP projections',
    minimumTests: 5,
    executable: dotnet,
    args: [
      'test',
      'apps/identity/tests/VietRide.Identity.IntegrationTests/VietRide.Identity.IntegrationTests.csproj',
      '-c',
      'Release',
      '--filter',
      'FullyQualifiedName~UiGapInternalProjectionEndpointTests|FullyQualifiedName~AdminDashboardIdentityMetricsEndpointTests',
    ],
  },
  {
    label: 'Trip operator selector and internal analytics HTTP',
    minimumTests: 12,
    executable: dotnet,
    args: [
      'test',
      'apps/trip/tests/VietRide.Trip.IntegrationTests/VietRide.Trip.IntegrationTests.csproj',
      '-c',
      'Release',
      '--filter',
      'FullyQualifiedName~OperatorTripsListEndpointTests|FullyQualifiedName~InternalTripsEndpointTests.BatchTripSummaries|FullyQualifiedName~InternalOperatorAnalyticsEndpointTests',
    ],
  },
  {
    label: 'Booking report, buyer, stats and dashboard HTTP',
    minimumTests: 15,
    executable: dotnet,
    args: [
      'test',
      'apps/booking/tests/VietRide.Booking.IntegrationTests/VietRide.Booking.IntegrationTests.csproj',
      '-c',
      'Release',
      '--filter',
      'FullyQualifiedName~AdminPlatformReportEndpointTests|FullyQualifiedName~OperatorBookingsEndpointIntegrationTests|FullyQualifiedName~BookingStatsEndpointIntegrationTests|FullyQualifiedName~AdminDashboardEndpointIntegrationTests',
    ],
  },
  {
    label: 'Payment financial and revenue analytics HTTP',
    minimumTests: 19,
    executable: dotnet,
    args: [
      'test',
      'apps/payment/tests/VietRide.Payment.IntegrationTests/VietRide.Payment.IntegrationTests.csproj',
      '-c',
      'Release',
      '--filter',
      'FullyQualifiedName~AdminFinancialProjectionEndpointTests|FullyQualifiedName~AdminRevenueAnalyticsEndpointTests|FullyQualifiedName~OperatorRevenueAnalyticsEndpointTests',
    ],
  },
  {
    label: 'Parcel fare, list/detail and stats HTTP',
    minimumTests: 17,
    executable: dotnet,
    args: [
      'test',
      'apps/parcel/tests/VietRide.Parcel.IntegrationTests/VietRide.Parcel.IntegrationTests.csproj',
      '-c',
      'Release',
      '--filter',
      'FullyQualifiedName~BatchParcelRouteFareEndpointTests|FullyQualifiedName~UiGapOperatorParcelHttpE2ETests|FullyQualifiedName~OperatorParcelStatsEndpointTests',
    ],
  },
  {
    label: 'RAG Policy admin/operator HTTP',
    executable: process.execPath,
    args: [
      nxCli,
      'run',
      'rag:test:e2e',
      '--runInBand',
      '--passWithNoTests=false',
      '--testPathPatterns=src/policies/policies.e2e-spec.ts',
    ],
  },
];

function quote(value) {
  return /\s|[|]/.test(value) ? JSON.stringify(value) : value;
}

function commandLine(command) {
  return [command.executable, ...command.args].map(quote).join(' ');
}

function assertRepositoryRoot() {
  if (!existsSync('package.json') || !existsSync('apps/gateway')) {
    throw new Error('Run this script from the VietRide repository root.');
  }
}

function main() {
  assertRepositoryRoot();
  if (process.argv.includes('--list')) {
    commands.forEach((command, index) => {
      process.stdout.write(`${index + 1}. ${command.label}\n   ${commandLine(command)}\n`);
    });
    return;
  }

  const fromArgument = process.argv.find((argument) => argument.startsWith('--from='));
  const from = fromArgument === undefined ? 1 : Number(fromArgument.slice('--from='.length));
  if (!Number.isInteger(from) || from < 1 || from > commands.length) {
    throw new Error(`--from must be an integer from 1 to ${commands.length}.`);
  }

  for (const [offset, command] of commands.slice(from - 1).entries()) {
    const index = from + offset;
    process.stdout.write(`\n[${index}/${commands.length}] ${command.label}\n`);
    process.stdout.write(`${commandLine(command)}\n`);
    const result = spawnSync(command.executable, command.args, {
      cwd: process.cwd(),
      env: {
        ...process.env,
        DOTNET_CLI_TELEMETRY_OPTOUT: '1',
      },
      encoding: 'utf8',
    });
    process.stdout.write(result.stdout ?? '');
    process.stderr.write(result.stderr ?? '');
    if (result.error) {
      throw result.error;
    }
    if (result.status !== 0) {
      throw new Error(`${command.label} failed with exit code ${result.status ?? 'unknown'}.`);
    }
    if (command.minimumTests !== undefined) {
      const output = `${result.stdout ?? ''}\n${result.stderr ?? ''}`;
      const totals = [...output.matchAll(/Total:\s+(\d+)/g)].map((match) => Number(match[1]));
      const selected = totals.at(-1);
      if (selected === undefined || selected < command.minimumTests) {
        throw new Error(
          `${command.label} selected ${selected ?? 0} tests; expected at least ${command.minimumTests}.`,
        );
      }
    }
  }

  const scope = from === 1 ? 'matrix' : `matrix steps ${from}-${commands.length}`;
  process.stdout.write(`\nPASS | UI-00–UI-22 service-level HTTP E2E ${scope} is green.\n`);
}

try {
  main();
} catch (error) {
  process.stderr.write(`FAIL | ${error instanceof Error ? error.message : String(error)}\n`);
  process.exitCode = 1;
}
