import { AppService } from './app.service';

// Legacy — AppController is NOT registered in AppModule for production.
// This test covers isolated class behavior only.
describe('AppService (legacy)', () => {
  it('getData should return "Hello API"', () => {
    const service = new AppService();
    expect(service.getData()).toEqual({ message: 'Hello API' });
  });
});
