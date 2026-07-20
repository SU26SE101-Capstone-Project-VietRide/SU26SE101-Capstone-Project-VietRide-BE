import fs from 'node:fs';
import path from 'node:path';

const root = process.cwd();
const inventoryPath = path.join(root, 'tests/dotnet/idempotency-endpoint-inventory.json');
const mutationAttribute = /\[\s*Http(Post|Patch|Put|Delete)(?:Attribute)?(?:\s*\(\s*"([^"]*)"\s*\))?\s*\]/g;
const skipAttribute = /\[\s*SkipIdempotency\(\s*"([^"]+)"\s*\)\s*\]/g;
const errors = [];

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

function combineRoute(controllerRoute, actionRoute, controllerName) {
  const controllerToken = controllerName.replace(/Controller$/, '');
  const expandedControllerRoute = controllerRoute.replace(/\[controller\]/gi, controllerToken);
  return normalizeRoute([expandedControllerRoute, actionRoute].filter(Boolean).join('/'));
}

function findControllerRoute(source, classIndex, file) {
  const routeMatches = [...source.slice(0, classIndex).matchAll(/\[\s*Route\(\s*"([^"]+)"\s*\)\s*\]/g)];
  if (routeMatches.length !== 1) {
    fail(`${file}: expected exactly one controller [Route], found ${routeMatches.length}`);
    return '';
  }
  return routeMatches[0][1];
}

function findAction(source, attributeEnd, file) {
  const remainder = source.slice(attributeEnd);
  const nextMutation = remainder.search(/\[\s*Http(?:Post|Patch|Put|Delete)/);
  const method = /\bpublic\s+(?:async\s+)?[\w?.<>,\[\]\s]+\s+([A-Za-z_]\w*)\s*\(/.exec(remainder);
  if (!method || (nextMutation !== -1 && nextMutation < method.index)) {
    fail(`${file}: mutation attribute is not followed by a public action`);
    return null;
  }

  return {
    attributes: remainder.slice(0, method.index),
    name: method[1],
  };
}

function scanController(file, serviceName) {
  const source = fs.readFileSync(file, 'utf8');
  const relativeFile = path.relative(root, file).replace(/\\/g, '/');
  const classMatch = /\bclass\s+([A-Za-z_]\w*Controller)\b/.exec(source);
  if (!classMatch) {
    fail(`${relativeFile}: controller class declaration not found`);
    return { controllerName: path.basename(file, '.cs'), mutations: [], skipCount: 0 };
  }

  const controllerName = classMatch[1];
  const controllerRoute = findControllerRoute(source, classMatch.index, relativeFile);
  const controllerPrefix = source.slice(0, classMatch.index);
  if (skipAttribute.test(controllerPrefix)) {
    fail(`${relativeFile}: controller-level [SkipIdempotency] is not auditable per route`);
  }
  skipAttribute.lastIndex = 0;

  const mutations = [];
  mutationAttribute.lastIndex = 0;
  let attribute;
  while ((attribute = mutationAttribute.exec(source)) !== null) {
    const action = findAction(source, mutationAttribute.lastIndex, relativeFile);
    if (!action) continue;

    skipAttribute.lastIndex = 0;
    const skipMatches = [...action.attributes.matchAll(skipAttribute)];
    if (skipMatches.length > 1) {
      fail(`${relativeFile}:${action.name}: multiple [SkipIdempotency] attributes`);
    }

    const method = attribute[1].toUpperCase();
    const route = combineRoute(controllerRoute, attribute[2] ?? '', controllerName);
    mutations.push({
      serviceName,
      controllerName,
      actionName: action.name,
      method,
      path: route,
      exemptionReason: skipMatches[0]?.[1] ?? null,
      file: relativeFile,
    });
  }

  const skipCount = [...source.matchAll(skipAttribute)].length;
  return { controllerName, mutations, skipCount };
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
  const registration = new RegExp(
    `AddVietRideIdempotency\\s*\\(\\s*"${escapedPrefix}"\\s*,\\s*requireAllMutations\\s*:\\s*true\\s*\\)`,
  );
  if (!service.requireAllMutations || !registration.test(source)) {
    fail(`${serviceName}: Program.cs must register service prefix "${service.servicePrefix}" with requireAllMutations: true`);
  }
  if (!/\bUseVietRideIdempotency\s*\(\s*\)/.test(source)) {
    fail(`${serviceName}: Program.cs must call UseVietRideIdempotency()`);
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
  const approvedControllers = new Set(service.controllers ?? []);

  if (approvedControllers.size !== (service.controllers ?? []).length) {
    fail(`${serviceName}: duplicate controller entry in inventory`);
  }

  for (const mutation of mutations) {
    if (!mutation.exemptionReason && !approvedControllers.has(mutation.controllerName)) {
      fail(`${mutation.file}:${mutation.actionName}: ${mutation.method} ${mutation.path} is not covered by the controller inventory or [SkipIdempotency]`);
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

  const codeExemptions = mutations.filter((mutation) => mutation.exemptionReason);
  const discoveredSkipCount = scanned.reduce((total, controller) => total + controller.skipCount, 0);
  if (discoveredSkipCount !== codeExemptions.length) {
    fail(`${serviceName}: found ${discoveredSkipCount} [SkipIdempotency] attributes but only ${codeExemptions.length} are attached to mutation actions`);
  }

  const exemptionKey = (entry) => `${entry.method.toUpperCase()} ${normalizeRoute(entry.path)}`;
  const inventoryExemptions = new Map();
  for (const exemption of service.exemptions ?? []) {
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
      fail(`${exemption.file}:${exemption.actionName}: exemption ${key} is missing from the inventory`);
    } else if (approvedReason !== exemption.exemptionReason) {
      fail(`${exemption.file}:${exemption.actionName}: exemption reason mismatch for ${key}`);
    }
  }

  for (const key of inventoryExemptions.keys()) {
    if (!codeExemptionKeys.has(key)) {
      fail(`${serviceName}: inventory exemption ${key} is not attached to a mutation action`);
    }
  }

  return { controllers: approvedControllers.size, mutations: mutations.length, exemptions: codeExemptions.length };
}

const inventory = JSON.parse(fs.readFileSync(inventoryPath, 'utf8'));
if (inventory.version !== 1 || inventory.policy !== 'all-post-patch-put-delete') {
  fail('inventory must use version 1 and policy all-post-patch-put-delete');
}

const expectedServices = ['booking', 'payment', 'parcel'];
const actualServices = Object.keys(inventory.services ?? {}).sort();
if (JSON.stringify(actualServices) !== JSON.stringify([...expectedServices].sort())) {
  fail(`inventory services must be exactly: ${expectedServices.join(', ')}`);
}

const results = [];
for (const serviceName of expectedServices) {
  const service = inventory.services?.[serviceName];
  if (!service) continue;
  results.push([serviceName, validateService(serviceName, service)]);
}

if (errors.length > 0) {
  console.error('idempotency inventory FAIL');
  for (const error of errors) console.error(`- ${error}`);
  process.exitCode = 1;
} else {
  const details = results
    .map(([name, result]) => `${name}=${result.controllers} controllers/${result.mutations} mutations/${result.exemptions} exemptions`)
    .join(', ');
  console.log(`idempotency inventory PASS (${details})`);
}
