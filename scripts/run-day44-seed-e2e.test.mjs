import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');

describe('Day 44 seed orchestrator harness', () => {
  const source = fs.readFileSync(new URL('./run-day44-seed-e2e.mjs', import.meta.url), 'utf8');
  const compose = fs.readFileSync(
    new URL('../infra/docker/docker-compose.day44-e2e.yml', import.meta.url),
    'utf8',
  );

  test('uses a unique Compose project and always removes its volumes', () => {
    assert.match(source, /const composeProject = `day44-e2e-\$\{invocationId\}`/);
    assert.match(source, /'down', '-v', '--remove-orphans'/);
    assert.match(compose, /DAY44_CONTAINER_PREFIX/);
  });

  test('proves two bounded runs, provider isolation, counts, and gateway smokes', () => {
    assert.match(source, /index <= 2/);
    assert.match(source, /elapsed >= 120000/);
    for (const marker of [
      'IDEMPOTENT_RERUN=PASS',
      'RAG_READY=PASS',
      'OPERATOR_DISTRICT_REMOVAL_E2E=PASS',
      'LOCATION_HIERARCHY_CATALOG_E2E=PASS',
      'LEAF_LOCATION_RESOURCE_CREATE_E2E=PASS',
      'ACCENT_INSENSITIVE_RESOURCE_SEARCH_E2E=PASS',
      'TRIP_LOCATION_STOP_SEARCH_E2E=PASS',
      'TRIP_30_DAY_GENERATION_E2E=PASS',
      'OPERATOR_INCIDENT_READ_E2E=PASS',
      'TRIP_SEARCH_TO_BOOKING_E2E=PASS',
      'BOOKING_READY=PASS',
      'PARCEL_READY=PASS',
      'DAY44_RUN=PASS',
    ])
      assert.ok(source.includes(marker));
    assert.match(source, /providerRequests !== '0'/);
    assert.match(compose, /internal: true/);
    assert.match(compose, /provider-trap/);
    assert.doesNotMatch(source, /SHOPAIKEY_API_KEY:\s*[^'"\s][^,\n]*/);
  });

  test('renders an internally reachable provider trap without weakening isolation', () => {
    const rendered = spawnSync(
      'docker',
      [
        'compose',
        '--env-file',
        '.env.example',
        '-f',
        'infra/docker/docker-compose.yml',
        '-f',
        'infra/docker/docker-compose.day44-e2e.yml',
        '--profile',
        'app',
        'config',
        '--format',
        'json',
      ],
      {
        cwd: root,
        encoding: 'utf8',
        env: {
          ...process.env,
          DAY44_CONTAINER_PREFIX: 'day44-e2e-static-test',
        },
      },
    );
    assert.equal(rendered.status, 0, rendered.stderr || rendered.stdout);
    const config = JSON.parse(rendered.stdout);
    const trap = config.services['provider-trap'];
    assert.deepEqual(trap.command.slice(0, 2), ['node', '-e']);
    assert.match(trap.command[2], /req\.url==='\/health'/);
    assert.match(trap.command[2], /listen\(8080,'0\.0\.0\.0'\)/);
    assert.equal(trap.ports, undefined);
    assert.match(trap.healthcheck.test.join(' '), /http:\/\/127\.0\.0\.1:8080\/health/);
    assert.equal(config.services.rag.depends_on['provider-trap'].condition, 'service_healthy');
    assert.equal(
      config.services.rag.environment.CLOUDINARY_CLOUD_NAME,
      'day44-disabled-cloudinary',
    );
    assert.equal(
      config.services.rag.environment.CLOUDINARY_API_KEY,
      'day44-disabled-cloudinary-key',
    );
    assert.equal(
      config.services.rag.environment.CLOUDINARY_API_SECRET,
      'day44-disabled-cloudinary-secret',
    );
    assert.equal(config.networks.default.internal, true);
    assert.match(source, /const healthUrl = 'http:\/\/127\.0\.0\.1:8080\/health'/);
    assert.match(source, /'exec',[\s\S]*`\$\{containerPrefix\}-provider-trap`/);
    assert.match(source, /waitForProviderTrap\(\)/);
  });

  test('emits bounded provider diagnostics before unconditional cleanup', () => {
    assert.ok(source.includes('DAY44_PROVIDER_TRAP_DIAGNOSTIC=${label}'));
    for (const diagnostic of ['compose ps', 'provider-trap logs', 'provider-trap inspect'])
      assert.ok(source.includes(`'${diagnostic}'`));
    assert.match(source, /status=\{\{\.State\.Status\}\}/);
    assert.match(source, /health=\{\{json \.State\.Health\}\}/);
    assert.match(source, /ports=\{\{json \.NetworkSettings\.Ports\}\}/);
    assert.doesNotMatch(source, /json \.Config\.Env/);
    assert.match(source, /'logs', '--no-color', '--tail', '100', 'provider-trap'/);
    assert.match(source, /const outputLimit = 4000/);
    assert.match(source, /output\.slice\(0, outputLimit\)/);
    const lifecycleStart = source.indexOf('function startAndVerifyProviderTrap()');
    const lifecycleEnd = source.indexOf('\nfunction assertCount', lifecycleStart);
    const lifecycle = source.slice(lifecycleStart, lifecycleEnd);
    const up = lifecycle.indexOf("run('docker', [...compose, 'up', '-d', '--build'])");
    const probe = lifecycle.indexOf('waitForProviderTrap();');
    const catchBlock = lifecycle.indexOf('catch (error)');
    const lifecycleDiagnostics = lifecycle.indexOf('emitProviderTrapDiagnostics();');
    const rethrow = lifecycle.indexOf('throw error;');
    assert.ok(
      up >= 0 &&
        probe > up &&
        catchBlock > probe &&
        lifecycleDiagnostics > catchBlock &&
        rethrow > lifecycleDiagnostics,
    );
    const diagnostics = source.indexOf('emitProviderTrapDiagnostics();');
    const cleanup = source.indexOf("'down', '-v', '--remove-orphans'");
    assert.ok(diagnostics >= 0 && cleanup > diagnostics);
  });

  test('runs Gateway health and smoke inside the container with secret-safe transport', () => {
    const rendered = spawnSync(
      'docker',
      [
        'compose',
        '--env-file',
        '.env.example',
        '-f',
        'infra/docker/docker-compose.yml',
        '-f',
        'infra/docker/docker-compose.day44-e2e.yml',
        '--profile',
        'app',
        'config',
        '--format',
        'json',
      ],
      {
        cwd: root,
        encoding: 'utf8',
        env: { ...process.env, DAY44_CONTAINER_PREFIX: 'day44-e2e-static-test' },
      },
    );
    assert.equal(rendered.status, 0, rendered.stderr || rendered.stdout);
    const config = JSON.parse(rendered.stdout);
    assert.equal(config.services.gateway.ports, undefined);
    assert.equal(config.networks.default.internal, true);
    assert.doesNotMatch(source, /GATEWAY_PORT|gatewayBaseUrl|curl\.exe/);
    assert.match(source, /fetch\('http:\/\/127\.0\.0\.1:3000\/health'\)/);
    assert.match(source, /\['exec', '-i', gatewayContainer, 'node', '-e', gatewayRequestScript\]/);
    assert.match(source, /input: JSON\.stringify\(\{ path, options \}\)/);
    assert.match(source, /process\.stdin\.on\('data'/);
    assert.doesNotMatch(source, /docker[\s\S]{0,120}(?:password|accessToken|authorization)/i);
    assert.doesNotMatch(source, /returned \$\{response\.status\}:|JSON\.stringify\(body\)/);
  });

  test('emits bounded Gateway diagnostics before cleanup on health or smoke failure', () => {
    assert.ok(source.includes('DAY44_GATEWAY_DIAGNOSTIC=${label}'));
    for (const diagnostic of ['compose ps', 'gateway logs', 'gateway inspect'])
      assert.ok(source.includes(`'${diagnostic}'`));
    assert.match(source, /'logs', '--no-color', '--tail', '100', 'gateway'/);
    assert.match(source, /async function withGatewayDiagnostics\(action\)/);
    assert.match(source, /return await action\(\)/);
    assert.match(source, /catch \(error\)[\s\S]*emitGatewayDiagnostics\(\);[\s\S]*throw error;/);
    assert.match(source, /await withGatewayDiagnostics\(\(\) => waitForGateway\(\)\)/);
    assert.match(source, /await withGatewayDiagnostics\(\(\) => currentFeatureSmoke\(\)\)/);
    assert.match(source, /await withGatewayDiagnostics\(\(\) => smoke\(\)\)/);
    assert.doesNotMatch(source, /json \.Config\.Env/);
    const diagnostics = source.indexOf('emitGatewayDiagnostics();');
    const cleanup = source.indexOf("'down', '-v', '--remove-orphans'");
    assert.ok(diagnostics >= 0 && cleanup > diagnostics);
  });

  test('requires RAG readiness and emits bounded secret-safe diagnostics before cleanup', () => {
    assert.match(source, /const ragContainer = `\$\{containerPrefix\}-rag`/);
    assert.match(source, /fetch\('http:\/\/127\.0\.0\.1:3003\/health'\)/);
    assert.match(source, /\['exec', ragContainer, 'node', '-e', probeScript\]/);
    assert.match(source, /verifyRagReady\(\);/);
    assert.ok(source.includes('DAY44_RAG_DIAGNOSTIC=${label}'));
    for (const diagnostic of ['compose ps', 'rag logs', 'rag inspect'])
      assert.ok(source.includes(`'${diagnostic}'`));
    assert.match(source, /'logs', '--no-color', '--tail', '100', 'rag'/);
    assert.match(
      source,
      /function verifyRagReady\(\)[\s\S]*waitForRag\(\);[\s\S]*catch \(error\)[\s\S]*emitRagDiagnostics\(\);[\s\S]*throw error;/,
    );
    assert.doesNotMatch(source, /json \.Config\.Env/);
    const diagnostics = source.indexOf('emitRagDiagnostics();');
    const cleanup = source.indexOf("'down', '-v', '--remove-orphans'");
    assert.ok(diagnostics >= 0 && cleanup > diagnostics);
  });

  test('never promotes huge child stdout into outer harness errors', () => {
    assert.match(source, /if \(result\.error\)/);
    assert.match(source, /\(result\.stderr \?\? ''\)\.slice\(0, 4000\)/);
    assert.doesNotMatch(source, /result\.stderr \|\| result\.stdout/);
    assert.doesNotMatch(source, /result\.stdout \|\| result\.stderr/);
    assert.match(source, /stderr unavailable or suppressed/);
  });
});
