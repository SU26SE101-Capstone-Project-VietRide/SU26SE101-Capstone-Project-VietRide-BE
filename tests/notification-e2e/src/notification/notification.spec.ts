import axios from 'axios';

describe('GET /health', () => {
  it('returns the Notification liveness envelope', async () => {
    const res = await axios.get('/health');

    expect(res.status).toBe(200);
    expect(res.data).toMatchObject({
      success: true,
      statusCode: 200,
      data: { status: 'ok', service: 'notification' },
    });
  });
});
