import axios from 'axios';

/**
 * E2E smoke against a running Gateway (assumes `gateway:serve` is up on port 3000).
 * Run via `npx nx run gateway-e2e:e2e`. Excluded from the default unit `nx test`
 * run (see nx.json jest plugin `exclude`).
 */
describe('Gateway /health', () => {
  it('returns 200 with service identifier', async () => {
    const res = await axios.get(`/health`);

    expect(res.status).toBe(200);
    expect(res.data).toMatchObject({
      success: true,
      statusCode: 200,
      data: { status: 'ok', service: 'Gateway' },
    });
  });

  it('exposes /ready readiness probe', async () => {
    // Readiness may flap if downstream services are not all healthy yet — accept
    // 200 (ready) OR 503 (not ready) but reject 404 / 5xx-other.
    const res = await axios.get(`/ready`, { validateStatus: () => true });

    expect([200, 503]).toContain(res.status);
  });
});
