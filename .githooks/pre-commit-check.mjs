// pre-commit hook: inspect STAGED file content for CPM / banned-dependency violations.
import { execFileSync } from 'node:child_process';
import { checkFileContent } from './rules.mjs';

function git(args) {
  return execFileSync('git', args, { encoding: 'utf8' });
}

let staged = [];
try {
  staged = git(['diff', '--cached', '--name-only', '--diff-filter=ACM'])
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean);
} catch {
  process.exit(0);
}

const interesting = staged.filter((f) => {
  const l = f.toLowerCase();
  return (
    l.endsWith('.csproj') || l.endsWith('directory.packages.props') || l.endsWith('package.json')
  );
});

const violations = [];
for (const f of interesting) {
  let text = '';
  try {
    text = git(['show', `:${f}`]); // staged blob content
  } catch {
    continue;
  }
  const err = checkFileContent(f, text);
  if (err) violations.push(`  ${f}: ${err}`);
}

if (violations.length) {
  process.stderr.write('\n[githooks/pre-commit] BLOCKED:\n' + violations.join('\n') + '\n\n');
  process.exit(1);
}
process.exit(0);
