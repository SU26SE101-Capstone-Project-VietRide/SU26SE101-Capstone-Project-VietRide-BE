#!/usr/bin/env node
// PostToolUse formatter for VietRide. Runs after Edit/Write/MultiEdit and formats
// ONLY the file that was just touched, so it never triggers a full build:
//   .cs                    -> dotnet format <service-or-libs.sln> --include <file>
//   .ts/.tsx/.js/.json/...  -> npx prettier --write <file>
// Best-effort: always exits 0 so a formatter hiccup never blocks the agent.

import { readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { relative, sep } from 'node:path';

function readStdin() {
  try {
    return readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

let payload;
try {
  payload = JSON.parse(readStdin() || '{}');
} catch {
  process.exit(0);
}

const input = payload.tool_input || {};
const file = String(input.file_path || '');
if (!file) process.exit(0);

const root = process.cwd();
const rel = relative(root, file).split(sep).join('/').toLowerCase();

// Skip generated / vendored locations.
if (/(^|\/)(node_modules|dist|bin|obj|\.nx)\//.test('/' + rel)) process.exit(0);

const lower = file.toLowerCase();

function run(cmd, args, label) {
  const res = spawnSync(cmd, args, {
    cwd: root,
    shell: true,
    encoding: 'utf8',
    timeout: 120000,
  });
  if (res.status === 0) {
    process.stdout.write(`[format-on-edit] ${label} OK\n`);
  } else {
    // Non-fatal: surface a short note, don't block.
    const msg = (res.stderr || res.stdout || '').trim().split('\n').slice(-3).join(' ');
    process.stdout.write(`[format-on-edit] ${label} skipped/failed: ${msg}\n`);
  }
}

// Map a .cs file to the solution that owns it.
function solutionFor(relPath) {
  const m = relPath.match(/^apps\/(identity|trip|booking|payment|parcel)\//);
  if (m) {
    const svc = m[1].charAt(0).toUpperCase() + m[1].slice(1);
    return `apps/${m[1]}/VietRide.${svc}.sln`;
  }
  if (relPath.startsWith('libs/dotnet/')) return 'libs/dotnet/VietRide.Libs.sln';
  return null;
}

if (lower.endsWith('.cs')) {
  const sln = solutionFor(rel);
  if (!sln) process.exit(0);
  // Pass the repo-relative path to --include: `dotnet format` filters by paths
  // relative to the working dir, so an absolute Windows path may match nothing.
  const relForInclude = relative(root, file).split(sep).join('/');
  run(
    'dotnet',
    ['format', sln, '--include', `"${relForInclude}"`, '--no-restore', '--verbosity', 'quiet'],
    `dotnet format ${sln}`,
  );
  process.exit(0);
}

if (/\.(ts|tsx|js|jsx|mjs|cjs|json|md|yml|yaml)$/.test(lower)) {
  run('npx', ['prettier', '--write', `"${file}"`], 'prettier');
  process.exit(0);
}

process.exit(0);
