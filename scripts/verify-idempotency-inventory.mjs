import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const root = process.cwd();
const inventoryPath = path.join(root, 'tests/dotnet/idempotency-endpoint-inventory.json');
const mutationAttribute =
  /\[\s*Http(Post|Patch|Put|Delete)(?:Attribute)?(?:\s*\(\s*"([^"]*)"\s*\))?\s*\]/g;
const skipAttribute = /\[\s*SkipIdempotency\(\s*"([^"]+)"\s*\)\s*\]/g;
const errors = [];
const discoveredEndpoints = new Map();

function fail(message) {
  errors.push(message);
}

function normalizeRoute(route) {
  const normalized = route
    .replace(/\\/g, '/')
    .replace(/^\/+|\/+$/g, '')
    .replace(/\/+/g, '/');
  return `/${normalized}`;
}

export function combineDotNetRoute(controllerRoute, actionRoute, controllerName) {
  const controllerToken = controllerName.replace(/Controller$/, '');
  const expandControllerToken = (route) => route.replace(/\[controller\]/gi, controllerToken);
  const expandedControllerRoute = expandControllerToken(controllerRoute);
  const expandedActionRoute = expandControllerToken(actionRoute);
  const isAbsoluteActionRoute =
    expandedActionRoute.startsWith('/') || expandedActionRoute.startsWith('~/');

  if (isAbsoluteActionRoute) {
    return normalizeRoute(expandedActionRoute.replace(/^~(?=\/)/, ''));
  }

  return normalizeRoute([expandedControllerRoute, expandedActionRoute].filter(Boolean).join('/'));
}

function combineNestRoute(controllerRoute, actionRoute) {
  return normalizeRoute([controllerRoute, actionRoute].filter(Boolean).join('/'));
}

function findControllerRoute(source, classIndex, file, reportError) {
  const routeMatches = [
    ...source.slice(0, classIndex).matchAll(/\[\s*Route\(\s*"([^"]+)"\s*\)\s*\]/g),
  ];
  if (routeMatches.length !== 1) {
    reportError(`${file}: expected exactly one controller [Route], found ${routeMatches.length}`);
    return '';
  }
  return routeMatches[0][1];
}

function findAction(source, attributeEnd, file, reportError) {
  const remainder = source.slice(attributeEnd);
  const nextMutation = remainder.search(/\[\s*Http(?:Post|Patch|Put|Delete)/);
  const method = /\bpublic\s+(?:async\s+)?[\w?.<>,[\]\s]+\s+([A-Za-z_]\w*)\s*\(/.exec(remainder);
  if (!method || (nextMutation !== -1 && nextMutation < method.index)) {
    reportError(`${file}: mutation attribute is not followed by a public action`);
    return null;
  }

  return {
    attributes: remainder.slice(0, method.index),
    name: method[1],
  };
}

function findAttributeBlockStart(source, attributeStart) {
  let blockStart = attributeStart;

  while (blockStart > 0) {
    let cursor = blockStart;
    while (cursor > 0 && /\s/.test(source[cursor - 1])) cursor -= 1;
    if (source[cursor - 1] !== ']') break;

    let depth = 0;
    let openingBracket = -1;
    for (let index = cursor - 1; index >= 0; index -= 1) {
      if (source[index] === ']') depth += 1;
      if (source[index] === '[') {
        depth -= 1;
        if (depth === 0) {
          openingBracket = index;
          break;
        }
      }
    }

    if (openingBracket === -1) break;
    blockStart = openingBracket;
  }

  return blockStart;
}

export function scanControllerSource(
  source,
  serviceName,
  relativeFile,
  requiredMetadata = [],
  reportError = fail,
) {
  const classMatch = /\bclass\s+([A-Za-z_]\w*Controller)\b/.exec(source);
  if (!classMatch) {
    reportError(`${relativeFile}: controller class declaration not found`);
    return {
      controllerName: path.basename(relativeFile, '.cs'),
      mutations: [],
      skipCount: 0,
    };
  }

  const controllerName = classMatch[1];
  const controllerRoute = findControllerRoute(source, classMatch.index, relativeFile, reportError);
  const controllerPrefix = source.slice(0, classMatch.index);
  if (skipAttribute.test(controllerPrefix)) {
    reportError(`${relativeFile}: controller-level [SkipIdempotency] is not auditable per route`);
  }
  skipAttribute.lastIndex = 0;

  const mutations = [];
  mutationAttribute.lastIndex = 0;
  let attribute;
  while ((attribute = mutationAttribute.exec(source)) !== null) {
    const attributeBlockStart = findAttributeBlockStart(source, attribute.index);
    const action = findAction(source, mutationAttribute.lastIndex, relativeFile, reportError);
    if (!action) continue;

    const actionAttributes =
      source.slice(attributeBlockStart, mutationAttribute.lastIndex) + action.attributes;
    if (/\[\s*NonAction(?:Attribute)?\s*\]/.test(actionAttributes)) {
      continue;
    }

    skipAttribute.lastIndex = 0;
    const skipMatches = [...actionAttributes.matchAll(skipAttribute)];
    if (skipMatches.length > 1) {
      reportError(`${relativeFile}:${action.name}: multiple [SkipIdempotency] attributes`);
    }

    const method = attribute[1].toUpperCase();
    const route = combineDotNetRoute(controllerRoute, attribute[2] ?? '', controllerName);
    mutations.push({
      serviceName,
      controllerName,
      actionName: action.name,
      method,
      path: route,
      exemptionReason: skipMatches[0]?.[1] ?? null,
      requiredBy: requiredMetadata.filter((attributeName) => {
        const pattern = new RegExp(
          `\\[\\s*${attributeName}(?:Attribute)?(?:\\s*\\([^\\]]*\\))?\\s*\\]`,
        );
        return pattern.test(actionAttributes);
      }),
      file: relativeFile,
    });
  }

  const skipCount = [...source.matchAll(skipAttribute)].length;
  return { controllerName, mutations, skipCount };
}

function scanController(file, serviceName, requiredMetadata = []) {
  const source = fs.readFileSync(file, 'utf8');
  const relativeFile = path.relative(root, file).replace(/\\/g, '/');
  return scanControllerSource(source, serviceName, relativeFile, requiredMetadata);
}

function listControllerFiles(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return listControllerFiles(fullPath);
    return entry.isFile() && entry.name.endsWith('Controller.cs') ? [fullPath] : [];
  });
}

function validateProgram(serviceName, service) {
  const apiName = serviceName[0].toUpperCase() + serviceName.slice(1);
  const programPath = path.join(
    root,
    'apps',
    serviceName,
    'src',
    `VietRide.${apiName}.Api`,
    'Program.cs',
  );
  if (!fs.existsSync(programPath)) {
    fail(`${serviceName}: Program.cs not found at ${path.relative(root, programPath)}`);
    return;
  }

  const source = fs.readFileSync(programPath, 'utf8');
  const escapedPrefix = service.servicePrefix.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const requireAllRegistration = new RegExp(
    `AddVietRideIdempotency\\s*\\(\\s*"${escapedPrefix}"\\s*,\\s*requireAllMutations\\s*:\\s*true\\s*\\)`,
  );
  const explicitRegistration = new RegExp(
    `AddVietRideIdempotency\\s*\\(\\s*"${escapedPrefix}"(?:\\s*,\\s*requireAllMutations\\s*:\\s*false)?\\s*\\)`,
  );
  if (service.requireAllMutations && !requireAllRegistration.test(source)) {
    fail(
      `${serviceName}: Program.cs must register service prefix "${service.servicePrefix}" with requireAllMutations: true`,
    );
  }
  if (!service.requireAllMutations && !explicitRegistration.test(source)) {
    fail(
      `${serviceName}: Program.cs must register service prefix "${service.servicePrefix}" without require-all mutations`,
    );
  }
  if (!/\bUseVietRideIdempotency\s*\(\s*\)/.test(source)) {
    fail(`${serviceName}: Program.cs must call UseVietRideIdempotency()`);
  }
}

function validateExactEndpoints(serviceName, service, mutations) {
  const endpointKey = (entry) => `${entry.method.toUpperCase()} ${normalizeRoute(entry.path)}`;
  const expected = new Map();
  for (const endpoint of service.endpoints ?? []) {
    const key = endpointKey(endpoint);
    if (expected.has(key)) fail(`${serviceName}: duplicate inventory endpoint ${key}`);
    if (!['required', 'exempt'].includes(endpoint.policy)) {
      fail(`${serviceName}: ${key} must be classified as required or exempt`);
    }
    if (endpoint.policy === 'exempt' && !endpoint.reason?.trim()) {
      fail(`${serviceName}: exempt endpoint ${key} must include a reason`);
    }
    expected.set(key, endpoint);
  }

  const actual = new Map();
  for (const mutation of mutations) {
    const key = endpointKey(mutation);
    const policy = mutation.exemptionReason
      ? 'exempt'
      : mutation.requiredBy?.length > 0 || service.requireAllMutations
        ? 'required'
        : 'unclassified';
    if (actual.has(key)) fail(`${serviceName}: duplicate runtime mutation ${key}`);
    actual.set(key, { ...mutation, policy });
    const approved = expected.get(key);
    if (!approved) {
      fail(`${mutation.file}:${mutation.actionName}: ${key} is missing from the exact inventory`);
      continue;
    }
    if (approved.policy !== policy) {
      fail(
        `${mutation.file}:${mutation.actionName}: ${key} runtime=${policy} inventory=${approved.policy}`,
      );
    }
    if (policy === 'exempt' && approved.reason !== mutation.exemptionReason) {
      fail(`${mutation.file}:${mutation.actionName}: exemption reason mismatch for ${key}`);
    }
  }
  for (const key of expected.keys()) {
    if (!actual.has(key))
      fail(`${serviceName}: inventory endpoint ${key} does not exist at runtime`);
  }
  if (actual.size !== expected.size) {
    fail(
      `${serviceName}: exact inventory has ${expected.size} endpoints but runtime has ${actual.size}`,
    );
  }
}

function validateService(serviceName, service) {
  validateProgram(serviceName, service);

  const apiName = serviceName[0].toUpperCase() + serviceName.slice(1);
  const controllerDirectory = path.join(
    root,
    'apps',
    serviceName,
    'src',
    `VietRide.${apiName}.Api`,
    'Controllers',
  );
  const files = listControllerFiles(controllerDirectory);
  const scanned = files.map((file) => scanController(file, serviceName));
  const mutations = scanned.flatMap((controller) => controller.mutations);
  discoveredEndpoints.set(
    serviceName,
    mutations.map((mutation) => ({
      method: mutation.method,
      path: mutation.path,
      policy: mutation.exemptionReason ? 'exempt' : 'required',
      ...(mutation.exemptionReason ? { reason: mutation.exemptionReason } : {}),
    })),
  );
  validateExactEndpoints(serviceName, service, mutations);
  const approvedControllers = new Set(service.controllers ?? []);

  if (approvedControllers.size !== (service.controllers ?? []).length) {
    fail(`${serviceName}: duplicate controller entry in inventory`);
  }

  if (!service.endpoints) {
    for (const mutation of mutations) {
      if (!mutation.exemptionReason && !approvedControllers.has(mutation.controllerName)) {
        fail(
          `${mutation.file}:${mutation.actionName}: ${mutation.method} ${mutation.path} is not covered by the controller inventory or [SkipIdempotency]`,
        );
      }
    }

    for (const controllerName of approvedControllers) {
      const controller = scanned.find((item) => item.controllerName === controllerName);
      if (!controller) {
        fail(`${serviceName}: inventory controller ${controllerName} does not exist`);
        continue;
      }
      if (!controller.mutations.some((mutation) => !mutation.exemptionReason)) {
        fail(`${serviceName}: inventory controller ${controllerName} has no non-exempt mutation`);
      }
    }
  }

  const codeExemptions = mutations.filter((mutation) => mutation.exemptionReason);
  const discoveredSkipCount = scanned.reduce(
    (total, controller) => total + controller.skipCount,
    0,
  );
  if (discoveredSkipCount !== codeExemptions.length) {
    fail(
      `${serviceName}: found ${discoveredSkipCount} [SkipIdempotency] attributes but only ${codeExemptions.length} are attached to mutation actions`,
    );
  }

  const exemptionKey = (entry) => `${entry.method.toUpperCase()} ${normalizeRoute(entry.path)}`;
  const inventoryExemptions = new Map();
  const approvedExemptions = service.endpoints
    ? service.endpoints.filter((endpoint) => endpoint.policy === 'exempt')
    : (service.exemptions ?? []);
  for (const exemption of approvedExemptions) {
    const key = exemptionKey(exemption);
    if (inventoryExemptions.has(key)) {
      fail(`${serviceName}: duplicate exemption ${key}`);
    }
    if (!exemption.reason?.trim()) {
      fail(`${serviceName}: exemption ${key} must include a reason`);
    }
    inventoryExemptions.set(key, exemption.reason);
  }

  const codeExemptionKeys = new Set();
  for (const exemption of codeExemptions) {
    const key = exemptionKey(exemption);
    codeExemptionKeys.add(key);
    const approvedReason = inventoryExemptions.get(key);
    if (approvedReason === undefined) {
      fail(
        `${exemption.file}:${exemption.actionName}: exemption ${key} is missing from the inventory`,
      );
    } else if (approvedReason !== exemption.exemptionReason) {
      fail(`${exemption.file}:${exemption.actionName}: exemption reason mismatch for ${key}`);
    }
  }

  for (const key of inventoryExemptions.keys()) {
    if (!codeExemptionKeys.has(key)) {
      fail(`${serviceName}: inventory exemption ${key} is not attached to a mutation action`);
    }
  }

  if (
    Number.isInteger(service.expectedMutationCount) &&
    mutations.length !== service.expectedMutationCount
  ) {
    fail(
      `${serviceName}: expected ${service.expectedMutationCount} mutations, found ${mutations.length}; classify the endpoint and update the inventory`,
    );
  }

  return {
    controllers: approvedControllers.size,
    mutations: mutations.length,
    exemptions: codeExemptions.length,
  };
}

function validateMetadataDeclarations(serviceName, service) {
  for (const [attributeName, relativeFile] of Object.entries(service.metadataAttributes ?? {})) {
    const file = path.join(root, relativeFile);
    if (!fs.existsSync(file)) {
      fail(`${serviceName}: metadata attribute file not found: ${relativeFile}`);
      continue;
    }

    const source = fs.readFileSync(file, 'utf8');
    const declaration = new RegExp(
      `class\\s+${attributeName}(?:Attribute)?\\b[^\\r\\n{]*IIdempotencyPolicyMetadata`,
    );
    if (!declaration.test(source)) {
      fail(`${relativeFile}: ${attributeName} must implement IIdempotencyPolicyMetadata`);
    }
  }
}

function validateExplicitDotnetService(serviceName, service) {
  validateProgram(serviceName, service);
  validateMetadataDeclarations(serviceName, service);

  const apiName = serviceName[0].toUpperCase() + serviceName.slice(1);
  const controllerDirectory = path.join(
    root,
    'apps',
    serviceName,
    'src',
    `VietRide.${apiName}.Api`,
    'Controllers',
  );
  const metadataNames = Object.keys(service.metadataAttributes ?? {});
  const files = listControllerFiles(controllerDirectory);
  const scanned = files.map((file) => scanController(file, serviceName, metadataNames));
  const mutations = scanned.flatMap((controller) => controller.mutations);
  const required = mutations.filter((mutation) => mutation.requiredBy.length > 0);
  discoveredEndpoints.set(
    serviceName,
    mutations.map((mutation) => ({
      method: mutation.method,
      path: mutation.path,
      policy: mutation.requiredBy.length > 0 ? 'required' : 'unclassified',
    })),
  );

  for (const mutation of mutations) {
    if (mutation.exemptionReason) {
      fail(
        `${mutation.file}:${mutation.actionName}: explicit-policy services must not use [SkipIdempotency] without an inventory exemption model`,
      );
    }
    if (mutation.requiredBy.length > 1) {
      fail(
        `${mutation.file}:${mutation.actionName}: multiple idempotency metadata attributes: ${mutation.requiredBy.join(', ')}`,
      );
    }
  }

  if (
    Number.isInteger(service.expectedMutationCount) &&
    mutations.length !== service.expectedMutationCount
  ) {
    fail(
      `${serviceName}: expected ${service.expectedMutationCount} mutations, found ${mutations.length}; classify the endpoint and update the inventory`,
    );
  }
  if (
    Number.isInteger(service.expectedRequiredCount) &&
    required.length !== service.expectedRequiredCount
  ) {
    fail(
      `${serviceName}: expected ${service.expectedRequiredCount} explicitly required mutations, found ${required.length}`,
    );
  }

  return { controllers: files.length, mutations: mutations.length, required: required.length };
}

function listFiles(directory, predicate) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) return listFiles(fullPath, predicate);
    return entry.isFile() && predicate(fullPath) ? [fullPath] : [];
  });
}

