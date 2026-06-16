import * as dotenv from 'dotenv';
import * as path from 'node:path';

const candidates = [
  path.resolve(process.cwd(), '.env'),
  path.resolve(__dirname, '../../../.env'),
  path.resolve(__dirname, '../../../../.env'),
];

for (const file of candidates) {
  const result = dotenv.config({ path: file, override: false });
  if (!result.error) break;
}
