import { NotificationsModule } from './notifications.module';
import { NotificationsRealtimeGateway } from './notifications-realtime.gateway';

describe('NotificationsModule realtime wiring', () => {
  it('registers the realtime gateway as a Nest provider', () => {
    const providers = Reflect.getMetadata('providers', NotificationsModule) as unknown[] | undefined;
    expect(providers).toContain(NotificationsRealtimeGateway);
  });
});
