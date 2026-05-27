// Shared, tool-agnostic rule checks for VietRide git hooks.
// Mirrors the Claude Code PreToolUse guard (.claude/hooks/pre-guard.mjs) so the same
// invariants hold whether a commit is made by Claude Code, OpenCode, Codex CLI, or by hand.

/** Returns an error string if the commit message violates the Co-Authored-By rule, else null. */
export function checkCommitMessage(message) {
  // Ignore git comment lines (start with #).
  const body = String(message)
    .split('\n')
    .filter((l) => !l.startsWith('#'))
    .join('\n');
  if (/co-?authored-by/i.test(body)) {
    return (
      "commit message contains a 'Co-Authored-By' trailer.\n" +
      'Capstone rule (SU26SE101): contribution must be attributed to a member only — remove it.'
    );
  }
  return null;
}

/** Returns an error string if the staged file content violates a dependency/CPM rule, else null. */
export function checkFileContent(path, text) {
  const lower = String(path).toLowerCase();

  // CPM: a .csproj <PackageReference> must not carry a Version= attribute.
  if (lower.endsWith('.csproj') && /<PackageReference\b[^>]*\bVersion\s*=/i.test(text)) {
    return (
      '<PackageReference> carries a Version= attribute (CPM is on).\n' +
      'Move the version to Directory.Packages.props as <PackageVersion .../>; keep the reference version-less.'
    );
  }

  // Banned .NET dependencies — match real package declarations only (not comments).
  if (lower.endsWith('directory.packages.props') || lower.endsWith('.csproj')) {
    const banned = text.match(
      /<Package(?:Version|Reference)\b[^>]*Include="[^"]*(AutoMapper|OpenTelemetry|prometheus-net|Prometheus|Grafana|Loki|Tempo)[^"]*"/i,
    );
    if (banned) {
      return (
        `banned .NET dependency "${banned[1]}".\n` +
        'BSOT bans AutoMapper and the OpenTelemetry/Prometheus/Grafana/Tempo/Loki stack ' +
        '(observability v1 = Serilog + Sentry + UptimeRobot).'
      );
    }
    if (/<PackageVersion\b[^>]*Include="MediatR"[^>]*Version="\s*(?:1[2-9]|[2-9]\d)/i.test(text)) {
      return 'MediatR v12+ is commercially licensed. Pin MediatR to 11.x (BSOT §2.1).';
    }
  }

  // Banned npm dependencies.
  if (lower.endsWith('package.json')) {
    const npm = text.match(/("@opentelemetry\/[^"]+"|"prom-client"|"prometheus[^"]*")/i);
    if (npm) {
      return (
        `banned npm dependency ${npm[1]}.\n` +
        'Observability v1 = Winston/Serilog + Sentry + UptimeRobot — no OpenTelemetry/Prometheus.'
      );
    }
  }

  return null;
}
