// Loaded as the very first import in main.ts so env vars are populated
// before AppModule's top-level `loadEnv()` runs at module-load time.
import * as dotenv from 'dotenv';
import * as path from 'node:path';

// Try workspace root variants (works for both `nx serve` dev and bundled prod).
const candidates = [
  path.resolve(process.cwd(), '.env'),
  path.resolve(__dirname, '../../../.env'),
  path.resolve(__dirname, '../../../../.env'),
];

for (const file of candidates) {
  const result = dotenv.config({ path: file, override: false });
  if (!result.error) break;
}
