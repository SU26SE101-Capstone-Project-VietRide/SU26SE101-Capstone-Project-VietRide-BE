// commit-msg hook: reject a Co-Authored-By trailer. Arg 1 = path to the commit message file.
import { readFileSync } from 'node:fs';
import { checkCommitMessage } from './rules.mjs';

const msgFile = process.argv[2];
if (!msgFile) process.exit(0);

let message = '';
try {
  message = readFileSync(msgFile, 'utf8');
} catch {
  process.exit(0);
}

const err = checkCommitMessage(message);
if (err) {
  process.stderr.write('\n[githooks/commit-msg] BLOCKED: ' + err + '\n\n');
  process.exit(1);
}
process.exit(0);
