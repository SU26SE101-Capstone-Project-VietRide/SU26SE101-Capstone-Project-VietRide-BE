// Point git at the committed .githooks/ directory. Run automatically by the npm `prepare`
// script; safe to run anywhere (no-op if git is unavailable).
import { execFileSync } from 'node:child_process';

try {
  execFileSync('git', ['config', 'core.hooksPath', '.githooks'], { stdio: 'ignore' });
  process.stdout.write('[githooks] core.hooksPath -> .githooks\n');
} catch {
  // Not a git checkout (e.g. tarball install) — nothing to do.
}
