#!/usr/bin/env node
// PreToolUse guard for VietRide capstone invariants.
// Reads the hook payload on stdin and blocks (exit 2) on violations:
//   1. `git commit` carrying a Co-Authored-By trailer  -> capstone: contribution
//      must be attributed to a member only, no AI/co-author trailer.
//   2. `git commit ... --no-verify`                    -> never bypass hooks.
//   3. Editing a .csproj to add a Version= on a <PackageReference> -> CPM is on;
//      versions live only in Directory.Packages.props.
// Exit 0 = allow. Exit 2 = block, stderr is shown back to Claude.

import { readFileSync } from 'node:fs';

function readStdin() {
  try {
    return readFileSync(0, 'utf8');
  } catch {
    return '';
  }
}

function block(message) {
  process.stderr.write(message + '\n');
  process.exit(2);
}

let payload;
try {
  payload = JSON.parse(readStdin() || '{}');
} catch {
  process.exit(0); // malformed payload -> don't get in the way
}

const tool = payload.tool_name || '';
const input = payload.tool_input || {};

if (tool === 'Bash') {
  const cmd = String(input.command || '');
  const isCommit = /\bgit\b[^\n]*\bcommit\b/.test(cmd);
  if (isCommit && /co-?authored-by/i.test(cmd)) {
    block(
      "BLOCKED: commit message contains a 'Co-Authored-By' trailer.\n" +
        'Capstone rule (SU26SE101): contribution must be attributed to a member only.\n' +
        'Remove the Co-Authored-By line from the commit message and try again.',
    );
  }
  if (isCommit && /--no-verify\b/.test(cmd)) {
    block(
      "BLOCKED: 'git commit --no-verify' bypasses hooks.\n" +
        'Fix the underlying failure instead of skipping verification.',
    );
  }

  // Catch banned dependencies installed via the CLI (they bypass the Edit/Write
  // content check above; the git pre-commit hook is the only other net otherwise).
  const isDepInstall =
    /\bdotnet\s+add\b[^\n]*\bpackage\b/i.test(cmd) || /\bnpm\s+(?:i|install|add)\b/i.test(cmd);
  if (isDepInstall) {
    const banned = cmd.match(
      /\b(AutoMapper|OpenTelemetry|@opentelemetry\/[\w.-]+|prom-client|prometheus[\w.-]*|Grafana|Loki|Tempo)\b/i,
    );
    if (banned) {
      block(
        `BLOCKED: installing banned dependency "${banned[1]}".\n` +
          'BSOT bans AutoMapper (use Mapster/manual mapping) and the OpenTelemetry/\n' +
          'Prometheus/Grafana/Tempo/Loki stack (observability v1 = Serilog/Winston + Sentry + UptimeRobot).\n' +
          'Get explicit approval before adding ANY new dependency.',
      );
    }
  }

  process.exit(0);
}

if (tool === 'Edit' || tool === 'Write' || tool === 'MultiEdit') {
  const file = String(input.file_path || '');
  const lower = file.toLowerCase();

  let text = '';
  if (tool === 'Write') text = String(input.content || '');
  else if (tool === 'Edit') text = String(input.new_string || '');
  else if (Array.isArray(input.edits))
    text = input.edits.map((e) => String(e.new_string || '')).join('\n');

  // CPM: a .csproj <PackageReference> must not carry a Version= attribute.
  if (lower.endsWith('.csproj') && /<PackageReference\b[^>]*\bVersion\s*=/i.test(text)) {
    block(
      'BLOCKED: <PackageReference> with a Version= attribute in a .csproj.\n' +
        'Central Package Management (CPM) is enabled. Declare the version in\n' +
        'Directory.Packages.props as <PackageVersion Include="..." Version="..."/>\n' +
        'and keep the .csproj reference version-less: <PackageReference Include="..."/>.',
    );
  }

  // Banned .NET dependencies — match only real package declarations (not comments),
  // so editing the existing explanatory comment in Directory.Packages.props is safe.
  if (lower.endsWith('directory.packages.props') || lower.endsWith('.csproj')) {
    const banned = text.match(
      /<Package(?:Version|Reference)\b[^>]*Include="[^"]*(AutoMapper|OpenTelemetry|prometheus-net|Prometheus|Grafana|Loki|Tempo)[^"]*"/i,
    );
    if (banned) {
      block(
        `BLOCKED: banned .NET dependency "${banned[1]}".\n` +
          'BSOT bans AutoMapper (use Mapster/manual mapping) and the OpenTelemetry/\n' +
          'Prometheus/Grafana/Tempo/Loki stack (observability v1 = Serilog + Sentry + UptimeRobot).\n' +
          'Get explicit approval before adding ANY new dependency.',
      );
    }
    // MediatR must stay on v11.x — v12+ is commercially licensed.
    if (/<PackageVersion\b[^>]*Include="MediatR"[^>]*Version="\s*(?:1[2-9]|[2-9]\d)/i.test(text)) {
      block('BLOCKED: MediatR v12+ is commercially licensed. Pin MediatR to 11.x (BSOT §2.1).');
    }
  }

  // Banned npm dependencies.
  if (lower.endsWith('package.json')) {
    const npm = text.match(/("@opentelemetry\/[^"]+"|"prom-client"|"prometheus[^"]*")/i);
    if (npm) {
      block(
        `BLOCKED: banned npm dependency ${npm[1]}.\n` +
          'Observability v1 = Winston/Serilog + Sentry + UptimeRobot — no OpenTelemetry/Prometheus.\n' +
          'Get explicit approval before adding ANY new dependency.',
      );
    }
  }

  process.exit(0);
}

process.exit(0);