export function scanNestControllerSource(
  source,
  serviceName,
  decoratorName,
  relativeFile,
  reportError = fail,
) {
  const controller = /@Controller\(\s*(?:(['"])(.*?)\1)?\s*\)/.exec(source);
  if (!controller) {
    reportError(`${relativeFile}: @Controller route not found`);
    return [];
  }

  const mutations = [];
  const httpDecorator = /@(Post|Patch|Put|Delete)\(\s*(?:(['"])(.*?)\2)?\s*\)/g;
  let match;
  while ((match = httpDecorator.exec(source)) !== null) {
    const remainder = source.slice(httpDecorator.lastIndex);
    const nextMutation = remainder.search(/@(Post|Patch|Put|Delete)\s*\(/);
    const method = /(?:^|\r?\n)\s*(?:public\s+)?(?:async\s+)?([A-Za-z_]\w*)\s*\(/m.exec(remainder);
    if (!method || (nextMutation !== -1 && nextMutation < method.index)) {
      reportError(`${relativeFile}: @${match[1]} is not followed by a controller action`);
      continue;
    }

    const attributes = remainder.slice(0, method.index);
    const decoratorPattern = new RegExp(`@${decoratorName}\\s*\\(\\s*\\)`);
    const exemptDecoratorPattern = /@ApiIdempotencyExempt\s*\(\s*[^)]*\)/;
    mutations.push({
      serviceName,
      actionName: method[1],
      method: match[1].toUpperCase(),
      path: combineNestRoute(controller[2] ?? '', match[3] ?? ''),
      requiredBy: decoratorPattern.test(attributes) ? [decoratorName] : [],
      exemptionReason: exemptDecoratorPattern.test(attributes)
        ? (/@ApiIdempotencyExempt\s*\(\s*['"]([^'"]+)['"]\s*,?\s*\)/.exec(attributes)?.[1] ?? null)
        : null,
      file: relativeFile,
    });
  }

  return mutations;
}

function scanNestController(file, serviceName, decoratorName) {
  const source = fs.readFileSync(file, 'utf8');
  const relativeFile = path.relative(root, file).replace(/\\/g, '/');
  return scanNestControllerSource(source, serviceName, decoratorName, relativeFile);
}

function validateNestService(serviceName, service) {
  const sourceDirectory = path.join(root, service.sourceDirectory);
  const files = listFiles(
    sourceDirectory,
    (file) => file.endsWith('.controller.ts') && !file.endsWith('.spec.ts'),
  );
  const mutations = files.flatMap((file) =>
    scanNestController(file, serviceName, service.requiredDecorator),
  );
  const required = mutations.filter((mutation) => mutation.requiredBy.length > 0);
  discoveredEndpoints.set(
    serviceName,
    mutations.map((mutation) => ({
      method: mutation.method,
      path: mutation.path,
      policy: mutation.exemptionReason
        ? 'exempt'
        : mutation.requiredBy.length > 0
          ? 'required'
          : 'unclassified',
      ...(mutation.exemptionReason ? { reason: mutation.exemptionReason } : {}),
    })),
  );
  validateExactEndpoints(serviceName, service, mutations);

  if (
    Number.isInteger(service.expectedMutationCount) &&
    mutations.length !== service.expectedMutationCount
  ) {
    fail(
      `${serviceName}: expected ${service.expectedMutationCount} mutations, found ${mutations.length}; classify the endpoint and update the inventory`,
    );
  }
  if (
    Number.isInteger(service.expectedRequiredCount) &&
    required.length !== service.expectedRequiredCount
  ) {
    fail(
      `${serviceName}: expected ${service.expectedRequiredCount} decorated mutations, found ${required.length}`,
    );
  }
  if (required.length > 0) {
    const decoratorFile = path.join(root, service.decoratorFile);
    const decoratorSource = fs.readFileSync(decoratorFile, 'utf8');
    for (const requiredToken of ['ApiHeader', 'ApiExtension']) {
      if (!decoratorSource.includes(requiredToken)) {
        fail(
          `${service.decoratorFile}: ${service.requiredDecorator} must compose ${requiredToken}`,
        );
      }
    }
  }

  return { controllers: files.length, mutations: mutations.length, required: required.length };
}

function validateSocketOperations(socketOperations) {
  const gatewayFile = path.join(root, 'apps/tracking/src/location/location.gateway.ts');
  const source = fs.readFileSync(gatewayFile, 'utf8');
  const discovered = [...source.matchAll(/@SubscribeMessage\(\s*['"]([^'"]*gps:update)['"]\s*\)/g)]
    .map((match) => match[1])
    .sort();
  const expected = (socketOperations ?? []).map((entry) => entry.event).sort();

  if (JSON.stringify(discovered) !== JSON.stringify(expected)) {
    fail(
      `tracking socket inventory mismatch: runtime=${discovered.join(', ')} inventory=${expected.join(', ')}`,
    );
  }
  for (const operation of socketOperations ?? []) {
    if (operation.service !== 'tracking' || operation.policy !== 'required') {
      fail(`socket ${operation.event}: must be tracking/required`);
    }
  }

  const requiredTokens = [
    'trackingGpsIdempotencyKey',
    'shuttleGpsIdempotencyKey',
    'GPS_OPERATION_PAYLOAD_MISMATCH',
    'if (result.duplicate)',
  ];
  const trackingSource = [
    source,
    fs.readFileSync(path.join(root, 'apps/tracking/src/location/location.service.ts'), 'utf8'),
    fs.readFileSync(path.join(root, 'apps/tracking/src/shuttle/shuttle.service.ts'), 'utf8'),
  ].join('\n');
  for (const token of requiredTokens) {
    if (!trackingSource.includes(token)) {
      fail(`tracking socket idempotency implementation is missing ${token}`);
    }
  }
}

function validateCrossSystemCoverage(coverage) {
  const dotnetServices = ['identity', 'trip', 'booking', 'payment', 'parcel'];
  const dotnetSourceFiles = dotnetServices.flatMap((service) =>
    listFiles(path.join(root, 'apps', service, 'src'), (file) => file.endsWith('.cs')),
  );
  const handlerCount = dotnetSourceFiles.reduce((total, file) => {
    const source = fs.readFileSync(file, 'utf8');
    return total + [...source.matchAll(/IIntegrationEventHandler\s*</g)].length;
  }, 0);
  if (handlerCount !== coverage.dotnetRabbitMqRegistrations) {
    fail(
      `.NET RabbitMQ inventory drift: expected ${coverage.dotnetRabbitMqRegistrations}, found ${handlerCount}`,
    );
  }

  for (const service of ['identity', 'trip', 'booking', 'parcel']) {
    const serviceSources = dotnetSourceFiles
      .filter((file) => file.includes(`${path.sep}apps${path.sep}${service}${path.sep}`))
      .map((file) => fs.readFileSync(file, 'utf8'))
      .join('\n');
    if (
      !serviceSources.includes('AddVietRideIntegrationInbox<') ||
      !serviceSources.includes('AddVietRideIntegrationInbox()')
    ) {
      fail(`${service}: RabbitMQ handlers exist but durable integration inbox is not fully wired`);
    }
  }

  const notificationFiles = listFiles(
    path.join(root, 'apps/notification/src'),
    (file) => file.endsWith('.ts') && !file.endsWith('.spec.ts'),
  );
  const subscriptionCount = notificationFiles.reduce((total, file) => {
    const source = fs.readFileSync(file, 'utf8');
    return total + [...source.matchAll(/\.subscribe\s*\(/g)].length;
  }, 0);
  if (subscriptionCount !== coverage.notificationRabbitMqSubscriptions) {
    fail(
      `Notification RabbitMQ inventory drift: expected ${coverage.notificationRabbitMqSubscriptions}, found ${subscriptionCount}`,
    );
  }

  const outboundPattern = /HttpMethod\.(Post|Put|Patch|Delete)\b/g;
  const outboundCallsites = [];
  for (const file of dotnetSourceFiles) {
    const source = fs.readFileSync(file, 'utf8');
    for (const match of source.matchAll(outboundPattern)) {
      outboundCallsites.push({ file, source, method: match[1] });
    }
  }
  if (outboundCallsites.length !== coverage.dotnetOutboundHttpMutations) {
    fail(
      `.NET outbound HTTP mutation inventory drift: expected ${coverage.dotnetOutboundHttpMutations}, found ${outboundCallsites.length}`,
    );
  }

  const exemptions = new Set(coverage.dotnetOutboundHttpExemptions ?? []);
  for (const callsite of outboundCallsites) {
    const relativeFile = path.relative(root, callsite.file).replace(/\\/g, '/');
    if (exemptions.has(relativeFile)) continue;
    if (!callsite.source.includes('Idempotency-Key')) {
      fail(`${relativeFile}: outbound ${callsite.method} does not forward Idempotency-Key`);
    }
  }
  for (const exemption of exemptions) {
    if (
      !outboundCallsites.some(
        (callsite) => path.relative(root, callsite.file).replace(/\\/g, '/') === exemption,
      )
    ) {
      fail(`outbound HTTP exemption no longer exists: ${exemption}`);
    }
  }

  const forbiddenInternalKey =
    /(?:Idempotency-Key|idempotencyKey)[^\r\n]{0,100}\$"[^"\r\n]*(?:cargo:|refund:|booking-|payment:|parcel:)/i;
  for (const file of dotnetSourceFiles) {
    const source = fs.readFileSync(file, 'utf8');
    if (forbiddenInternalKey.test(source)) {
      fail(`${path.relative(root, file)}: prefixed internal idempotency key is forbidden`);
    }
  }

  const routeTable = path.join(root, coverage.gatewayRouteTable ?? '');
  if (!fs.existsSync(routeTable)) {
    fail(`Gateway route table not found: ${coverage.gatewayRouteTable}`);
  } else {
    const routeSource = fs.readFileSync(routeTable, 'utf8');
    for (const service of [
      'IDENTITY',
      'TRIP',
      'BOOKING',
      'PAYMENT',
      'PARCEL',
      'NOTIFICATION',
      'RAG',
    ]) {
      if (!routeSource.includes(`env.${service}_BASE_URL`)) {
        fail(`Gateway route table has no ${service} target`);
      }
    }
  }
}

function main() {
  const inventory = JSON.parse(fs.readFileSync(inventoryPath, 'utf8'));
  if (inventory.version !== 3 || inventory.policy !== 'system-wide-idempotency-contract') {
    fail('inventory must use version 3 and policy system-wide-idempotency-contract');
  }

  const expectedServices = [
    'identity',
    'trip',
    'booking',
    'payment',
    'parcel',
    'notification',
    'rag',
  ];
  const actualServices = Object.keys(inventory.services ?? {}).sort();
  if (JSON.stringify(actualServices) !== JSON.stringify([...expectedServices].sort())) {
    fail(`inventory services must be exactly: ${expectedServices.join(', ')}`);
  }

  const results = [];
  for (const serviceName of expectedServices) {
    const service = inventory.services?.[serviceName];
    if (!service) continue;
    const result =
      service.stack === 'nestjs'
        ? validateNestService(serviceName, service)
        : service.requireAllMutations
          ? validateService(serviceName, service)
          : validateExplicitDotnetService(serviceName, service);
    results.push([serviceName, result]);
  }

  validateSocketOperations(inventory.socketOperations);
  validateCrossSystemCoverage(inventory.coverage ?? {});

  if (process.argv.includes('--discover')) {
    console.log(JSON.stringify(Object.fromEntries(discoveredEndpoints), null, 2));
    process.exit(0);
  }

  if (errors.length > 0) {
    console.error('idempotency inventory FAIL');
    for (const error of errors) console.error(`- ${error}`);
    process.exitCode = 1;
  } else {
    const details = results
      .map(([name, result]) =>
        result.exemptions === undefined
          ? `${name}=${result.controllers} controllers/${result.mutations} mutations/${result.required} required`
          : `${name}=${result.controllers} controllers/${result.mutations} mutations/${result.exemptions} exemptions`,
      )
      .join(', ');
    console.log(`idempotency inventory PASS (${details})`);
  }
}

const isMain =
  process.argv[1] && pathToFileURL(path.resolve(process.argv[1])).href === import.meta.url;
if (isMain) {
  main();
}
